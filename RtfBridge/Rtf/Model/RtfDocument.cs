// ============================================================================
// Product:   RtfBridge
// File:      RtfDocument.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using System.Collections.Generic;

namespace RtfBridge.Rtf.Model;

/// <summary>Root RTF document model.</summary>
internal sealed class RtfDocument
{
    public int AnsiCodePage { get; set; } = 1252;
    public int DefaultFontIndex { get; set; }
    /// <summary>LCID from \deflangN or the \s0 Normal style \langN. 0 = unset.</summary>
    public int DefaultLanguageId { get; set; }

    /// <summary>Font size (half-points) from the \s0 Normal style. 0 = unset (falls back to RTF default 12pt).</summary>
    public int DefaultBodyFontSizeHalfPoints { get; set; }

    /// <summary>Color-table index from the \s0 Normal style \cfN. Null = unset (auto = black).</summary>
    public int? DefaultBodyColorIndex { get; set; }
    public RtfFontTable Fonts { get; } = new RtfFontTable();
    public RtfColorTable Colors { get; } = new RtfColorTable();
    public RtfListTable Lists { get; } = new RtfListTable();
    public List<RtfBlock> Body { get; } = new List<RtfBlock>();

    /// <summary>Captured header content (may be empty). Emission gated by options.</summary>
    public List<RtfRun> Header { get; } = new List<RtfRun>();

    /// <summary>Captured footer content (may be empty). Emission gated by options.</summary>
    public List<RtfRun> Footer { get; } = new List<RtfRun>();

    /// <summary>Collected footnotes, indexed by 1-based reference number.</summary>
    public List<RtfFootnote> Footnotes { get; } = new List<RtfFootnote>();
}

internal abstract class RtfBlock { }

internal sealed class RtfParagraph : RtfBlock
{
    public ParagraphFormat Format { get; set; } = new ParagraphFormat();
    public List<RtfRun> Runs { get; } = new List<RtfRun>();

    /// <summary>Non-null when the paragraph belongs to a list (\lsN).</summary>
    public int? ListId { get; set; }

    /// <summary>Zero-based list nesting level (\ilvlN). Defaults to 0.</summary>
    public int ListLevel { get; set; }
}

internal sealed class RtfPageBreak : RtfBlock { }

internal sealed class RtfSectionBreak : RtfBlock { }

/// <summary>A materialised list of paragraphs formed from consecutive list-flagged paragraphs.</summary>
internal sealed class RtfList : RtfBlock
{
    public bool Ordered { get; set; }
    public List<RtfListItem> Items { get; } = new List<RtfListItem>();

    /// <summary>Reference to the list definition containing level formatting info. Null for legacy lists.</summary>
    public RtfListDefinition? Definition { get; set; }

    /// <summary>List ID from \lsN. Used to look up definition.</summary>
    public int? ListId { get; set; }
}

internal sealed class RtfListItem
{
    public int Level { get; set; }
    public List<RtfRun> Runs { get; } = new List<RtfRun>();
    public ParagraphFormat Format { get; set; } = new ParagraphFormat();

    /// <summary>The numbering value for this item (1-based). Used for custom start values.</summary>
    public int Number { get; set; } = 1;
}

/// <summary>A block-level table built from \trowd..\row groups.</summary>
internal sealed class RtfTable : RtfBlock
{
    public List<RtfRow> Rows { get; } = new List<RtfRow>();
}

internal sealed class RtfRow
{
    public List<RtfCell> Cells { get; } = new List<RtfCell>();

    /// <summary>Row height in twips (\trrh). 0 = unset. Negative in RTF = "exact"; stored as absolute.</summary>
    public int HeightTwips { get; set; }

    /// <summary>True when the row is exact-height (negative \trrhN).</summary>
    public bool HeightExact { get; set; }

    /// <summary>Row-level horizontal alignment (\trql/\trqc/\trqr).</summary>
    public TextAlignment Alignment { get; set; } = TextAlignment.Left;

    /// <summary>True when \trhdr marks this row as a header row (repeats across pages).</summary>
    public bool IsHeader { get; set; }

    /// <summary>Default cell padding (in twips) from \trpaddl/t/r/b. 0 = unset.</summary>
    public int PaddingLeftTwips { get; set; }
    public int PaddingTopTwips { get; set; }
    public int PaddingRightTwips { get; set; }
    public int PaddingBottomTwips { get; set; }
}

internal sealed class RtfCell
{
    /// <summary>Number of columns this cell spans horizontally (>= 1).</summary>
    public int ColSpan { get; set; } = 1;

    /// <summary>Number of rows this cell spans vertically (>= 1), resolved from \clvmgf/\clvmrg.</summary>
    public int RowSpan { get; set; } = 1;

    /// <summary>True when this cell begins a vertical merge (\clvmgf).</summary>
    public bool VerticalMergeStart { get; set; }

    /// <summary>True when this cell continues a vertical merge from the row above (\clvmrg).</summary>
    public bool VerticalMergeContinue { get; set; }

    /// <summary>Set during merge resolution when this cell is absorbed by an origin cell and must not render.</summary>
    public bool SuppressRender { get; set; }

    /// <summary>Right boundary in twips (\cellxN).</summary>
    public int RightEdgeTwips { get; set; }

    /// <summary>Explicit cell width in twips (\clwWidth). 0 = derive from RightEdgeTwips.</summary>
    public int WidthTwips { get; set; }

    /// <summary>Vertical alignment within cell (\clvertalt/c/b).</summary>
    public CellVerticalAlignment VerticalAlignment { get; set; } = CellVerticalAlignment.Top;

    /// <summary>Background shading color-table index (\clcbpat). Null = none.</summary>
    public int? BackgroundColorIndex { get; set; }

    /// <summary>Cell borders (top/right/bottom/left).</summary>
    public BorderSide BorderTop    { get; set; } = new BorderSide();
    public BorderSide BorderRight  { get; set; } = new BorderSide();
    public BorderSide BorderBottom { get; set; } = new BorderSide();
    public BorderSide BorderLeft   { get; set; } = new BorderSide();

    public List<RtfBlock> Blocks { get; } = new List<RtfBlock>();
}

internal enum CellVerticalAlignment { Top, Center, Bottom }

/// <summary>Style of a single border side.</summary>
internal sealed class BorderSide
{
    public BorderStyle Style { get; set; } = BorderStyle.None;
    /// <summary>Border width in twips (\brdrwN). 0 = default.</summary>
    public int WidthTwips { get; set; }
    /// <summary>Color-table index (\brdrcfN). Null = auto/default.</summary>
    public int? ColorIndex { get; set; }

    public BorderSide Clone() => new BorderSide
    {
        Style = Style,
        WidthTwips = WidthTwips,
        ColorIndex = ColorIndex,
    };

    public bool IsVisible => Style != BorderStyle.None;
}

internal enum BorderStyle { None, Single, Double, Dotted, Dashed, Hair, Thick }

/// <summary>List-table info parsed from \listtable / \listoverridetable.</summary>
internal sealed class RtfListTable
{
    private readonly Dictionary<int, RtfListDefinition> _byListId = new Dictionary<int, RtfListDefinition>();
    private readonly Dictionary<int, int> _lsToListId = new Dictionary<int, int>();

    public void SetDefinition(int listId, RtfListDefinition def) => _byListId[listId] = def;
    public void MapOverride(int ls, int listId) => _lsToListId[ls] = listId;

    /// <summary>Resolve the definition referenced by \lsN. Returns false when unknown.</summary>
    public bool TryGet(int ls, out RtfListDefinition def)
    {
        if (_lsToListId.TryGetValue(ls, out int listId) &&
            _byListId.TryGetValue(listId, out var d))
        {
            def = d; return true;
        }
        // Fallback: some minimal RTFs use \lsN directly as the list id.
        if (_byListId.TryGetValue(ls, out var direct))
        {
            def = direct; return true;
        }
        def = default!;
        return false;
    }
}

internal sealed class RtfListDefinition
{
    /// <summary>True when at least one level uses a numeric bullet type (0=Arabic, ...).</summary>
    public bool Ordered { get; set; }

    /// <summary>List level definitions (max 9 levels, indexed 0-8). Empty for simple legacy lists.</summary>
    public List<RtfListLevelDefinition> Levels { get; } = new List<RtfListLevelDefinition>();
}

/// <summary>Definition for a single list level parsed from \listlevel.</summary>
internal sealed class RtfListLevelDefinition
{
    /// <summary>Numbering format (\levelnfc / \levelnfcn): 0=Arabic, 1=UpperRoman, 2=LowerRoman, 3=UpperAlpha, 4=LowerAlpha, 23=Bullet, etc.</summary>
    public int NumberFormat { get; set; }

    /// <summary>Start value for numbered lists (\levelstartat). Default is 1.</summary>
    public int StartAt { get; set; } = 1;

    /// <summary>Level text template (\leveltext), e.g., "%1.", "%1.%2.", or a bullet character.</summary>
    public string? LevelText { get; set; }

    /// <summary>Justification (\leveljc): 0=left, 1=center, 2=right.</summary>
    public int Justification { get; set; }

    /// <summary>Follow character (\levelfollow): 0=tab, 1=space, 2=nothing.</summary>
    public int FollowChar { get; set; }

    /// <summary>True if this level restarts numbering (\levelnorestart 0 or absent).</summary>
    public bool Restart { get; set; } = true;

    /// <summary>True if this is a legal-style numbered list (\levellegal).</summary>
    public bool Legal { get; set; }

    /// <summary>Font index for bullet character (\levelnumbers or level text parsing).</summary>
    public int? FontIndex { get; set; }
}

/// <summary>A footnote captured from a \footnote destination.</summary>
internal sealed class RtfFootnote
{
    /// <summary>1-based reference number assigned in document order.</summary>
    public int Number { get; set; }
    public List<RtfRun> Runs { get; } = new List<RtfRun>();
}

