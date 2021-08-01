// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.Formats.Webp.BitReader;
using SixLabors.ImageSharp.Formats.Webp.Lossy;

namespace SixLabors.ImageSharp.Formats.Webp
{
    internal class Vp8FrameInfo : ISize, IVp8FrameHeader
    {
        /// <summary>
        /// Gets or sets the bitmap width in pixels.
        /// </summary>
        public uint Width { get; set; }

        /// <summary>
        /// Gets or sets the bitmap height in pixels.
        /// </summary>
        public uint Height { get; set; }

        /// <summary>
        /// Gets or sets 
        /// </summary>
        public sbyte XScale { get; set; }

        public sbyte YScale { get; set; }

        public uint PartitionLength { get; set; }
        public sbyte Profile { get; set; }
        public bool KeyFrame { get; set; }
        public uint DataSize { get; internal set; }
        public uint XPosition { get; internal set; } = 0;
        public uint YPosition { get; internal set; } = 0;
    }
}
