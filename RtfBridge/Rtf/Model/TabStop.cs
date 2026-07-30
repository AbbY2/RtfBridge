// ============================================================================
// Product:   RtfBridge
// File:      TabStop.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
namespace RtfBridge.Rtf.Model;

/// <summary>A single paragraph tab stop (\txN with optional alignment and leader).</summary>
internal sealed class TabStop
{
    /// <summary>Absolute tab-stop position from the left margin, in twips.</summary>
    public int PositionTwips { get; set; }

    /// <summary>Tab alignment (\tql default, \tqr, \tqc, \tqdec).</summary>
    public TabAlignment Alignment { get; set; } = TabAlignment.Left;

    /// <summary>Leader fill between the previous content and this stop (\tldot etc.).</summary>
    public TabLeader Leader { get; set; } = TabLeader.None;

    public TabStop Clone() => new TabStop
    {
        PositionTwips = PositionTwips,
        Alignment = Alignment,
        Leader = Leader,
    };
}

/// <summary>Tab-stop alignment kinds.</summary>
internal enum TabAlignment
{
    Left,
    Right,
    Center,
    Decimal,
}

/// <summary>Tab-stop leader fill kinds.</summary>
internal enum TabLeader
{
    None,
    Dot,
    Hyphen,
    Underline,
    Thick,
    Equal,
}
