// ============================================================================
// Product:   RtfBridge.Tests
// File:      RtfPhase4FieldTests.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using RtfBridge;
using Xunit;

namespace RtfBridgeTests;

public class RtfPhase4FieldTests
{
    private static string Convert(string rtf) =>
        new global::RtfBridge.RtfBridgeConverter()
            .ConvertRtfToHtml(rtf, RtfToHtmlOptions.FragmentOnly);

    [Fact]
    public void PageRef_EmitsInternalAnchor()
    {
        var rtf = @"{\rtf1\ansi{\field{\*\fldinst PAGEREF MyMark \\* MERGEFORMAT}{\fldrslt 7}}\par}";
        var html = Convert(rtf);
        Assert.Contains("<a href=\"#MyMark\">7</a>", html);
    }

    [Fact]
    public void Ref_EmitsInternalAnchorUsingArgumentWhenResultEmpty()
    {
        var rtf = @"{\rtf1\ansi{\field{\*\fldinst REF MyMark}{\fldrslt}}\par}";
        var html = Convert(rtf);
        Assert.Contains("<a href=\"#MyMark\">MyMark</a>", html);
    }

    [Fact]
    public void Date_UsesFldrsltWhenPresent()
    {
        var rtf = @"{\rtf1\ansi{\field{\*\fldinst DATE \\@ ""yyyy-MM-dd""}{\fldrslt 2024-05-01}}\par}";
        var html = Convert(rtf);
        Assert.Contains("2024-05-01", html);
        Assert.DoesNotContain("[DATE]", html);
    }

    [Fact]
    public void Time_FallsBackToPlaceholderWhenResultEmpty()
    {
        var rtf = @"{\rtf1\ansi{\field{\*\fldinst TIME}{\fldrslt}}\par}";
        var html = Convert(rtf);
        Assert.Contains("[TIME]", html);
    }

    [Fact]
    public void Page_FallsBackToPlaceholder()
    {
        var rtf = @"{\rtf1\ansi{\field{\*\fldinst PAGE}{\fldrslt}}\par}";
        var html = Convert(rtf);
        Assert.Contains("[PAGE]", html);
    }

    [Fact]
    public void NumPages_UsesResultValue()
    {
        var rtf = @"{\rtf1\ansi{\field{\*\fldinst NUMPAGES}{\fldrslt 12}}\par}";
        var html = Convert(rtf);
        Assert.Contains("12", html);
        Assert.DoesNotContain("[NUMPAGES]", html);
    }

    [Fact]
    public void UnknownField_FallsBackToFldrsltText()
    {
        var rtf = @"{\rtf1\ansi{\field{\*\fldinst FOOBAR arg}{\fldrslt fallback text}}\par}";
        var html = Convert(rtf);
        Assert.Contains("fallback text", html);
    }

    [Fact]
    public void Hyperlink_StillWorks_AfterFieldRefactor()
    {
        var rtf = @"{\rtf1\ansi{\field{\*\fldinst HYPERLINK ""http://example.com""}{\fldrslt example}}\par}";
        var html = Convert(rtf);
        Assert.Contains("<a href=\"http://example.com\">example</a>", html);
    }
}
