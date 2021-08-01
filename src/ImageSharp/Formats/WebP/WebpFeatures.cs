// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;

namespace SixLabors.ImageSharp.Formats.Webp
{
    /// <summary>
    /// Image features of a VP8X image.
    /// </summary>
    internal class WebpFeatures : ISize
    {
        /// <summary>
        /// Gets or sets a value indicating whether this image has an ICC Profile.
        /// </summary>
        public bool IccProfile { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this image has an alpha channel.
        /// </summary>
        public bool Alpha { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this image has an EXIF Profile.
        /// </summary>
        public bool ExifProfile { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this image has XMP Metadata.
        /// </summary>
        public bool XmpMetaData { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this image is an animation.
        /// </summary>
        public bool Animation { get; set; }

        public uint Width { get; set; }

        public uint Height { get; set; }
    }
}
