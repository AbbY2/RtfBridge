// ============================================================================
// Product:   RtfBridge.Tests
// File:      RtfLibreOfficeCompatTests.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using RtfBridge;
using Xunit;
using Xunit.Abstractions;

namespace RtfBridgeTests;

/// <summary>
/// LibreOffice-specific RTF patterns: Symbol-font PUA bullets, shape-anchored pictures
/// (\shp \shpinst pib), and \shppict/\nonshppict alternates.
/// </summary>
public class RtfLibreOfficeCompatTests
{
    private readonly ITestOutputHelper _out;
    public RtfLibreOfficeCompatTests(ITestOutputHelper o) { _out = o; }

    private string Convert(string rtf)
    {
        var html = new global::RtfBridge.RtfBridge()
            .ConvertRtfToHtml(rtf, RtfToHtmlOptions.FragmentOnly);
        _out.WriteLine(html);
        return html;
    }

    [Fact]
    public void SymbolFont_PuaBullet_RemappedToRealBulletCharacter()
    {
        // Font f1 is Symbol (charset 2). The PUA \u61623? (0xF0B7) glyph is Symbol's bullet;
        // it must be remapped to U+2022 (•) in HTML, not leaked as an unmappable char.
        var rtf = @"{\rtf1\ansi\ansicpg1252\deff0
{\fonttbl{\f0 Calibri;}{\f1\fcharset2 Symbol;}}
\pard {\f1\u61623?} {\f0 Item text}\par}";
        var html = Convert(rtf);
        Assert.Contains("\u2022", html);
    }

    [Fact]
    public void SymbolFont_AsciiBulletByte_RemappedToRealBullet()
    {
        // In a Symbol run, byte 0xB7 renders as bullet, not middle-dot.
        var rtf = @"{\rtf1\ansi\ansicpg1252\deff0
{\fonttbl{\f0 Calibri;}{\f1\fcharset2 Symbol;}}
\pard {\f1\'b7} {\f0 Item}\par}";
        var html = Convert(rtf);
        Assert.Contains("\u2022", html);
    }

    [Fact]
    public void NonSymbolFont_MiddleDot_IsNotRemapped()
    {
        // In a normal (non-Symbol) run, 0xB7 must remain U+00B7 (middle dot).
        var rtf = @"{\rtf1\ansi\ansicpg1252\deff0
{\fonttbl{\f0 Calibri;}}
\pard {\f0\'b7 X}\par}";
        var html = Convert(rtf);
        Assert.Contains("\u00B7", html);
        Assert.DoesNotContain("\u2022", html);
    }

    [Fact]
    public void ShapePicture_PibExtracted_AsInlineImage()
    {
        // A LibreOffice-style shape-anchored PNG: {\shp{\*\shpinst{\sp{\sn pib}{\sv {\pict\pngblip 89504E47}}}}}
        // The parser used to skip \shp entirely and drop the image; now it should extract the
        // nested \pict as a normal <img>.
        var rtf = @"{\rtf1\ansi\ansicpg1252\deff0
{\fonttbl{\f0 Calibri;}}
\pard Before {\shp{\*\shpinst{\sp{\sn pib}{\sv {\pict\pngblip\picw10\pich10 89504E470D0A1A0A0000000D49484452}}}}} After\par}";
        var html = Convert(rtf);
        Assert.Contains("<img", html);
        Assert.Contains("data:image/png;base64,", html);
    }

    [Fact]
    public void ShppictWithNonshppictSibling_OnlyModernVariantEmitted()
    {
        // When \shppict (modern PNG) and \nonshppict (legacy WMF) both appear as siblings,
        // only the PNG should be emitted — not a duplicate placeholder for the WMF.
        var rtf = @"{\rtf1\ansi\ansicpg1252\deff0
{\fonttbl{\f0 Calibri;}}
\pard {\*\shppict{\pict\pngblip\picw10\pich10 89504E470D0A1A0A0000000D49484452}}{\*\nonshppict{\pict\wmetafile8\picw10\pich10 010009000003}}\par}";
        var html = Convert(rtf);
        // Modern PNG emitted:
        Assert.Contains("data:image/png;base64,", html);
        // Exactly one <img> — no duplicate/placeholder from the nonshppict fallback:
        int imgCount = System.Text.RegularExpressions.Regex.Matches(html, "<img").Count;
        Assert.Equal(1, imgCount);
    }
}
