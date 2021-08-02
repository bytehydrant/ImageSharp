// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using SixLabors.ImageSharp.Formats.Webp.BitReader;
using SixLabors.ImageSharp.Formats.Webp.Lossless;
using SixLabors.ImageSharp.Formats.Webp.Lossy;
using SixLabors.ImageSharp.IO;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.Metadata.Animation;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Webp
{
    /// <summary>
    /// Performs the webp decoding operation.
    /// </summary>
    internal sealed class WebpDecoderCore : IImageDecoderInternals
    {
        /// <summary>
        /// Reusable buffer.
        /// </summary>
        private readonly byte[] buffer = new byte[4];

        /// <summary>
        /// Used for allocating memory during processing operations.
        /// </summary>
        private readonly MemoryAllocator memoryAllocator;

        /// <summary>
        /// The stream to decode from.
        /// </summary>
        private Stream currentStream;

        /// <summary>
        /// The webp specific metadata.
        /// </summary>
        private WebpMetadata webpMetadata;

        private WebpChunkType currentChunkType;

        private uint currentChunkSize;

        private ISize size;

        /// <summary>
        /// Initializes a new instance of the <see cref="WebpDecoderCore"/> class.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <param name="options">The options.</param>
        public WebpDecoderCore(Configuration configuration, IWebpDecoderOptions options)
        {
            this.Configuration = configuration;
            this.memoryAllocator = configuration.MemoryAllocator;
            this.IgnoreMetadata = options.IgnoreMetadata;
        }

        /// <summary>
        /// Gets a value indicating whether the metadata should be ignored when the image is being decoded.
        /// </summary>
        public bool IgnoreMetadata { get; }

        /// <summary>
        /// Gets the <see cref="ImageMetadata"/> decoded by this decoder instance.
        /// </summary>
        public ImageMetadata Metadata { get; private set; }

        /// <inheritdoc/>
        public Configuration Configuration { get; }

        /// <summary>
        /// Gets the dimensions of the image.
        /// </summary>
        public Size Dimensions => new Size((int)this.size.Width, (int)this.size.Height);

        /// <inheritdoc />
        public Image<TPixel> Decode<TPixel>(BufferedReadStream stream, CancellationToken cancellationToken)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            this.currentStream = stream;
            this.Metadata = new ImageMetadata();
            this.webpMetadata = this.Metadata.GetFormatMetadata(WebpFormat.Instance);

            WebpFeatures features = null;
            Image<TPixel> image = null;

            this.ReadImageHeader();
            this.NextChunk();

            if (this.currentChunkType == WebpChunkType.Vp8X)
            {
                this.size = features = this.ReadVp8XHeader();
                this.NextChunk();
            }

            if (features?.IccProfile == true)
            {
                this.ReadIccpChunk();
                this.NextChunk();
            }

            if (features?.Animation == true)
            {
                this.ReadAnimationChunk();
                this.NextChunk();
            }

            ImageFrame<TPixel> currentFrame = null;
            ImageFrame<TPixel> previousFrame = null;
            do
            {
                FrameAnimationInfo<TPixel> animationInfo = null;
                if (features?.Animation == true)
                {
                    animationInfo = this.ReadAnimationFrameChunk<TPixel>();
                    animationInfo.PreviousFrame = previousFrame;
                    this.NextChunk();
                }

                FrameAlphaInfo alphaInfo = null;
                try
                {
                    if (this.currentChunkType == WebpChunkType.Alpha)
                    {
                        if (features?.Alpha == false)
                        {
                            WebpThrowHelper.ThrowImageFormatException("Unexpected ALPH chunk");
                        }

                        alphaInfo = this.ReadAlphaChunk();
                        this.NextChunk();
                    }

                    switch (this.currentChunkType)
                    {
                        case WebpChunkType.Vp8:
                            Vp8FrameInfo vp8Frameinfo = this.ReadVp8Header();
                            this.size ??= vp8Frameinfo;
                            this.EnsureImageAndFrame(ref image, ref currentFrame, this.size);
                            using (var reader = new Vp8BitReader(this.currentStream, vp8Frameinfo.DataSize, this.memoryAllocator, vp8Frameinfo.PartitionLength) { Remaining = vp8Frameinfo.DataSize })
                            {
                                var lossyDecoder = new WebPLossyDecoder(reader, this.memoryAllocator, this.Configuration);
                                lossyDecoder.Decode(currentFrame.PixelBuffer, image.Width, image.Height, vp8Frameinfo, alphaInfo, animationInfo);
                            }

                            break;
                        case WebpChunkType.Vp8L:
                            using (var bitReader = new Vp8LBitReader(this.currentStream, this.currentChunkSize, this.memoryAllocator))
                            {
                                Vp8LFrameInfo frameInfo = this.ReadVp8LHeader(bitReader);
                                this.size ??= frameInfo;
                                this.EnsureImageAndFrame(ref image, ref currentFrame, this.size);
                                var losslessDecoder = new WebPLosslessDecoder(bitReader, this.memoryAllocator, this.Configuration);
                                losslessDecoder.Decode(currentFrame.PixelBuffer, image.Width, image.Height);
                            }

                            break;
                        default:
                            WebpThrowHelper.ThrowImageFormatException("Unrecognized VP8 header");
                            break; // unreachable
                    }

                    if (features?.Animation == true && animationInfo != null)
                    {
                        WebpFrameMetadata frameMetadata = currentFrame.Metadata.GetWebpMetadata();
                        frameMetadata.FrameDelay = (int)animationInfo.FrameDuration;
                    }

                    previousFrame = currentFrame;
                }
                finally
                {
                    alphaInfo?.Dispose();
                }

                this.NextChunk();
            }
            while (features?.Animation == true && this.currentChunkType != WebpChunkType.Exif && this.currentChunkType != WebpChunkType.Xmp && this.currentStream.Position < this.currentStream.Length);

            // There can be optional chunks after the image data, like EXIF and XMP.
            while (!this.IgnoreMetadata && this.currentStream.Position < this.currentStream.Length && features?.ExifProfile == true)
            {
                if (this.currentChunkType == WebpChunkType.Exif && features?.ExifProfile == true && this.Metadata.ExifProfile == null)
                {
                    var exifData = new byte[this.currentChunkSize];
                    this.currentStream.Read(exifData, 0, (int)this.currentChunkSize);
                    this.Metadata.ExifProfile = new ExifProfile(exifData);
                }
                else
                {
                    // Skip XMP chunk data or any duplicate EXIF chunk.
                    this.currentStream.Skip((int)this.currentChunkSize);
                }

                this.NextChunk();
            }

            return image;
        }

        private void EnsureImageAndFrame<TPixel>(ref Image<TPixel> image, ref ImageFrame<TPixel> currentFrame, ISize dimensions)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            image ??= new Image<TPixel>(this.Configuration, (int)dimensions.Width, (int)dimensions.Height, this.Metadata);
            currentFrame = (currentFrame == null) ? image.Frames[0] : image.Frames.CreateFrame((int)dimensions.Width, (int)dimensions.Height);
        }

        private FrameAlphaInfo ReadAlphaChunk()
        {
            // reduce by 1 in anticipation of first reading the "Header" byte below
            int alphaDataSize = (int)(this.currentChunkSize - 1);
            var alphaInfo = new FrameAlphaInfo
            {
                AlphaChunkHeader = (byte)this.currentStream.ReadByte(),
                AlphaData = this.memoryAllocator.Allocate<byte>(alphaDataSize)
            };
            this.currentStream.Read(alphaInfo.AlphaData.Memory.Span, 0, alphaDataSize);
            return alphaInfo;
        }

        private FrameAnimationInfo<TPixel> ReadAnimationFrameChunk<TPixel>()
            where TPixel : unmanaged, IPixel<TPixel>
        {
            if (this.currentChunkType != WebpChunkType.Animation)
            {
                WebpThrowHelper.ThrowImageFormatException("Expected ANMF chunk");
            }

            var animationInfo = new FrameAnimationInfo<TPixel>
            {
                FrameX = this.currentStream.ReadUint24(this.buffer) * 2,
                FrameY = this.currentStream.ReadUint24(this.buffer) * 2,
                Width = this.currentStream.ReadUint24(this.buffer) + 1,
                Height = this.currentStream.ReadUint24(this.buffer) + 1,
                FrameDuration = this.currentStream.ReadUint24(this.buffer)
            };
            byte bits = (byte)this.currentStream.ReadByte();
            animationInfo.AlphaBlend = (bits & 0x2) == 0;
            animationInfo.DisposeToBackground = (bits & 0x1) != 0;
            return animationInfo;
        }

        private void ReadAnimationChunk()
        {
            if (this.currentChunkType != WebpChunkType.AnimationParameter)
            {
                WebpThrowHelper.ThrowImageFormatException("Expected ANIM chunk");
            }

            this.currentStream.Skip(4); // TODO background color, note, spec says this is optional, so low pri
            this.currentStream.Read(this.buffer, 0, 2);
            uint loopCount = BinaryPrimitives.ReadUInt16LittleEndian(this.buffer);
            this.Metadata.GetWebpMetadata().LoopCount = (int)loopCount;
        }

        private void ReadIccpChunk()
        {
            if (this.currentChunkType != WebpChunkType.Iccp)
            {
                WebpThrowHelper.ThrowImageFormatException("Expected ICCP chunk");
            }

            if (this.IgnoreMetadata)
            {
                this.currentStream.Skip((int)this.currentChunkSize);
            }
            else
            {
                var iccpData = new byte[this.currentChunkSize];
                this.currentStream.Read(iccpData, 0, (int)this.currentChunkSize);
                var profile = new IccProfile(iccpData);
                if (profile.CheckIsValid())
                {
                    this.Metadata.IccProfile = profile;
                }
            }
        }

        /// <inheritdoc />
        public IImageInfo Identify(BufferedReadStream stream, CancellationToken cancellationToken)
        {
            this.currentStream = stream;
            this.Metadata = new ImageMetadata();
            this.webpMetadata = this.Metadata.GetFormatMetadata(WebpFormat.Instance);

            this.ReadImageHeader();
            this.NextChunk();

            WebpFeatures features = null;

            if (this.currentChunkType == WebpChunkType.Vp8X)
            {
                features = this.ReadVp8XHeader();
                return new ImageInfo(new PixelTypeInfo(features.Alpha ? 32 : 24), (int)features.Width, (int)features.Height, this.Metadata);
            }

            // TODO jumping over these chunks skips ICCP, which would have ended up in the Metadata. Also note the early return above if this gets added later
            while (this.currentChunkType != WebpChunkType.Vp8 && this.currentChunkType != WebpChunkType.Vp8L)
            {
                this.currentStream.Skip((int)this.currentChunkSize);
                this.NextChunk();
            }

            if (this.currentChunkType == WebpChunkType.Vp8)
            {
                Vp8FrameInfo frameInfo = this.ReadVp8Header();
                return new ImageInfo(new PixelTypeInfo(features.Alpha ? 32 : 24), (int)frameInfo.Width, (int)frameInfo.Height, this.Metadata);
            }

            using (var bitReader = new Vp8LBitReader(this.currentStream, this.currentChunkSize, this.memoryAllocator))
            {
                Vp8LFrameInfo frameInfo = this.ReadVp8LHeader(bitReader);
                return new ImageInfo(new PixelTypeInfo(32), (int)frameInfo.Width, (int)frameInfo.Height, this.Metadata);
            }
        }

        /// <summary>
        /// Reads and skips over the image header.
        /// </summary>
        /// <returns>The file size in bytes.</returns>
        private uint ReadImageHeader()
        {
            // Skip FourCC header, we already know its a RIFF file at this point.
            this.currentStream.Skip(4);

            // Read file size.
            // The size of the file in bytes starting at offset 8.
            // The file size in the header is the total size of the chunks that follow plus 4 bytes for the ‘WEBP’ FourCC.
            uint fileSize = this.ReadChunkSize();

            // Skip 'WEBP' from the header.
            this.currentStream.Skip(4);

            return fileSize;
        }

        private void NextChunk()
        {
            while (this.currentStream.Position < this.currentStream.Length)
            {
                this.currentChunkType = this.ReadChunkType();
                this.currentChunkSize = this.ReadChunkSize();

                if (Enum.IsDefined(typeof(WebpChunkType), this.currentChunkType))
                {
                    break;
                }

                this.currentStream.Skip((int)this.currentChunkSize);
            }
        }

        private uint ReadChunkSize()
        {
            this.currentStream.Read(this.buffer, 0, 4);
            return (BinaryPrimitives.ReadUInt32LittleEndian(this.buffer) + 1) & ~0x1U;
        }

        private WebpChunkType ReadChunkType()
        {
            this.currentStream.Read(this.buffer, 0, 4);
            return (WebpChunkType)BinaryPrimitives.ReadUInt32BigEndian(this.buffer);
        }

        /// <summary>
        /// Reads an the extended webp file header. An extended file header consists of:
        /// - A 'VP8X' chunk with information about features used in the file.
        /// - An optional 'ICCP' chunk with color profile.
        /// - An optional 'ANIM' chunk with animation control data.
        /// - An optional 'ALPH' chunk with alpha channel data.
        /// After the image header, image data will follow. After that optional image metadata chunks (EXIF and XMP) can follow.
        /// </summary>
        private WebpFeatures ReadVp8XHeader()
        {
            var features = new WebpFeatures();

            // The first byte contains information about the image features used.
            byte imageFeatures = (byte)this.currentStream.ReadByte();

            // The first two bit of it are reserved and should be 0.
            if (imageFeatures >> 6 != 0)
            {
                WebpThrowHelper.ThrowImageFormatException("first two bits of the VP8X header are expected to be zero");
            }

            // If bit 3 is set, a ICC Profile Chunk should be present.
            features.IccProfile = (imageFeatures & (1 << 5)) != 0;

            // If bit 4 is set, any of the frames of the image contain transparency information ("alpha" chunk).
            features.Alpha = (imageFeatures & (1 << 4)) != 0;

            // If bit 5 is set, a EXIF metadata should be present.
            features.ExifProfile = (imageFeatures & (1 << 3)) != 0;

            // If bit 6 is set, XMP metadata should be present.
            features.XmpMetaData = (imageFeatures & (1 << 2)) != 0;

            // If bit 7 is set, animation should be present.
            features.Animation = (imageFeatures & (1 << 1)) != 0;

            // 3 reserved bytes should follow which are supposed to be zero.
            this.currentStream.Read(this.buffer, 0, 3);
            if (this.buffer[0] != 0 || this.buffer[1] != 0 || this.buffer[2] != 0)
            {
                WebpThrowHelper.ThrowImageFormatException("reserved bytes should be zero");
            }

            // Width and Height are both spec'd as value-minus-1, hence the +1 on the end of each
            features.Width = this.currentStream.ReadUint24(this.buffer) + 1;
            features.Height = this.currentStream.ReadUint24(this.buffer) + 1;

            return features;
        }

        /// <summary>
        /// Reads the header of a lossy webp image.
        /// </summary>
        private Vp8FrameInfo ReadVp8Header()
        {
            this.webpMetadata.Format = WebpFormatType.Lossy;

            // remaining counts the available image data payload.
            uint remaining = this.currentChunkSize;

            // Paragraph 9.1 https://tools.ietf.org/html/rfc6386#page-30
            // Frame tag that contains four fields:
            // - A 1-bit frame type (0 for key frames, 1 for interframes).
            // - A 3-bit version number.
            // - A 1-bit show_frame flag.
            // - A 19-bit field containing the size of the first data partition in bytes.
            this.currentStream.Read(this.buffer, 0, 3);
            uint frameTag = (uint)(this.buffer[0] | (this.buffer[1] << 8) | (this.buffer[2] << 16));
            remaining -= 3;
            bool isNoKeyFrame = (frameTag & 0x1) == 1;
            if (isNoKeyFrame)
            {
                WebpThrowHelper.ThrowImageFormatException("VP8 header indicates the image is not a key frame");
            }

            uint version = (frameTag >> 1) & 0x7;
            if (version > 3)
            {
                WebpThrowHelper.ThrowImageFormatException($"VP8 header indicates unknown profile {version}");
            }

            bool invisibleFrame = ((frameTag >> 4) & 0x1) == 0;
            if (invisibleFrame)
            {
                WebpThrowHelper.ThrowImageFormatException("VP8 header indicates that the first frame is invisible");
            }

            uint partitionLength = frameTag >> 5;
            if (partitionLength > this.currentChunkSize)
            {
                WebpThrowHelper.ThrowImageFormatException("VP8 header contains inconsistent size information");
            }

            // Check for VP8 magic bytes.
            this.currentStream.Read(this.buffer, 0, 3);
            if (!this.buffer.AsSpan().Slice(0, 3).SequenceEqual(WebpConstants.Vp8HeaderMagicBytes))
            {
                WebpThrowHelper.ThrowImageFormatException("VP8 magic bytes not found");
            }

            this.currentStream.Read(this.buffer, 0, 4);
            uint tmp = (uint)BinaryPrimitives.ReadInt16LittleEndian(this.buffer);
            uint width = tmp & 0x3fff;
            sbyte xScale = (sbyte)(tmp >> 6);
            tmp = (uint)BinaryPrimitives.ReadInt16LittleEndian(this.buffer.AsSpan(2));
            uint height = tmp & 0x3fff;
            sbyte yScale = (sbyte)(tmp >> 6);
            remaining -= 7;
            if (width == 0 || height == 0)
            {
                WebpThrowHelper.ThrowImageFormatException("width or height can not be zero");
            }

            if (partitionLength > remaining)
            {
                WebpThrowHelper.ThrowImageFormatException("bad partition length");
            }

            return new Vp8FrameInfo()
            {
                Width = width,
                Height = height,
                XScale = xScale,
                YScale = yScale,
                PartitionLength = partitionLength,
                Profile = (sbyte)version,
                KeyFrame = true,
                DataSize = remaining
            };
        }

        /// <summary>
        /// .
        /// </summary>
        /// <param name="bitReader">.</param>
        /// <returns>.</returns>
        private Vp8LFrameInfo ReadVp8LHeader(Vp8LBitReader bitReader)
        {
            this.webpMetadata.Format = WebpFormatType.Lossless;

            // One byte signature, should be 0x2f.
            uint signature = bitReader.ReadValue(8);
            if (signature != WebpConstants.Vp8LHeaderMagicByte)
            {
                WebpThrowHelper.ThrowImageFormatException("Invalid VP8L signature");
            }

            // The first 28 bits of the bitstream specify the width and height of the image.
            uint width = bitReader.ReadValue(WebpConstants.Vp8LImageSizeBits) + 1;
            uint height = bitReader.ReadValue(WebpConstants.Vp8LImageSizeBits) + 1;
            if (width == 0 || height == 0)
            {
                WebpThrowHelper.ThrowImageFormatException("invalid width or height read");
            }

            // The alphaIsUsed flag should be set to 0 when all alpha values are 255 in the picture, and 1 otherwise.
            // TODO: this flag value is not used yet
            bool alphaIsUsed = bitReader.ReadBit();

            // The next 3 bits are the version. The version number is a 3 bit code that must be set to 0.
            // Any other value should be treated as an error.
            uint version = bitReader.ReadValue(WebpConstants.Vp8LVersionBits);
            if (version != 0)
            {
                WebpThrowHelper.ThrowNotSupportedException($"Unexpected version number {version} found in VP8L header");
            }

            return new Vp8LFrameInfo()
            {
                Width = width,
                Height = height
            };
        }
    }
}
