// ============================================================================
// Product:   RtfBridge.SystemDrawing.Tests
// File:      SystemDrawingImageRendererTests.cs
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
using System.Text;
using RtfBridge;
using RtfBridge.SystemDrawing;
using Xunit;

namespace RtfBridge.SystemDrawing.Tests;

public class SystemDrawingImageRendererTests
{
    private static bool IsWindows =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>Create a tiny valid EMF programmatically so tests don't need binary fixtures.</summary>
    private static byte[] CreateSampleEmfBytes()
    {
        using var ms = new MemoryStream();
        using (var refBmp = new Bitmap(1, 1))
        using (var refG = Graphics.FromImage(refBmp))
        {
            var hdc = refG.GetHdc();
            try
            {
                using var mf = new Metafile(ms, hdc, new Rectangle(0, 0, 40, 20), MetafileFrameUnit.Pixel, EmfType.EmfOnly);
                using (var g = Graphics.FromImage(mf))
                {
                    g.FillRectangle(Brushes.Red, 0, 0, 40, 20);
                    g.DrawLine(Pens.Black, 0, 0, 40, 20);
                }
            }
            finally
            {
                refG.ReleaseHdc(hdc);
            }
        }
        return ms.ToArray();
    }

    [Fact]
    public void Render_Emf_ProducesPngBytes()
    {
        if (!IsWindows) return; // silent skip on non-Windows

        var emf = CreateSampleEmfBytes();
        var renderer = new SystemDrawingImageRenderer();
        var result = renderer.Render(emf, RtfImageKind.Emf);

        Assert.NotNull(result);
        Assert.Equal("image/png", result!.MediaType);
        Assert.True(result.Bytes.Length > 8);
        // PNG magic
        Assert.Equal(0x89, result.Bytes[0]);
        Assert.Equal(0x50, result.Bytes[1]);
        Assert.Equal(0x4E, result.Bytes[2]);
        Assert.Equal(0x47, result.Bytes[3]);
        Assert.True(result.Width > 0);
        Assert.True(result.Height > 0);
    }

    [Fact]
    public void Render_CorruptBytes_ReturnsNull()
    {
        if (!IsWindows) return;
        var renderer = new SystemDrawingImageRenderer();
        var result = renderer.Render(new byte[] { 0x00, 0x01, 0x02 }, RtfImageKind.Emf);
        Assert.Null(result);
    }

    [Fact]
    public void CanRender_OnlyWmfAndEmf()
    {
        var r = new SystemDrawingImageRenderer();
        Assert.True(r.CanRender(RtfImageKind.Wmf));
        Assert.True(r.CanRender(RtfImageKind.Emf));
        Assert.False(r.CanRender(RtfImageKind.Png));
        Assert.False(r.CanRender(RtfImageKind.Jpeg));
        Assert.False(r.CanRender(RtfImageKind.Unknown));
    }

    [Fact]
    public void EndToEnd_EmfInRtf_EmbedsAsPngDataUri()
    {
        if (!IsWindows) return;

        var emf = CreateSampleEmfBytes();
        var hex = new StringBuilder(emf.Length * 2);
        foreach (var b in emf) hex.Append(b.ToString("X2"));

        var rtf = @"{\rtf1\ansi{\pict\emfblip\picw40\pich20\picwgoal600\pichgoal300 " + hex + "}}";

        var options = new RtfToHtmlOptions
        {
            EmitFullDocument = false,
            VectorImageRenderer = new SystemDrawingImageRenderer(),
        };
        var html = new global::RtfBridge.RtfBridgeConverter().ConvertRtfToHtml(rtf, options);

        Assert.Contains("<img src=\"data:image/png;base64,", html);
        Assert.DoesNotContain("[image]", html);
    }

    [Fact]
    public void EndToEnd_EmfInRtf_WithoutRenderer_EmitsPlaceholder()
    {
        if (!IsWindows) return;

        var emf = CreateSampleEmfBytes();
        var hex = new StringBuilder(emf.Length * 2);
        foreach (var b in emf) hex.Append(b.ToString("X2"));

        var rtf = @"{\rtf1\ansi{\pict\emfblip\picw40\pich20 " + hex + "}}";
        var html = new global::RtfBridge.RtfBridgeConverter().ConvertRtfToHtml(rtf, RtfToHtmlOptions.FragmentOnly);

        Assert.Contains("[image]", html);
        Assert.DoesNotContain("<img", html);
    }
}
