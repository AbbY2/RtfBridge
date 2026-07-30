// ============================================================================
// Product:   RtfBridge
// File:      RtfStyleDefinition.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
namespace RtfBridge.Rtf.Model;

/// <summary>
/// A named style entry captured from the RTF <c>\stylesheet</c>. Properties are stored as
/// nullable "deltas": only formatting that the style definition explicitly declares is set,
/// so applying a style overlays those values onto the current run/paragraph state while
/// leaving everything else (and document-level defaults) intact.
/// </summary>
internal sealed class RtfStyleDefinition
{
    /// <summary>The style index (\sN).</summary>
    public int Index { get; set; }

    /// <summary>Index of the style this one is based on (\sbasedonN), or null.</summary>
    public int? BasedOn { get; set; }

    // ---------- character deltas ----------
    public bool? Bold { get; set; }
    public bool? Italic { get; set; }
    public bool? Strike { get; set; }
    public bool? AllCaps { get; set; }
    public bool? SmallCaps { get; set; }
    public UnderlineStyle? Underline { get; set; }
    public VerticalPosition? VerticalPosition { get; set; }
    public int? FontIndex { get; set; }
    public int? FontSizeHalfPoints { get; set; }
    public int? ForegroundColorIndex { get; set; }
    public int? HighlightColorIndex { get; set; }

    // ---------- paragraph deltas ----------
    public TextAlignment? Alignment { get; set; }
    public int? LeftIndentTwips { get; set; }
    public int? RightIndentTwips { get; set; }
    public int? FirstLineIndentTwips { get; set; }
    public int? SpaceBeforeTwips { get; set; }
    public int? SpaceAfterTwips { get; set; }
    public int? OutlineLevel { get; set; }
    public bool? KeepTogether { get; set; }
    public bool? KeepWithNext { get; set; }
    public bool? PageBreakBefore { get; set; }
}
