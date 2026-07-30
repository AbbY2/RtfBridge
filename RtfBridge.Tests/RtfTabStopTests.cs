// ============================================================================
// Product:   RtfBridge.Tests
// File:      RtfTabStopTests.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using RtfBridge;
using Xunit;

namespace RtfBridgeTests;

public class RtfTabStopTests
{
    private static string Convert(string rtf) =>
        new global::RtfBridge.RtfBridgeConverter()
            .ConvertRtfToHtml(rtf, RtfToHtmlOptions.FragmentOnly);

    [Fact]
    public void DefinedTabStop_EmitsSizedSpacer()
    {
        // \tx1440 = 1 inch = 72pt tab stop; the tab advances to it from the line start.
        var html = Convert(@"{\rtf1\ansi\pard\tx1440 a\tab b\par}");
        Assert.Contains("display:inline-block;width:72pt", html);
        Assert.DoesNotContain("&emsp;", html);
    }

    [Fact]
    public void DotLeaderTabStop_EmitsDottedBorder()
    {
        var html = Convert(@"{\rtf1\ansi\pard\tqr\tldot\tx8640 Chapter\tab 5\par}");
        Assert.Contains("border-bottom:1px dotted", html);
    }

    [Fact]
    public void SecondTab_UsesDeltaBetweenStops()
    {
        // Stops at 1440 (72pt) and 3600 (180pt): first tab = 72pt, second tab = 108pt.
        var html = Convert(@"{\rtf1\ansi\pard\tx1440\tx3600 a\tab b\tab c\par}");
        Assert.Contains("width:72pt", html);
        Assert.Contains("width:108pt", html);
    }

    [Fact]
    public void NoTabStops_FallsBackToEmSpace()
    {
        var html = Convert(@"{\rtf1\ansi a\tab b}");
        Assert.Contains("a&emsp;b", html);
        Assert.DoesNotContain("inline-block", html);
    }

    [Fact]
    public void TabBeyondDefinedStops_FallsBackToEmSpace()
    {
        // Only one stop defined; the second tab has no matching stop.
        var html = Convert(@"{\rtf1\ansi\pard\tx1440 a\tab b\tab c\par}");
        Assert.Contains("width:72pt", html);
        Assert.Contains("&emsp;", html);
    }
}
