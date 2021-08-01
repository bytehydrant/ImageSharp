// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;

namespace SixLabors.ImageSharp.Formats.Webp
{
    internal class FrameAlphaInfo : IDisposable
    {
        /// <summary>
        /// Gets or sets the alpha data, if an ALPH chunk is present.
        /// </summary>
        public IMemoryOwner<byte> AlphaData { get; set; }

        /// <summary>
        /// Gets or sets the alpha chunk header.
        /// </summary>
        public byte AlphaChunkHeader { get; set; }

        public void Dispose() => this.AlphaData?.Dispose();
    }
}
