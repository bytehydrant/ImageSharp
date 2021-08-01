// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Formats.Tga;
using SixLabors.ImageSharp.Metadata.Animation;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Formats.Webp
{
    internal class FrameAnimationInfo<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        public uint FrameX { get; internal set; }
        public uint FrameY { get; internal set; }
        public uint Width { get; internal set; }
        public uint Height { get; internal set; }
        public uint FrameDuration { get; internal set; }
        public bool AlphaBlend { get; internal set; }
        public bool DisposeToBackground { get; set; }

        public ImageFrame<TPixel> PreviousFrame { get; set; }

        public Rectangle GetClip()
        {
            return new Rectangle((int)this.FrameX, (int)this.FrameY, (int)this.Width, (int)this.Height);
        }
    }
}
