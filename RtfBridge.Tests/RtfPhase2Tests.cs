// ============================================================================
// Product:   RtfBridge.Tests
// File:      RtfPhase2Tests.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using RtfBridge;
using Xunit;

namespace RtfBridgeTests;

public class RtfPhase2Tests
{
    private static string Convert(string rtf) =>
        new global::RtfBridge.RtfBridge()
            .ConvertRtfToHtml(rtf, RtfToHtmlOptions.FragmentOnly);

    // ---------- images ----------

    // A minimal 1x1 PNG. Bytes: 89 50 4E 47 0D 0A 1A 0A ... total 67 bytes.
    private const string OnePixelPngHex =
        "89504E470D0A1A0A0000000D49484452000000010000000108060000001F15C4" +
        "890000000A49444154789C63000100000500010D0A2DB40000000049454E44AE426082";

    [Fact]
    public void PngImage_EmbeddedAsBase64DataUri()
    {
        var rtf = @"{\rtf1\ansi{\pict\pngblip\picw1\pich1\picwgoal15\pichgoal15 " + OnePixelPngHex + "}}";
        var html = Convert(rtf);
        Assert.Contains("<img src=\"data:image/png;base64,", html);
        Assert.Contains("width=\"1\"", html);
        Assert.Contains("height=\"1\"", html);
    }

    [Fact]
    public void UnsupportedPicture_EmitsPlaceholderAndWarning()
    {
        // \wmetafile with empty hex — unsupported vector format.
        var rtf = @"{\rtf1\ansi{\pict\wmetafile8\picw10\pich10 }}";
        var conv = new global::RtfBridge.RtfBridge();
        var result = conv.ConvertRtfToHtmlDetailed(rtf, RtfToHtmlOptions.FragmentOnly);
        Assert.Contains("[image]", result.Html);
        Assert.Contains(result.Warnings, w => w.Code == "IMG_UNSUPPORTED");
    }

    // ---------- hyperlinks ----------

    [Fact]
    public void HyperlinkField_EmitsAnchor()
    {
        var rtf = @"{\rtf1\ansi {\field{\*\fldinst HYPERLINK ""https://example.com/""}{\fldrslt click here}}}";
        var html = Convert(rtf);
        Assert.Contains("<a href=\"https://example.com/\">click here</a>", html);
    }

    [Fact]
    public void HyperlinkUrl_IsAttributeEscaped()
    {
        var rtf = @"{\rtf1\ansi {\field{\*\fldinst HYPERLINK ""https://a.test/?x=1&y=2""}{\fldrslt go}}}";
        var html = Convert(rtf);
        Assert.Contains("href=\"https://a.test/?x=1&amp;y=2\"", html);
    }

    // ---------- lists ----------

    [Fact]
    public void BulletList_TwoItems_EmitsUnorderedList()
    {
        // \levelnfc23 = bullet
        var rtf = @"{\rtf1\ansi{\listtable{\list\listid1{\listlevel\levelnfc23}}}" +
                  @"{\listoverridetable{\listoverride\listid1\ls1}}" +
                  @"\pard\ls1\ilvl0 first\par\ls1\ilvl0 second\par}";
        var html = Convert(rtf);
        Assert.Contains("<ul>", html);
        Assert.Contains("<li>first</li>", html);
        Assert.Contains("<li>second</li>", html);
        Assert.EndsWith("</ul>", html);
    }

    [Fact]
    public void NumberedList_UsesOrderedListTag()
    {
        // \levelnfc0 = arabic
        var rtf = @"{\rtf1\ansi{\listtable{\list\listid2{\listlevel\levelnfc0}}}" +
                  @"{\listoverridetable{\listoverride\listid2\ls2}}" +
                  @"\pard\ls2\ilvl0 one\par\ls2\ilvl0 two\par}";
        var html = Convert(rtf);
        Assert.Contains("<ol>", html);
        Assert.Contains("<li>one</li>", html);
        Assert.Contains("<li>two</li>", html);
    }

    // ---------- tables ----------

    [Fact]
    public void SingleRowTable_TwoCells_Renders()
    {
        var rtf = @"{\rtf1\ansi\trowd\cellx3000\cellx6000 A\cell B\cell\row}";
        var html = Convert(rtf);
        Assert.Contains("<table", html);
        Assert.Contains("<tr>", html);
        Assert.Contains(">A<", html);
        Assert.Contains(">B<", html);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "<td").Count);
    }

    [Fact]
    public void TwoRowTable_ProducesTwoTrElements()
    {
        var rtf = @"{\rtf1\ansi" +
                  @"\trowd\cellx3000\cellx6000 A\cell B\cell\row" +
                  @"\trowd\cellx3000\cellx6000 C\cell D\cell\row}";
        var html = Convert(rtf);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "<tr>").Count);
        Assert.Contains(">A<", html);
        Assert.Contains(">D<", html);
    }
}
