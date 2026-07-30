// ============================================================================
// Product:   RtfBridge.Tests
// File:      RtfStyleResolutionTests.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using RtfBridge;
using Xunit;

namespace RtfBridgeTests;

public class RtfStyleResolutionTests
{
    private static string Convert(string rtf) =>
        new global::RtfBridge.RtfBridgeConverter()
            .ConvertRtfToHtml(rtf, RtfToHtmlOptions.FragmentOnly);

    [Fact]
    public void ParagraphStyle_AppliesBoldFromStylesheet()
    {
        // Style 15 declares bold; the paragraph references it via \s15 without an explicit \b.
        var rtf = @"{\rtf1\ansi{\stylesheet{\s15\b Strong Para;}}\pard\s15 hello\par}";
        var html = Convert(rtf);
        Assert.Contains("<strong>hello</strong>", html);
    }

    [Fact]
    public void ParagraphStyle_AppliesAlignmentAndIndentFromStylesheet()
    {
        var rtf = @"{\rtf1\ansi{\stylesheet{\s2\qc\li720 Centered;}}\pard\s2 middle\par}";
        var html = Convert(rtf);
        Assert.Contains("text-align:center", html);
        Assert.Contains("margin-left:36pt", html);
    }

    [Fact]
    public void ExplicitControlWord_OverridesStyleFormatting()
    {
        // Style 3 declares bold, but the run turns it off with \b0 after \s3.
        var rtf = @"{\rtf1\ansi{\stylesheet{\s3\b Bold Style;}}\pard\s3\b0 plain\par}";
        var html = Convert(rtf);
        Assert.DoesNotContain("<strong>", html);
        Assert.Contains("plain", html);
    }

    [Fact]
    public void BasedOnStyle_InheritsBaseFormatting()
    {
        // Style 1 is italic; style 2 is based on style 1 and adds bold. \s2 should be bold+italic.
        var rtf = @"{\rtf1\ansi{\stylesheet{\s1\i Base;}{\s2\sbasedon1\b Derived;}}\pard\s2 text\par}";
        var html = Convert(rtf);
        Assert.Contains("<strong>", html);
        Assert.Contains("<em>", html);
    }

    [Fact]
    public void HeadingStyle_StillMapsToHeadingTag()
    {
        // Ensure existing outline-level heading behaviour is preserved.
        var rtf = @"{\rtf1\ansi{\stylesheet{\s1\outlinelevel0 heading 1;}}\pard\s1 Title\par}";
        var html = Convert(rtf);
        Assert.Contains("<h1", html);
    }
}
