// ============================================================================
// Product:   RtfBridge.Tests
// File:      RtfPhase8Tests.cs
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
/// Phase 8 regression tests: cell borders, cell shading, cell vertical alignment,
/// explicit cell width, row header/alignment/padding, and paragraph borders/shading.
/// </summary>
public class RtfPhase8Tests
{
    private static string Convert(string rtf) =>
        new global::RtfBridge.RtfBridge()
            .ConvertRtfToHtml(rtf, RtfToHtmlOptions.FragmentOnly);

    // ---- header row + row alignment ------------------------------------------------

    [Fact]
    public void TrHdr_EmitsTheadWithThCells()
    {
        // Two rows: first is a header (\trhdr), second is a body row.
        var rtf =
            @"{\rtf1\ansi" +
            @"\trowd\trhdr\cellx3000\cellx6000 A\cell B\cell \row" +
            @"\trowd\cellx3000\cellx6000 C\cell D\cell \row" +
            "}";
        var html = Convert(rtf);
        Assert.Contains("<thead>", html);
        Assert.Contains("<th ", html);
        Assert.Contains("<tbody>", html);
        Assert.Contains("<td ", html);
    }

    [Fact]
    public void TrQc_EmitsTextAlignCenterOnRow()
    {
        var rtf =
            @"{\rtf1\ansi" +
            @"\trowd\trqc\cellx3000\cellx6000 A\cell B\cell \row" +
            "}";
        var html = Convert(rtf);
        Assert.Contains("<tr style=\"text-align:center\">", html);
    }

    // ---- cell shading / vertical align / width -----------------------------------

    [Fact]
    public void ClCbPat_EmitsCellBackgroundFromColorTable()
    {
        // Color 2 in the color table = red (#FF0000).
        var rtf =
            @"{\rtf1\ansi{\colortbl;\red0\green0\blue0;\red255\green0\blue0;}" +
            @"\trowd\clcbpat2\cellx3000 X\cell \row" +
            "}";
        var html = Convert(rtf);
        Assert.Contains("background-color:#ff0000", html);
    }

    [Fact]
    public void ClVertAlB_EmitsVerticalAlignBottom()
    {
        var rtf =
            @"{\rtf1\ansi" +
            @"\trowd\clvertalb\cellx3000 X\cell \row" +
            "}";
        var html = Convert(rtf);
        Assert.Contains("vertical-align:bottom", html);
    }

    [Fact]
    public void ClwWidth_EmitsExplicitCellWidth()
    {
        // 1440 twips = 72pt.
        var rtf =
            @"{\rtf1\ansi" +
            @"\trowd\clwWidth1440\cellx3000 X\cell \row" +
            "}";
        var html = Convert(rtf);
        Assert.Contains("width:72pt", html);
    }

    // ---- cell borders --------------------------------------------------------------

    [Fact]
    public void ClBrdrT_Single_EmitsBorderTop()
    {
        // Solid top border on the cell, width 20 twips (~1.33px, floored to 1px in HTML).
        var rtf =
            @"{\rtf1\ansi" +
            @"\trowd\clbrdrt\brdrs\brdrw20\cellx3000 X\cell \row" +
            "}";
        var html = Convert(rtf);
        Assert.Contains("border-top:", html);
        Assert.Contains(" solid", html);
    }

    [Fact]
    public void ClBrdrB_Double_EmitsDoubleStyle()
    {
        var rtf =
            @"{\rtf1\ansi" +
            @"\trowd\clbrdrb\brdrdb\brdrw40\cellx3000 X\cell \row" +
            "}";
        var html = Convert(rtf);
        Assert.Contains("border-bottom:", html);
        Assert.Contains("double", html);
    }

    // ---- paragraph borders + shading ---------------------------------------------

    [Fact]
    public void ParagraphBrdrBox_EmitsAllFourSides()
    {
        var rtf = @"{\rtf1\ansi\pard\brdrbox\brdrs\brdrw20 Hello.\par}";
        var html = Convert(rtf);
        Assert.Contains("border-top:", html);
        Assert.Contains("border-right:", html);
        Assert.Contains("border-bottom:", html);
        Assert.Contains("border-left:", html);
    }

    [Fact]
    public void ParagraphCbPat_EmitsBackgroundColor()
    {
        // Color index 2 = red (#FF0000).
        var rtf =
            @"{\rtf1\ansi{\colortbl;\red0\green0\blue0;\red255\green0\blue0;}" +
            @"\pard\cbpat2 Hello.\par}";
        var html = Convert(rtf);
        Assert.Contains("background-color:#ff0000", html);
    }
}
