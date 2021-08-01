// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.Metadata.Animation;

namespace SixLabors.ImageSharp.Formats.Webp
{
    /// <summary>
    /// WebpFrameMetadata
    /// </summary>
    public class WebpFrameMetadata : IDeepCloneable, IAnimationFrameMetadata
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WebpFrameMetadata"/> class.
        /// </summary>
        public WebpFrameMetadata()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WebpFrameMetadata"/> class.
        /// </summary>
        /// <param name="other">other</param>
        public WebpFrameMetadata(WebpFrameMetadata other) => this.FrameDelay = other.FrameDelay;

        /// <summary>
        /// Gets or sets the FrameDelay
        /// </summary>
        public int FrameDelay { get; set; }

        public TimeSpan FrameDuration => TimeSpan.FromMilliseconds(this.FrameDelay);

        /// <inheritdoc/>
        public IDeepCloneable DeepClone() => new WebpFrameMetadata(this);
    }
}
