// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text;
using SixLabors.ImageSharp.Formats.Gif;

namespace SixLabors.ImageSharp.Metadata.Animation
{
    /// <summary>
    /// Specifies shared animation frame metadata, e.g. for WebP and GIF
    /// </summary>
    public interface IAnimationFrameMetadata
    {
        /// <summary>
        /// Gets the duration for which the frame should be displayed
        /// </summary>
        TimeSpan FrameDuration { get; }
    }
}
