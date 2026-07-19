// ============================================================================
// Product:   RtfBridge
// File:      IImageRenderer.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using System;

namespace RtfBridge;

/// <summary>Category of an RTF picture payload.</summary>
public enum RtfImageKind
{
    Unknown = 0,
    Png,
    Jpeg,
    Emf,
    Wmf,
    Dib,
    WBitmap,
    PmMetafile,
}

/// <summary>A renderer capable of turning vector picture payloads (WMF/EMF) into raster bytes.</summary>
public interface IImageRenderer
{
    /// <summary>Returns true when this renderer can process the specified <paramref name="kind"/>.</summary>
    bool CanRender(RtfImageKind kind);

    /// <summary>Renders vector bytes into a raster image. Return null to skip.</summary>
    RenderedImage? Render(byte[] vectorBytes, RtfImageKind kind);
}

/// <summary>Result of rendering a vector image to raster.</summary>
public sealed class RenderedImage
{
    public string MediaType { get; }
    public byte[] Bytes { get; }
    public int Width { get; }
    public int Height { get; }

    public RenderedImage(string mediaType, byte[] bytes, int width, int height)
    {
        MediaType = mediaType ?? throw new ArgumentNullException(nameof(mediaType));
        Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
        Width = width;
        Height = height;
    }
}
