// ============================================================================
// Product:   RtfBridge.Tests
// File:      RtfFuzzTests.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using System;
using System.Diagnostics;
using System.Text;
using RtfBridge;
using Xunit;

namespace RtfBridgeTests;

public class RtfFuzzTests
{
    private static string Convert(string rtf) =>
        new global::RtfBridge.RtfBridgeConverter()
            .ConvertRtfToHtml(rtf, RtfToHtmlOptions.FragmentOnly);

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("}")]
    [InlineData(@"{\rtf1")]
    [InlineData(@"{\rtf1\ansi")]
    [InlineData(@"{\rtf1\ansi{{{{{{")]
    [InlineData(@"{\rtf1\ansi}}}}}}")]
    [InlineData(@"{\rtf1\ansi\bin100 ")] // truncated \bin
    [InlineData(@"{\rtf1\ansi\u")]        // \u without parameter
    [InlineData(@"{\rtf1\ansi\'zz}")]      // invalid hex
    [InlineData(@"{\rtf1\ansi\field{\*\fldinst}}")] // empty field
    [InlineData(@"{\rtf1\ansi{\pict\pngblip}}")]     // pict with no data
    [InlineData(@"{\rtf1\ansi{\pict\pngblip GGGG}}")] // invalid hex in pict
    [InlineData(@"{\rtf1\ansi\trowd\cell\row")]      // table row with no cellx
    [InlineData(@"{\rtf1\ansi{\*\bkmkstart}}")]      // empty bookmark
    [InlineData(@"{\rtf1\ansi{\footnote")]           // unterminated footnote
    [InlineData(@"\rtf1\ansi Hello without braces")]  // missing root group
    public void MalformedInput_DoesNotThrowOrHang(string rtf)
    {
        var sw = Stopwatch.StartNew();
        string html = Convert(rtf);
        sw.Stop();

        Assert.NotNull(html);
        Assert.True(sw.ElapsedMilliseconds < 2000, $"conversion took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void DeeplyNestedGroups_DoNotStackOverflow()
    {
        var sb = new StringBuilder(@"{\rtf1\ansi");
        for (int i = 0; i < 5000; i++) sb.Append('{');
        sb.Append(" deep ");
        for (int i = 0; i < 5000; i++) sb.Append('}');
        sb.Append('}');

        var sw = Stopwatch.StartNew();
        string html = Convert(sb.ToString());
        sw.Stop();

        Assert.NotNull(html);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"conversion took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void RandomGarbageWithControlPrefix_DoesNotThrow()
    {
        var rng = new Random(12345);
        var buf = new byte[4096];
        for (int iter = 0; iter < 50; iter++)
        {
            rng.NextBytes(buf);
            string rtf = @"{\rtf1\ansi " + Encoding.ASCII.GetString(buf) + "}";
            string html = Convert(rtf);
            Assert.NotNull(html);
        }
    }
}
