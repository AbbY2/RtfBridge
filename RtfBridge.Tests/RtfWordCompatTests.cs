// ============================================================================
// Product:   RtfBridge.Tests
// File:      RtfWordCompatTests.cs
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

/// <summary>Tests using realistic Word-generated RTF fragments to catch integration issues.</summary>
public class RtfWordCompatTests
{
    private readonly ITestOutputHelper _out;
    public RtfWordCompatTests(ITestOutputHelper o) { _out = o; }

    private string Convert(string rtf)
    {
        var html = new global::RtfBridge.RtfBridge()
            .ConvertRtfToHtml(rtf, RtfToHtmlOptions.FragmentOnly);
        _out.WriteLine(html);
        return html;
    }

    [Fact]
    public void WordStyle_Table_2x2_ShouldRender()
    {
        // A minimal but Word-realistic table with \pard\intbl before each cell.
        var rtf = @"{\rtf1\ansi\ansicpg1252\deff0
{\fonttbl{\f0 Calibri;}}
\pard
\trowd\trgaph108\trleft-108
\cellx3000\cellx6000
\pard\intbl A1\cell
\pard\intbl B1\cell
\row
\trowd\trgaph108\trleft-108
\cellx3000\cellx6000
\pard\intbl A2\cell
\pard\intbl B2\cell
\row
\pard\par}";
        var html = Convert(rtf);
        Assert.Contains("<table", html);
        Assert.Contains("A1", html);
        Assert.Contains("B2", html);
    }

    [Fact]
    public void WordStyle_Image_WrappedInShppict_ShouldRender()
    {
        const string OnePixelPngHex =
            "89504E470D0A1A0A0000000D49484452000000010000000108060000001F15C4" +
            "890000000A49444154789C63000100000500010D0A2DB40000000049454E44AE426082";
        var rtf = @"{\rtf1\ansi{\*\shppict{\pict\pngblip\picw1\pich1\picwgoal15\pichgoal15 " + OnePixelPngHex + "}}}";
        var html = Convert(rtf);
        Assert.Contains("<img src=\"data:image/png;base64,", html);
    }

    [Fact]
    public void WordStyle_Image_WrappedInShppictWithNonshppictFallback_ShouldRender()
    {
        const string OnePixelPngHex =
            "89504E470D0A1A0A0000000D49484452000000010000000108060000001F15C4" +
            "890000000A49444154789C63000100000500010D0A2DB40000000049454E44AE426082";
        // Word emits both \*\shppict (modern) and \nonshppict (legacy fallback). We must render one, not both.
        var rtf = @"{\rtf1\ansi{\*\shppict{\pict\pngblip\picw1\pich1 " + OnePixelPngHex + @"}}{\nonshppict{\pict\wmetafile8\picw1\pich1 " + OnePixelPngHex + "}}}";
        var html = Convert(rtf);
        _out.WriteLine("---");
        _out.WriteLine(html);
        Assert.Contains("<img", html);
    }
}
