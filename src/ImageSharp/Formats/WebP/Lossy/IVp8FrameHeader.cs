// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.Formats.Webp.Lossy
{
    /// <summary>
    /// Vp8 frame header information.
    /// </summary>
    internal interface IVp8FrameHeader
    {
        /// <summary>
        /// Gets or sets a value indicating whether this is a key frame.
        /// </summary>
        bool KeyFrame { get; set; }

        /// <summary>
        /// Gets or sets Vp8 profile [0..3].
        /// </summary>
        sbyte Profile { get; set; }

        /// <summary>
        /// Gets or sets the partition length.
        /// </summary>
        uint PartitionLength { get; set; }
    }
}
