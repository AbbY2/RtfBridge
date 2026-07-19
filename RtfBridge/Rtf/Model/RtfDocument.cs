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
}

internal sealed class RtfListItem
{
    public int Level { get; set; }
    public List<RtfRun> Runs { get; } = new List<RtfRun>();
    public ParagraphFormat Format { get; set; } = new ParagraphFormat();
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
}

/// <summary>A footnote captured from a \footnote destination.</summary>
internal sealed class RtfFootnote
{
    /// <summary>1-based reference number assigned in document order.</summary>
    public int Number { get; set; }
    public List<RtfRun> Runs { get; } = new List<RtfRun>();
}

