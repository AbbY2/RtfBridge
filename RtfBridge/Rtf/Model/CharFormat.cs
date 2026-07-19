// ============================================================================
// Product:   RtfBridge
// File:      CharFormat.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
namespace RtfBridge.Rtf.Model;

/// <summary>Character-run visual properties.</summary>
internal sealed class CharFormat
{
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool Strike { get; set; }
    public VerticalPosition VerticalPosition { get; set; } = VerticalPosition.Normal;
    public int?  FontIndex { get; set; }
    /// <summary>Font size in half-points as stored in RTF (\fsN).</summary>
    public int?  FontSizeHalfPoints { get; set; }
    public int?  ForegroundColorIndex { get; set; }
    public int?  HighlightColorIndex { get; set; }

    /// <summary>Render text as ALL CAPS (\caps).</summary>
    public bool AllCaps { get; set; }

    /// <summary>Render text as small caps (\scaps).</summary>
    public bool SmallCaps { get; set; }

    /// <summary>Hidden text (\v). Not rendered by default; enabled via options.</summary>
    public bool Hidden { get; set; }

    /// <summary>Character-run direction: right-to-left when true (\rtlch), left-to-right when false (\ltrch).</summary>
    public bool RightToLeft { get; set; }

    /// <summary>Underline style variants (\ul, \uld, \uldb, \ulwave, ...).</summary>
    public UnderlineStyle UnderlineStyle { get; set; } = UnderlineStyle.None;

    /// <summary>BCP-47 language tag derived from \langN (LCID). Optional.</summary>
    public int? LanguageId { get; set; }

    /// <summary>Baseline shift in half-points (\upN positive = raise, \dnN negative = lower). 0 = no shift.</summary>
    public int BaselineShiftHalfPoints { get; set; }

    public CharFormat Clone() => new CharFormat
    {
        Bold = Bold,
        Italic = Italic,
        Underline = Underline,
        Strike = Strike,
        VerticalPosition = VerticalPosition,
        FontIndex = FontIndex,
        FontSizeHalfPoints = FontSizeHalfPoints,
        ForegroundColorIndex = ForegroundColorIndex,
        HighlightColorIndex = HighlightColorIndex,
        AllCaps = AllCaps,
        SmallCaps = SmallCaps,
        Hidden = Hidden,
        RightToLeft = RightToLeft,
        UnderlineStyle = UnderlineStyle,
        LanguageId = LanguageId,
        BaselineShiftHalfPoints = BaselineShiftHalfPoints,
    };
}

internal enum VerticalPosition
{
    Normal,
    Superscript,
    Subscript,
}

/// <summary>Underline style variants supported by RTF.</summary>
internal enum UnderlineStyle
{
    None,
    Single,
    Double,
    Dotted,
    Dashed,
    Wave,
    Thick,
}
