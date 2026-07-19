// ============================================================================
// Product:   RtfBridge
// File:      RtfFontTable.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using System.Collections.Generic;

namespace RtfBridge.Rtf.Model;

internal sealed class RtfFontTable
{
    private readonly Dictionary<int, RtfFont> _fonts = new Dictionary<int, RtfFont>();

    public void Set(int index, RtfFont font) => _fonts[index] = font;

    public RtfFont? Get(int index) =>
        _fonts.TryGetValue(index, out var font) ? font : null;

    public bool TryGet(int index, out RtfFont font)
    {
        if (_fonts.TryGetValue(index, out var f)) { font = f; return true; }
        font = null!;
        return false;
    }
}

internal sealed class RtfFont
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public int CharSet { get; set; }
    public int PitchAndFamily { get; set; }
}

internal sealed class RtfColorTable
{
    private readonly List<RtfColor> _colors = new List<RtfColor>();

    public void Add(RtfColor color) => _colors.Add(color);

    public IReadOnlyList<RtfColor> Colors => _colors;

    public RtfColor? Get(int index) =>
        (index >= 0 && index < _colors.Count) ? _colors[index] : null;

    public RtfColor GetOrAuto(int index) =>
        (index >= 0 && index < _colors.Count) ? _colors[index] : RtfColor.Auto;
}

internal readonly struct RtfColor
{
    public byte R { get; }
    public byte G { get; }
    public byte B { get; }
    public bool IsAuto { get; }

    public RtfColor(byte r, byte g, byte b, bool isAuto = false)
    {
        R = r; G = g; B = b; IsAuto = isAuto;
    }

    public static RtfColor Auto => new RtfColor(0, 0, 0, isAuto: true);

    public string ToHex() => IsAuto ? "" : $"#{R:x2}{G:x2}{B:x2}";
}
