// ============================================================================
// Product:   RtfBridge.SystemDrawing
// File:      SystemDrawingImageRenderer.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RtfBridge.SystemDrawing;

/// <summary>
/// Rasterizes WMF/EMF picture payloads to PNG via <see cref="Metafile"/> (GDI+).
/// Windows-only. On non-Windows hosts <see cref="Render"/> returns <c>null</c> so the core
/// library falls back to its inline placeholder + warning.
/// </summary>
#if NET5_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public sealed class SystemDrawingImageRenderer : IImageRenderer
{
    /// <summary>Target DPI used when rasterizing. Defaults to 96.</summary>
    public float TargetDpi { get; set; } = 96f;

    /// <summary>Optional cap on output pixel dimensions. Metafiles that would exceed this are downscaled.</summary>
    public int MaxOutputDimensionPx { get; set; } = 4096;

    /// <inheritdoc />
    public bool CanRender(RtfImageKind kind) =>
        kind == RtfImageKind.Wmf || kind == RtfImageKind.Emf;

    /// <inheritdoc />
    public RenderedImage? Render(byte[] vectorBytes, RtfImageKind kind)
    {
        if (vectorBytes == null || vectorBytes.Length == 0) return null;
#if NET5_0_OR_GREATER
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return null;
#endif
        if (!CanRender(kind)) return null;

        try
        {
            using var ms = new MemoryStream(vectorBytes, writable: false);
            using var mf = new Metafile(ms);

            var header = mf.GetMetafileHeader();
            float dpiX = header.DpiX > 0 ? header.DpiX : 96f;
            float dpiY = header.DpiY > 0 ? header.DpiY : 96f;

            // Compute pixel size at target DPI. Metafile bounds are in the metafile's own unit space.
            GraphicsUnit unit = GraphicsUnit.Pixel;
            var boundsF = mf.GetBounds(ref unit);
            float widthPx = boundsF.Width;
            float heightPx = boundsF.Height;

            // Normalize bounds to pixels.
            switch (unit)
            {
                case GraphicsUnit.Pixel:
                    break;
                case GraphicsUnit.Point:
                    widthPx = boundsF.Width * TargetDpi / 72f;
                    heightPx = boundsF.Height * TargetDpi / 72f;
                    break;
                case GraphicsUnit.Inch:
                    widthPx = boundsF.Width * TargetDpi;
                    heightPx = boundsF.Height * TargetDpi;
                    break;
                case GraphicsUnit.Millimeter:
                    widthPx = boundsF.Width * TargetDpi / 25.4f;
                    heightPx = boundsF.Height * TargetDpi / 25.4f;
                    break;
                case GraphicsUnit.Document:
                    widthPx = boundsF.Width * TargetDpi / 300f;
                    heightPx = boundsF.Height * TargetDpi / 300f;
                    break;
                default:
                    // HIMETRIC etc. — fall back to source DPI scaling.
                    widthPx = boundsF.Width * TargetDpi / dpiX;
                    heightPx = boundsF.Height * TargetDpi / dpiY;
                    break;
            }

            int w = (int)Math.Ceiling(widthPx);
            int h = (int)Math.Ceiling(heightPx);
            if (w <= 0 || h <= 0) return null;

            // Cap.
            if (w > MaxOutputDimensionPx || h > MaxOutputDimensionPx)
            {
                double scale = (double)MaxOutputDimensionPx / Math.Max(w, h);
                w = Math.Max(1, (int)(w * scale));
                h = Math.Max(1, (int)(h * scale));
            }

            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            bmp.SetResolution(TargetDpi, TargetDpi);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.PageUnit = GraphicsUnit.Pixel;
                g.DrawImage(mf, new Rectangle(0, 0, w, h));
            }

            using var outStream = new MemoryStream();
            bmp.Save(outStream, ImageFormat.Png);
            return new RenderedImage("image/png", outStream.ToArray(), w, h);
        }
        catch
        {
            // Corrupt or unsupported metafile — signal via null so caller emits the placeholder.
            return null;
        }
    }
}
