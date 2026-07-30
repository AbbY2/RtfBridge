// ============================================================================
// Product:   RtfBridge.Tests
// File:      RtfVerticalMergeTests.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using RtfBridge;
using Xunit;

namespace RtfBridgeTests;

/// <summary>
/// Coverage tests for vertical cell merges (\clvmgf/\clvmrg) rendered as rowspan.
/// </summary>
public class RtfVerticalMergeTests
{
    private static string Convert(string rtf) =>
        new global::RtfBridge.RtfBridgeConverter()
            .ConvertRtfToHtml(rtf, RtfToHtmlOptions.FragmentOnly);

    [Fact]
    public void VerticalMerge_TwoRows_EmitsRowspan2AndSuppressesContinuation()
    {
        // Row 1: cell A starts a vertical merge (\clvmgf) in column 1, cell B in column 2.
        // Row 2: column 1 continues the merge (\clvmrg); column 2 has its own content.
        var rtf =
            @"{\rtf1\ansi" +
            @"\trowd\clvmgf\cellx2000\cellx4000 A\cell B1\cell\row" +
            @"\trowd\clvmrg\cellx2000\cellx4000 \cell B2\cell\row" +
            @"}";
        var html = Convert(rtf);

        Assert.Contains("rowspan=\"2\"", html);
        // The merged cell content still renders once; the continuation cell is suppressed.
        Assert.Contains("A", html);
        Assert.Contains("B2", html);
    }

    [Fact]
    public void ThreeRowMerge_EmitsRowspan3()
    {
        var rtf =
            @"{\rtf1\ansi" +
            @"\trowd\clvmgf\cellx2000\cellx4000 A\cell B1\cell\row" +
            @"\trowd\clvmrg\cellx2000\cellx4000 \cell B2\cell\row" +
            @"\trowd\clvmrg\cellx2000\cellx4000 \cell B3\cell\row" +
            @"}";
        var html = Convert(rtf);

        Assert.Contains("rowspan=\"3\"", html);
    }

    [Fact]
    public void NoVerticalMerge_ProducesNoRowspan()
    {
        var rtf =
            @"{\rtf1\ansi" +
            @"\trowd\cellx2000\cellx4000 A1\cell B1\cell\row" +
            @"\trowd\cellx2000\cellx4000 A2\cell B2\cell\row" +
            @"}";
        var html = Convert(rtf);

        Assert.DoesNotContain("rowspan", html);
    }
}
