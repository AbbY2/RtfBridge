// ============================================================================
// Product:   RtfBridge.Tests
// File:      RtfPhase3Tests.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using RtfBridge;
using Xunit;

namespace RtfBridgeTests;

public class RtfPhase3Tests
{
    private static string Convert(string rtf, RtfToHtmlOptions? options = null) =>
        new global::RtfBridge.RtfBridgeConverter()
            .ConvertRtfToHtml(rtf, options ?? RtfToHtmlOptions.FragmentOnly);

    // ---------- headers / footers ----------

    [Fact]
    public void Header_NotEmitted_WhenOptionDisabled()
    {
        var rtf = @"{\rtf1\ansi{\header Top of page.}Body.\par}";
        var html = Convert(rtf);
        Assert.DoesNotContain("<header>", html);
        Assert.DoesNotContain("Top of page.", html);
        Assert.Contains("Body.", html);
    }

    [Fact]
    public void Header_Emitted_WhenOptionEnabled()
    {
        var rtf = @"{\rtf1\ansi{\header Top of page.}Body.\par}";
        var opts = new RtfToHtmlOptions { EmitFullDocument = false, RenderHeadersFooters = true };
        var html = Convert(rtf, opts);
        Assert.Contains("<header>", html);
        Assert.Contains("Top of page.", html);
        Assert.Contains("Body.", html);
    }

    [Fact]
    public void Footer_Emitted_WhenOptionEnabled()
    {
        var rtf = @"{\rtf1\ansi{\footer Bottom line.}Body.\par}";
        var opts = new RtfToHtmlOptions { EmitFullDocument = false, RenderHeadersFooters = true };
        var html = Convert(rtf, opts);
        Assert.Contains("<footer>", html);
        Assert.Contains("Bottom line.", html);
    }

    // ---------- footnotes ----------

    [Fact]
    public void Footnote_EmitsInlineRefAndListAtEnd()
    {
        var rtf = @"{\rtf1\ansi Hello{\super\chftn}{\footnote This is a note.}\par}";
        var html = Convert(rtf);
        // Inline reference marker.
        Assert.Contains("<sup><a href=\"#fn-1\">1</a></sup>", html);
        // Footnote list at end.
        Assert.Contains("<ol class=\"footnotes\">", html);
        Assert.Contains("id=\"fn-1\"", html);
        Assert.Contains("This is a note.", html);
    }

    [Fact]
    public void Footnote_NotEmitted_WhenOptionDisabled()
    {
        var rtf = @"{\rtf1\ansi Hi{\footnote note text}\par}";
        var opts = new RtfToHtmlOptions { EmitFullDocument = false, RenderFootnotesAsList = false };
        var html = Convert(rtf, opts);
        Assert.DoesNotContain("class=\"footnotes\"", html);
        Assert.DoesNotContain("note text", html);
        // But the inline reference is still emitted.
        Assert.Contains("<sup>", html);
    }

    // ---------- bookmarks ----------

    [Fact]
    public void Bookmark_EmitsAnchorWithId()
    {
        var rtf = @"{\rtf1\ansi Before {\*\bkmkstart MyMark}After\par}";
        var html = Convert(rtf);
        Assert.Contains("<a id=\"MyMark\"></a>", html);
        Assert.Contains("Before", html);
        Assert.Contains("After", html);
    }
}
