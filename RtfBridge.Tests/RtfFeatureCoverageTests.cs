// ============================================================================
// Product:   RtfBridge.Tests
// File:      RtfFeatureCoverageTests.cs
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
/// Coverage tests for common RTF control words wired in the "Close High-Value RTF
/// Feature Gaps" pass: \caps, \scaps, \v (hidden), \sl/\slmult, \rtlpar/\ltrpar,
/// \rtlch/\ltrch, \lang, \keep/\keepn/\pagebb, and underline style variants.
/// </summary>
public class RtfFeatureCoverageTests
{
    private readonly ITestOutputHelper _out;
    public RtfFeatureCoverageTests(ITestOutputHelper o) { _out = o; }

    private string Convert(string rtf, RtfToHtmlOptions? options = null)
    {
        var html = new global::RtfBridge.RtfBridge()
            .ConvertRtfToHtml(rtf, options ?? RtfToHtmlOptions.FragmentOnly);
        _out.WriteLine(html);
        return html;
    }

    [Fact]
    public void AllCaps_UppercasesText_AndEmitsCssHint()
    {
        var html = Convert(@"{\rtf1\ansi\caps hello\par}");
        Assert.Contains("HELLO", html);
        Assert.Contains("text-transform:uppercase", html);
    }

    [Fact]
    public void SmallCaps_EmitsFontVariantSmallCaps()
    {
        var html = Convert(@"{\rtf1\ansi\scaps hello\par}");
        Assert.Contains("font-variant:small-caps", html);
    }

    [Fact]
    public void HiddenText_DroppedByDefault()
    {
        var html = Convert(@"{\rtf1\ansi visible\v hidden\v0 tail\par}");
        Assert.Contains("visible", html);
        Assert.Contains("tail", html);
        Assert.DoesNotContain("hidden", html);
    }

    [Fact]
    public void HiddenText_EmittedWhenOptionEnabled()
    {
        var opts = RtfToHtmlOptions.FragmentOnly;
        opts.RenderHiddenText = true;
        var html = Convert(@"{\rtf1\ansi visible\v hidden\v0 tail\par}", opts);
        Assert.Contains("hidden", html);
        Assert.Contains("display:none", html);
    }

    [Fact]
    public void RightToLeftParagraph_EmitsDirRtl()
    {
        var html = Convert(@"{\rtf1\ansi\rtlpar hello\par}");
        Assert.Contains("dir=\"rtl\"", html);
    }

    [Fact]
    public void LanguageId_MapsToBcp47LangAttribute()
    {
        // 1036 = fr-FR
        var html = Convert(@"{\rtf1\ansi\lang1036 bonjour\par}");
        Assert.Contains("lang=\"fr-FR\"", html);
    }

    [Fact]
    public void LineHeightMultiple_EmitsUnitlessLineHeight()
    {
        // \sl360\slmult1 = 1.5x single spacing.
        var html = Convert(@"{\rtf1\ansi\sl360\slmult1 hi\par}");
        Assert.Contains("line-height:1.5", html);
    }

    [Fact]
    public void PageBreakBefore_EmitsCssBreak()
    {
        var html = Convert(@"{\rtf1\ansi\pagebb hi\par}");
        Assert.Contains("page-break-before:always", html);
    }

    [Fact]
    public void UnderlineDouble_EmitsCssTextDecorationDouble()
    {
        var html = Convert(@"{\rtf1\ansi\uldb hi\par}");
        Assert.Contains("text-decoration:underline double", html);
    }

    [Fact]
    public void UnderlineWave_EmitsCssWavy()
    {
        var html = Convert(@"{\rtf1\ansi\ulwave hi\par}");
        Assert.Contains("text-decoration:underline wavy", html);
    }
}
