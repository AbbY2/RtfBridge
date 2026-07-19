// ============================================================================
// Product:   RtfBridge.Tests
// File:      RtfImageSizingTests.cs
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
/// Coverage tests for image sizing: \picwgoal/\pichgoal (twips), \picw/\pich pixel
/// fallback when no goal is provided, and \picscalex/\picscaley percent scaling.
/// </summary>
public class RtfImageSizingTests
{
    // Minimal 1x1 PNG (67 bytes) as hex.
    private const string OnePixelPngHex =
        "89504E470D0A1A0A0000000D49484452000000010000000108060000001F15C4" +
        "890000000A49444154789C63000100000500010D0A2DB40000000049454E44AE426082";

    private static string Convert(string rtf) =>
        new global::RtfBridge.RtfBridge()
            .ConvertRtfToHtml(rtf, RtfToHtmlOptions.FragmentOnly);

    [Fact]
    public void PicwGoal_ConvertedTwipsToPx()
    {
        // 1500 twips / 15 = 100 px.
        var rtf = @"{\rtf1\ansi{\pict\pngblip\picw1\pich1\picwgoal1500\pichgoal750 " + OnePixelPngHex + "}}";
        var html = Convert(rtf);
        Assert.Contains("width=\"100\"", html);
        Assert.Contains("height=\"50\"", html);
    }

    [Fact]
    public void PicwPich_UsedAsFallback_WhenGoalMissing()
    {
        var rtf = @"{\rtf1\ansi{\pict\pngblip\picw42\pich24 " + OnePixelPngHex + "}}";
        var html = Convert(rtf);
        Assert.Contains("width=\"42\"", html);
        Assert.Contains("height=\"24\"", html);
    }

    [Fact]
    public void PicScaleX_PicScaleY_ScalesResultingSize()
    {
        // 1500 twips -> 100 px; * 50% = 50 px.
        var rtf = @"{\rtf1\ansi{\pict\pngblip\picwgoal1500\pichgoal1500\picscalex50\picscaley200 " + OnePixelPngHex + "}}";
        var html = Convert(rtf);
        Assert.Contains("width=\"50\"", html);
        Assert.Contains("height=\"200\"", html);
    }
}
