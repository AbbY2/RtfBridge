// ============================================================================
// Product:   RtfBridge
// File:      RtfRun.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
namespace RtfBridge.Rtf.Model;

/// <summary>Base for inline runs inside a paragraph.</summary>
internal abstract class RtfRun
{
    public CharFormat Format { get; set; } = new CharFormat();
}

/// <summary>A run of literal text.</summary>
internal sealed class RtfTextRun : RtfRun
{
    public string Text { get; set; } = "";
}

/// <summary>An explicit line break inside a paragraph (RTF \line).</summary>
internal sealed class RtfLineBreak : RtfRun { }

/// <summary>A tab character.</summary>
internal sealed class RtfTab : RtfRun
{
    /// <summary>
    /// Resolved advance width in twips when this tab lands on a defined tab stop; 0 when it
    /// falls back to a default tab (rendered as an em-space).
    /// </summary>
    public int WidthTwips { get; set; }

    /// <summary>Leader fill for the resolved tab stop (\tldot etc.).</summary>
    public TabLeader Leader { get; set; } = TabLeader.None;
}

/// <summary>An embedded image (from a \pict destination).</summary>
internal sealed class RtfImageRun : RtfRun
{
    /// <summary>Raw image bytes. Empty when the picture could not be decoded and a placeholder is used.</summary>
    public byte[] Bytes { get; set; } = System.Array.Empty<byte>();

    /// <summary>IANA media type (e.g. image/png, image/jpeg, image/bmp). "" when placeholder.</summary>
    public string MediaType { get; set; } = "";

    /// <summary>Desired width in pixels (0 = auto).</summary>
    public int WidthPx { get; set; }

    /// <summary>Desired height in pixels (0 = auto).</summary>
    public int HeightPx { get; set; }

    /// <summary>Text-wrap mode from \shpwr on the containing shape. 0 = inline (default).</summary>
    public ImageWrapMode WrapMode { get; set; } = ImageWrapMode.Inline;

    /// <summary>When true, emit a text/placeholder instead of an &lt;img&gt; tag.</summary>
    public bool IsPlaceholder { get; set; }
}

/// <summary>Text-wrap mode for a shape-anchored image, from \shpwr.</summary>
internal enum ImageWrapMode
{
    /// <summary>No explicit wrap; render inline.</summary>
    Inline = 0,
    /// <summary>\shpwr1 — no text wrap (top/bottom).</summary>
    TopBottom,
    /// <summary>\shpwr2 — around (tight/square).</summary>
    Around,
    /// <summary>\shpwr3 — none (in front of / behind text).</summary>
    None,
    /// <summary>\shpwr4 — tight around.</summary>
    Tight,
    /// <summary>\shpwr5 — through.</summary>
    Through,
}

/// <summary>A hyperlink wrapping one or more child runs (result of a HYPERLINK field).</summary>
internal sealed class RtfHyperlinkRun : RtfRun
{
    public string Url { get; set; } = "";
    public System.Collections.Generic.List<RtfRun> Children { get; } = new System.Collections.Generic.List<RtfRun>();
}

/// <summary>A bookmark anchor emitted from \bkmkstart NAME.</summary>
internal sealed class RtfBookmarkAnchor : RtfRun
{
    public string Name { get; set; } = "";
}

/// <summary>An inline footnote reference emitted where \footnote occurs; content is stored on RtfDocument.Footnotes.</summary>
internal sealed class RtfFootnoteRef : RtfRun
{
    public int Number { get; set; }
}

