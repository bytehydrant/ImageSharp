// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.Formats.Webp
{
    internal class Vp8LFrameInfo : ISize
    {
        public Vp8LFrameInfo()
        {
        }

        public uint Width { get; set; }
        public uint Height { get; set; }
    }
}
