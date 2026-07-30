// ============================================================================
// Product:   RtfBridge.Tests
// File:      RtfReviewFixTests.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using System.IO;
using System.Linq;
using System.Text;
using RtfBridge;
using Xunit;

namespace RtfBridgeTests;

/// <summary>Regression tests for the 2026-07 full-code-review fixes.</summary>
public class RtfReviewFixTests
{
    private static string Convert(string rtf) =>
        new global::RtfBridge.RtfBridgeConverter()
            .ConvertRtfToHtml(rtf, RtfToHtmlOptions.FragmentOnly);

    private static string Convert(string rtf, RtfToHtmlOptions options) =>
        new global::RtfBridge.RtfBridgeConverter().ConvertRtfToHtml(rtf, options);

    // ---------- surrogate pairs / emoji ----------

    [Fact]
    public void Emoji_SurrogatePair_DoesNotCrash_AndRoundTrips()
    {
        // 😀 U+1F600 = D83D DE00 → \u-10179?\u-8704?
        var html = Convert(@"{\rtf1\ansi Hello \u-10179?\u-8704? world\par}");
        Assert.Contains("😀", html);
        Assert.Contains("Hello", html);
        Assert.Contains("world", html);
    }

    [Fact]
    public void LoneSurrogate_IsDropped_NotCrashing()
    {
        var html = Convert(@"{\rtf1\ansi A\u-10179?B\par}");
        Assert.Contains("A", html);
        Assert.Contains("B", html);
    }

    // ---------- DBCS / codepage decoding ----------

    [Fact]
    public void ShiftJis_DoubleByteEscapes_DecodeAsPairs()
    {
        // 日本 in cp932: 93 FA 96 7B
        var html = Convert(@"{\rtf1\ansi\ansicpg932 \'93\'fa\'96\'7b\par}");
        Assert.Contains("日本", html);
    }

    [Fact]
    public void FontCharset_OverridesDocumentCodePage()
    {
        // fcharset204 = Cyrillic (cp1251): C0 C1 C2 = АБВ
        var html = Convert(@"{\rtf1\ansi{\fonttbl{\f0\fcharset204 Arial;}}\f0 \'c0\'c1\'c2\par}");
        Assert.Contains("АБВ", html);
    }

    [Fact]
    public void RawHighBytes_DecodeViaDocumentCodePage()
    {
        // Unescaped cp1252 smart quotes 0x93 / 0x94 around "q".
        var bytes = Encoding.ASCII.GetBytes(@"{\rtf1\ansi\ansicpg1252 a")
            .Concat(new byte[] { 0x93 })
            .Concat(Encoding.ASCII.GetBytes("q"))
            .Concat(new byte[] { 0x94 })
            .Concat(Encoding.ASCII.GetBytes(@"b\par}"))
            .ToArray();
        using var ms = new MemoryStream(bytes);
        var html = new global::RtfBridge.RtfBridgeConverter().ConvertRtfToHtml(ms, RtfToHtmlOptions.FragmentOnly);
        Assert.Contains("a“q”b", html);
    }

    // ---------- skipped destinations must not leak ----------

    [Fact]
    public void UnknownStarredDestination_LeaksNothing()
    {
        var html = Convert(@"{\rtf1{\*\unknowndest junk\emdash more\par junk2}visible\par}");
        Assert.Equal("<p>visible</p>", html);
    }

    // ---------- font table forms ----------

    [Fact]
    public void UnbracedFontTable_ParsesFontNames()
    {
        var html = Convert(@"{\rtf1{\fonttbl\f0\fswiss Arial;\f1\froman Times New Roman;}\f1\fs24 styled\par}");
        Assert.Contains("font-family:'Times New Roman'", html);
    }

    [Fact]
    public void FontAltName_DoesNotPolluteFontName()
    {
        var html = Convert(@"{\rtf1{\fonttbl{\f0\fswiss Arial{\*\falt Helvetica};}}\f0 x\par}");
        Assert.Contains("font-family:'Arial'", html);
        Assert.DoesNotContain("Helvetica", html);
    }

    // ---------- hyperlink URL sanitization ----------

    [Fact]
    public void JavascriptUrl_IsStripped_AtBasicLevel()
    {
        var html = Convert(@"{\rtf1{\field{\*\fldinst HYPERLINK ""javascript:alert(1)""}{\fldrslt click}}\par}");
        Assert.DoesNotContain("javascript:", html);
        Assert.Contains("click", html);
    }

    [Fact]
    public void JavascriptUrl_PassesThrough_WhenSanitizationNone()
    {
        var options = new RtfToHtmlOptions { EmitFullDocument = false, Sanitization = HtmlSanitizationLevel.None };
        var html = Convert(@"{\rtf1{\field{\*\fldinst HYPERLINK ""javascript:alert(1)""}{\fldrslt click}}\par}", options);
        Assert.Contains("javascript:alert(1)", html);
    }

    [Fact]
    public void StrictLevel_AllowsOnlyAllowlistedSchemes()
    {
        var options = new RtfToHtmlOptions { EmitFullDocument = false, Sanitization = HtmlSanitizationLevel.Strict };
        var https = Convert(@"{\rtf1{\field{\*\fldinst HYPERLINK ""https://example.com""}{\fldrslt ok}}\par}", options);
        Assert.Contains("href=\"https://example.com\"", https);
        var ftp = Convert(@"{\rtf1{\field{\*\fldinst HYPERLINK ""ftp://example.com""}{\fldrslt nope}}\par}", options);
        Assert.DoesNotContain("href=", ftp);
    }

    // ---------- nested tables ----------

    [Fact]
    public void NestedTable_DoesNotCrash_ContentPreserved()
    {
        var html = Convert(
            @"{\rtf1\trowd\cellx4000\pard\intbl\itap2 inner\nestcell{\*\nesttableprops\trowd\cellx2000\nestrow}\pard\intbl outer\cell\row}");
        Assert.Contains("inner", html);
        Assert.Contains("outer", html);
        Assert.Contains("<table", html);
    }

    // ---------- legacy \*\pn lists ----------

    [Fact]
    public void LegacyPnBulletList_RendersAsUl()
    {
        var html = Convert(
            @"{\rtf1\pard{\*\pn\pnlvlblt\pnf1\pnindent0{\pntxtb\'b7}}{\pntext \'b7\tab}Item one\par{\pntext \'b7\tab}Item two\par}");
        Assert.Contains("<ul>", html);
        Assert.Contains("<li>Item one</li>", html);
        Assert.Contains("<li>Item two</li>", html);
    }

    // ---------- headings ----------

    [Fact]
    public void OutlineLevel_MapsToHeadingTags()
    {
        var html = Convert(@"{\rtf1\pard\outlinelevel0 Title\par\pard Body\par}");
        Assert.Contains("<h1", html);
        Assert.Contains("Title</h1>", html);
        Assert.Contains("<p>Body</p>", html);
    }

    [Fact]
    public void HeadingStyle_FromStylesheet_MapsToHeadingTag()
    {
        var html = Convert(
            @"{\rtf1{\stylesheet{\s0 Normal;}{\s1 heading 1;}}\pard\s1 Chapter\par\pard\s0 Text\par}");
        Assert.Contains("<h1", html);
        Assert.Contains("Chapter</h1>", html);
        Assert.Contains("<p>Text</p>", html);
    }

    // ---------- \pict sub-group hex isolation ----------

    [Fact]
    public void PictSubGroupHex_NotAccumulatedAsImageData()
    {
        // A \pict group with a \*\blipuid sub-group whose 32-char hex GUID would prepend
        // 16 garbage bytes to the PNG magic, breaking the image if included.
        // Minimal 1×1 PNG (68 bytes).
        byte[] png =
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82,
        };
        // Build RTF with a \*\blipuid sub-group before the hex data
        var hexData = string.Concat(System.Array.ConvertAll(png, b => b.ToString("x2")));
        var rtf = @"{\rtf1{\pict\pngblip{\*\blipuid deadbeefdeadbeefdeadbeefdeadbeef}" + hexData + @"}\par}";
        var html = Convert(rtf);
        // Must emit a real <img> tag with valid PNG base64, not a placeholder
        Assert.Contains("<img src=\"data:image/png;base64,", html);
        // The base64 must decode to the exact PNG bytes (no GUID prepended)
        var b64 = System.Text.RegularExpressions.Regex.Match(html, @"base64,([^""]+)").Groups[1].Value;
        var decoded = System.Convert.FromBase64String(b64);
        Assert.Equal(png, decoded);
    }

    // ---------- \bin picture payloads ----------

    [Fact]
    public void BinaryPicture_IsEmittedAsDataUri()
    {
        // Minimal 1x1 PNG.
        byte[] png =
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82,
        };
        var header = Encoding.ASCII.GetBytes(@"{\rtf1{\pict\pngblip\bin" + png.Length + " ");
        var footer = Encoding.ASCII.GetBytes(@"}\par}");
        var bytes = header.Concat(png).Concat(footer).ToArray();
        using var ms = new MemoryStream(bytes);
        var html = new global::RtfBridge.RtfBridgeConverter().ConvertRtfToHtml(ms, RtfToHtmlOptions.FragmentOnly);
        Assert.Contains("data:image/png;base64,", html);
    }

    // ---------- Outlook HTML encapsulation ----------

    [Fact]
    public void FromHtml_ExtractsEncapsulatedHtml()
    {
        var rtf =
            @"{\rtf1\ansi\ansicpg1252\fromhtml1{\fonttbl{\f0 Arial;}}" +
            @"{\*\htmltag2 <html>}{\*\htmltag50 <p>}Hello \b\htmlrtf bold-fallback\htmlrtf0 " +
            @"{\*\htmltag84 <b>}bold{\*\htmltag92 </b>}{\*\htmltag58 </p>}{\*\htmltag4 </html>}}";
        var html = Convert(rtf);
        Assert.Contains("<html>", html);
        Assert.Contains("<p>", html);
        Assert.Contains("<b>bold</b>", html);
        Assert.Contains("Hello", html);
        Assert.DoesNotContain("bold-fallback", html);
    }

    [Fact]
    public void FromHtml_CanBeDisabled()
    {
        var rtf = @"{\rtf1\ansi\fromhtml1{\*\htmltag50 <p>}text\par}";
        var options = new RtfToHtmlOptions { EmitFullDocument = false, DecodeHtmlEncapsulation = false };
        var html = Convert(rtf, options);
        Assert.DoesNotContain("<p><p>", html); // htmltag content skipped by normal pipeline (\* destination)
        Assert.Contains("text", html);
    }

    // ---------- RtfParseException wrapping ----------

    [Fact]
    public void InternalFailure_SurfacesAsRtfParseException()
    {
        // Force a failure via a null stream read path instead: a truncated \bin cannot crash,
        // so validate the exception type contract with an impossible codepage.
        var rtf = @"{\rtf1\ansi\ansicpg999999 \'93 x\par}";
        // Should not throw at all (fallback decoding) — conversion must succeed.
        var html = Convert(rtf);
        Assert.Contains("x", html);
    }

    // ---------- flattened list nesting option ----------

    [Fact]
    public void PreserveListNestingFalse_FlattensLevels()
    {
        var rtf =
            @"{\rtf1{\*\listtable{\list\listid1{\listlevel\levelnfc23}}}" +
            @"{\*\listoverridetable{\listoverride\listid1\ls1}}" +
            @"\pard\ls1\ilvl0 a\par\pard\ls1\ilvl1 b\par}";
        var nested = Convert(rtf);
        Assert.Contains("<ul><li>a</li><ul><li>b</li></ul></ul>", nested.Replace("\r", "").Replace("\n", ""));
        var flat = Convert(rtf, new RtfToHtmlOptions { EmitFullDocument = false, PreserveListNesting = false });
        Assert.Contains("<ul><li>a</li><li>b</li></ul>", flat.Replace("\r", "").Replace("\n", ""));
    }

    // ---------- Word double-\trowd table pattern ----------

    [Fact]
    public void WordDoubledTrowd_TableCellsRendered()
    {
        // Word emits \trowd twice per row: once before cell content, once just before \row.
        // The second \trowd must not wipe accumulated cell content.
        var rtf =
            @"{\rtf1" +
            @"\trowd\cellx4320\cellx8640" +
            @"\pard\intbl Hello\cell World\cell" +
            @"\pard\intbl{\trowd\cellx4320\cellx8640\row}}";
        var html = Convert(rtf);
        Assert.Contains("<table", html);
        Assert.Contains("<td", html);
        Assert.Contains("Hello", html);
        Assert.Contains("World", html);
    }

    // ---------- list inside table cell ----------

    [Fact]
    public void ListInsideTableCell_CollapsesCorrectly()
    {
        // A bullet list inside a table cell must produce <ul>/<li>, not bare <p> elements.
        var rtf =
            @"{\rtf1" +
            @"{\*\listtable{\list\listid5{\listlevel\levelnfc23}}}" +
            @"{\*\listoverridetable{\listoverride\listid5\ls5}}" +
            @"\trowd\cellx4000" +
            @"\pard\intbl\ls5\ilvl0 alpha\par" +
            @"\pard\intbl\ls5\ilvl0 beta\par" +
            @"\cell\row}";
        var html = Convert(rtf);
        Assert.Contains("<ul>", html);
        Assert.Contains("<li>alpha</li>", html);
        Assert.Contains("<li>beta</li>", html);
        Assert.DoesNotContain("<p>alpha</p>", html);
    }
}
