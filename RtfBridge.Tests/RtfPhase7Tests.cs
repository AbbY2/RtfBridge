// ============================================================================
// Product:   RtfBridge.Tests
// File:      RtfPhase7Tests.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using RtfBridge;
using Xunit;

namespace RtfBridgeTests;

/// <summary>
/// Regression tests for Phase 7: baseline shift (\up/\dn), paragraph positioning
/// (\posx/\posy/named-position), and image text-wrap (\shpwr).
/// </summary>
public class RtfPhase7Tests
{
    // Minimal 1x1 PNG (67 bytes) as hex.
    private const string OnePixelPngHex =
        "89504E470D0A1A0A0000000D49484452000000010000000108060000001F15C4" +
        "890000000A49444154789C63000100000500010D0A2DB40000000049454E44AE426082";

    private static string Convert(string rtf) =>
        new global::RtfBridge.RtfBridgeConverter()
            .ConvertRtfToHtml(rtf, RtfToHtmlOptions.FragmentOnly);

    [Fact]
    public void Up_RaisesBaseline_EmitsVerticalAlignPt()
    {
        // \up6 = 6 half-points = 3pt raise.
        var rtf = @"{\rtf1\ansi Hello {\up6 world}.}";
        var html = Convert(rtf);
        Assert.Contains("vertical-align:3pt", html);
    }

    [Fact]
    public void Dn_LowersBaseline_EmitsNegativeVerticalAlign()
    {
        // \dn4 = 4 half-points down = -2pt.
        var rtf = @"{\rtf1\ansi Hello {\dn4 world}.}";
        var html = Convert(rtf);
        Assert.Contains("vertical-align:-2pt", html);
    }

    [Fact]
    public void PosxPosy_EmitAbsolutePositioning()
    {
        // 1440 twips = 72pt, 720 twips = 36pt.
        var rtf = @"{\rtf1\ansi \pard\posx1440\posy720 Floating.\par}";
        var html = Convert(rtf);
        Assert.Contains("position:absolute", html);
        Assert.Contains("left:72pt", html);
        Assert.Contains("top:36pt", html);
    }

    [Fact]
    public void PosxrNamedPosition_EmitsRightAnchor()
    {
        var rtf = @"{\rtf1\ansi \pard\posxr Anchored right.\par}";
        var html = Convert(rtf);
        Assert.Contains("position:absolute", html);
        Assert.Contains("right:0", html);
    }

    [Fact]
    public void Shpwr_Around_EmitsFloatLeftOnImage()
    {
        // Shape group containing \shpwr2 (around) and an inner \pict.
        var rtf =
            @"{\rtf1\ansi{\shp\shpwr2{\*\shpinst}{\sp{\sn pib}{\sv " +
            @"{\pict\pngblip\picw1\pich1\picwgoal15\pichgoal15 " + OnePixelPngHex + "}}}}}";
        var html = Convert(rtf);
        Assert.Contains("float:left", html);
    }
}
