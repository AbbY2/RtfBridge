// ============================================================================
// Product:   RtfBridge
// File:      ParagraphFormat.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
namespace RtfBridge.Rtf.Model;

/// <summary>Paragraph-level visual properties.</summary>
internal sealed class ParagraphFormat
{
    public TextAlignment Alignment { get; set; } = TextAlignment.Left;

    /// <summary>Left indent in twips (1/1440 inch).</summary>
    public int LeftIndentTwips { get; set; }

    /// <summary>Right indent in twips.</summary>
    public int RightIndentTwips { get; set; }

    /// <summary>First-line indent in twips (can be negative for hanging).</summary>
    public int FirstLineIndentTwips { get; set; }

    /// <summary>Space before paragraph in twips.</summary>
    public int SpaceBeforeTwips { get; set; }

    /// <summary>Space after paragraph in twips.</summary>
    public int SpaceAfterTwips { get; set; }

    /// <summary>
    /// Line spacing in twips (\slN). Positive = "at least" or "exact" depending on \slmult.
    /// 0 = auto/single. Negative RTF values mean "exact" (absolute); we store the absolute value
    /// and set <see cref="LineHeightExact"/>.
    /// </summary>
    public int LineHeightTwips { get; set; }

    /// <summary>When true, \slmult1: line height is a multiple of single (240 twips = 1x).</summary>
    public bool LineHeightMultiple { get; set; }

    /// <summary>When true, \slN was negative — treat as an exact (non-expanding) line height.</summary>
    public bool LineHeightExact { get; set; }

    /// <summary>Paragraph direction: true = RTL (\rtlpar), false = LTR (\ltrpar / default).</summary>
    public bool RightToLeft { get; set; }

    /// <summary>Keep paragraph on one page (\keep).</summary>
    public bool KeepTogether { get; set; }

    /// <summary>Keep paragraph with the next paragraph (\keepn).</summary>
    public bool KeepWithNext { get; set; }

    /// <summary>Force a page break before this paragraph (\pagebb).</summary>
    public bool PageBreakBefore { get; set; }

    /// <summary>Horizontal absolute position in twips (\posx / \posnegx). 0 = unset.</summary>
    public int PositionXTwips { get; set; }

    /// <summary>Vertical absolute position in twips (\posy / \posnegy). 0 = unset.</summary>
    public int PositionYTwips { get; set; }

    /// <summary>Named horizontal position from \posxc/\posxl/\posxr, or None when \posxN is used.</summary>
    public NamedHorizontalPosition NamedHorizontalPosition { get; set; } = NamedHorizontalPosition.None;

    /// <summary>Named vertical position from \posyt/\posyc/\posyb, or None when \posyN is used.</summary>
    public NamedVerticalPosition NamedVerticalPosition { get; set; } = NamedVerticalPosition.None;

    /// <summary>Horizontal reference frame: page (\phpg), column (\phcol default), or margin (\phmrg).</summary>
    public PositionReference HorizontalReference { get; set; } = PositionReference.Column;

    /// <summary>Vertical reference frame: page (\pvpg), paragraph (\pvpara default), or margin (\pvmrg).</summary>
    public PositionReference VerticalReference { get; set; } = PositionReference.Paragraph;

    /// <summary>Paragraph background shading color-table index (\cbpat). Null = none.</summary>
    public int? BackgroundColorIndex { get; set; }

    /// <summary>Outline level 0..8 (\outlinelevelN or a "heading N" stylesheet style). Null = body text. 0..5 emit &lt;h1&gt;..&lt;h6&gt;.</summary>
    public int? OutlineLevel { get; set; }

    /// <summary>Paragraph borders (\brdrt/\brdrb/\brdrl/\brdrr, applied via \brdrbox).</summary>
    public BorderSide BorderTop    { get; set; } = new BorderSide();
    public BorderSide BorderRight  { get; set; } = new BorderSide();
    public BorderSide BorderBottom { get; set; } = new BorderSide();
    public BorderSide BorderLeft   { get; set; } = new BorderSide();

    /// <summary>Custom tab stops (\txN) in document order, with alignment and leaders.</summary>
    public System.Collections.Generic.List<TabStop> TabStops { get; set; } = new System.Collections.Generic.List<TabStop>();

    public ParagraphFormat Clone() => new ParagraphFormat
    {
        Alignment = Alignment,
        LeftIndentTwips = LeftIndentTwips,
        RightIndentTwips = RightIndentTwips,
        FirstLineIndentTwips = FirstLineIndentTwips,
        SpaceBeforeTwips = SpaceBeforeTwips,
        SpaceAfterTwips = SpaceAfterTwips,
        LineHeightTwips = LineHeightTwips,
        LineHeightMultiple = LineHeightMultiple,
        LineHeightExact = LineHeightExact,
        RightToLeft = RightToLeft,
        KeepTogether = KeepTogether,
        KeepWithNext = KeepWithNext,
        PageBreakBefore = PageBreakBefore,
        PositionXTwips = PositionXTwips,
        PositionYTwips = PositionYTwips,
        NamedHorizontalPosition = NamedHorizontalPosition,
        NamedVerticalPosition = NamedVerticalPosition,
        HorizontalReference = HorizontalReference,
        VerticalReference = VerticalReference,
        BackgroundColorIndex = BackgroundColorIndex,
        OutlineLevel = OutlineLevel,
        BorderTop    = BorderTop.Clone(),
        BorderRight  = BorderRight.Clone(),
        BorderBottom = BorderBottom.Clone(),
        BorderLeft   = BorderLeft.Clone(),
        TabStops     = TabStops.ConvertAll(t => t.Clone()),
    };
}

internal enum TextAlignment
{
    Left,
    Center,
    Right,
    Justify,
}

internal enum NamedHorizontalPosition { None, Left, Center, Right }
internal enum NamedVerticalPosition { None, Top, Center, Bottom }
internal enum PositionReference { Page, Column, Margin, Paragraph }
