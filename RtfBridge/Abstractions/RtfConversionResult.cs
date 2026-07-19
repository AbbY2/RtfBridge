// ============================================================================
// Product:   RtfBridge
// File:      RtfConversionResult.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using System;
using System.Collections.Generic;

namespace RtfBridge;

/// <summary>Detailed result of an RTF-to-HTML conversion.</summary>
public sealed class RtfConversionResult
{
    /// <summary>The generated HTML.</summary>
    public string Html { get; }

    /// <summary>Non-fatal issues encountered during conversion.</summary>
    public IReadOnlyList<RtfConversionWarning> Warnings { get; }

    /// <summary>Images that were embedded in the output (for diagnostic / logging use).</summary>
    public IReadOnlyList<RtfEmbeddedImage> Images { get; }

    public RtfConversionResult(
        string html,
        IReadOnlyList<RtfConversionWarning>? warnings = null,
        IReadOnlyList<RtfEmbeddedImage>? images = null)
    {
        Html = html ?? throw new ArgumentNullException(nameof(html));
        Warnings = warnings ?? Array.Empty<RtfConversionWarning>();
        Images = images ?? Array.Empty<RtfEmbeddedImage>();
    }
}

/// <summary>A non-fatal issue encountered during conversion.</summary>
public sealed class RtfConversionWarning
{
    public string Code { get; }
    public string Message { get; }
    public int? RtfPosition { get; }

    public RtfConversionWarning(string code, string message, int? rtfPosition = null)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Message = message ?? throw new ArgumentNullException(nameof(message));
        RtfPosition = rtfPosition;
    }
}

/// <summary>An image embedded in the output HTML.</summary>
public sealed class RtfEmbeddedImage
{
    public string MediaType { get; }
    public int Width { get; }
    public int Height { get; }
    public byte[] Bytes { get; }

    public RtfEmbeddedImage(string mediaType, int width, int height, byte[] bytes)
    {
        MediaType = mediaType ?? throw new ArgumentNullException(nameof(mediaType));
        Width = width;
        Height = height;
        Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
    }
}
