// ============================================================================
// Product:   RtfBridge.Tests
// File:      RtfToHtmlBasicTests.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using RtfBridge;
using Xunit;

namespace RtfBridgeTests;

public class RtfToHtmlBasicTests
{
    private static string Convert(string rtf) =>
        new global::RtfBridge.RtfBridgeConverter()
            .ConvertRtfToHtml(rtf, RtfToHtmlOptions.FragmentOnly);

    [Fact]
    public void EmptyDocument_ProducesNoParagraphs()
    {
        var html = Convert(@"{\rtf1\ansi}");
        Assert.Equal(string.Empty, html);
    }

    [Fact]
    public void PlainText_EmittedInsideParagraph()
    {
        var html = Convert(@"{\rtf1\ansi Hello world}");
        Assert.Equal("<p>Hello world</p>", html);
    }

    [Fact]
    public void BoldItalicUnderline_MapToSemanticTags()
    {
        var html = Convert(@"{\rtf1\ansi {\b bold} {\i italic} {\ul under}}");
        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("<em>italic</em>", html);
        Assert.Contains("<u>under</u>", html);
    }

    [Fact]
    public void ParagraphBreak_ProducesTwoParagraphs()
    {
        var html = Convert(@"{\rtf1\ansi first\par second}");
        Assert.Equal("<p>first</p><p>second</p>", html);
    }

    [Fact]
    public void UnicodeEscape_DecodedAndFallbackSkipped()
    {
        // \u8364? = euro sign, ? is the ANSI fallback that must be skipped.
        var html = Convert(@"{\rtf1\ansi\uc1 price \u8364?100}");
        Assert.Contains("price €100", html);
    }

    [Fact]
    public void HexEscape_UsesAnsiCodePage()
    {
        // 0xA9 in cp1252 = ©
        var html = Convert(@"{\rtf1\ansi\ansicpg1252 (\'a9) 2025}");
        Assert.Contains("(©) 2025", html);
    }

    [Fact]
    public void HtmlSpecialChars_AreEscaped()
    {
        var html = Convert(@"{\rtf1\ansi a<b & c>d}");
        Assert.Contains("a&lt;b &amp; c&gt;d", html);
    }

    [Fact]
    public void CenterAlignment_EmitsInlineStyle()
    {
        var html = Convert(@"{\rtf1\ansi\qc centered\par}");
        Assert.Contains("text-align:center", html);
    }

    [Fact]
    public void UnknownDestination_IsSkipped()
    {
        var html = Convert(@"{\rtf1\ansi{\*\unknowndest secret}visible}");
        Assert.Equal("<p>visible</p>", html);
        Assert.DoesNotContain("secret", html);
    }

    [Fact]
    public void SpecialCharControlWords_DecodeToUnicode()
    {
        var html = Convert(@"{\rtf1\ansi \ldblquote hi\rdblquote \emdash bye}");
        Assert.Contains("\u201Chi\u201D\u2014bye", html);
    }

    [Fact]
    public void FontColor_FromColorTable_EmitsInlineStyle()
    {
        var rtf = @"{\rtf1\ansi{\colortbl ;\red255\green0\blue0;}\cf1 red}";
        var html = Convert(rtf);
        Assert.Contains("color:#ff0000", html);
        Assert.Contains(">red<", html);
    }

    [Fact]
    public void FontSize_HalfPoints_ConvertedToPt()
    {
        var html = Convert(@"{\rtf1\ansi\fs24 twelve}");
        Assert.Contains("font-size:12pt", html);
    }

    [Fact]
    public void Tab_ProducesEmSpace()
    {
        var html = Convert(@"{\rtf1\ansi a\tab b}");
        Assert.Contains("a&emsp;b", html);
    }

    [Fact]
    public void LineBreak_ProducesBrTag()
    {
        var html = Convert(@"{\rtf1\ansi one\line two}");
        Assert.Contains("one<br>two", html);
    }

    [Fact]
    public void PageBreak_ProducesHorizontalRule()
    {
        var html = Convert(@"{\rtf1\ansi before\page after}");
        Assert.Contains("page-break-after:always", html);
    }

    [Fact]
    public void ConversionIsDeterministic_ByteIdenticalAcrossRuns()
    {
        var rtf = @"{\rtf1\ansi{\colortbl ;\red10\green20\blue30;}\cf1\b\i\fs28 hello\par world}";
        var conv = new global::RtfBridge.RtfBridgeConverter();
        var a = conv.ConvertRtfToHtml(rtf);
        var b = conv.ConvertRtfToHtml(rtf);
        Assert.Equal(a, b);
    }

    [Fact]
    public void BinaryPayload_IsNotEmittedAsText()
    {
        // \bin4 followed by 4 raw bytes ('X','X','X','X') then visible text.
        var rtf = "{\\rtf1\\ansi \\bin4 XXXXafter}";
        var html = Convert(rtf);
        Assert.DoesNotContain("XXXX", html);
        Assert.Contains("after", html);
    }

    [Fact]
    public void FullDocumentMode_WrapsInHtmlAndBody()
    {
        var html = new global::RtfBridge.RtfBridgeConverter()
            .ConvertRtfToHtml(@"{\rtf1\ansi hi}");
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<body>", html);
        Assert.EndsWith("</body></html>", html);
    }
}
