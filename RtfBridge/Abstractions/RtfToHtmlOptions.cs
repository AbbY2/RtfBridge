// ============================================================================
// Product:   RtfBridge
// File:      RtfToHtmlOptions.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
namespace RtfBridge;

/// <summary>Options controlling RTF-to-HTML conversion.</summary>
public sealed class RtfToHtmlOptions
{
    /// <summary>Emit a full <c>&lt;!DOCTYPE html&gt;...&lt;/html&gt;</c> document. When false, only the body content is returned.</summary>
    public bool EmitFullDocument { get; set; } = true;

    /// <summary>Emit CSS as inline <c>style="..."</c> attributes (email-safe) instead of a shared <c>&lt;style&gt;</c> block.</summary>
    public bool InlineStyles { get; set; } = false;

    /// <summary>Preserve nested list levels (<c>\lsN \ilvlN</c>) as nested &lt;ul&gt;/&lt;ol&gt;.</summary>
    public bool PreserveListNesting { get; set; } = true;

    /// <summary>Emit RTF header/footer destinations as HTML &lt;header&gt;/&lt;footer&gt; elements.</summary>
    public bool RenderHeadersFooters { get; set; } = false;

    /// <summary>Collect footnotes into an ordered list at the end of the document.</summary>
    public bool RenderFootnotesAsList { get; set; } = true;

    /// <summary>Emit <c>\page</c> and <c>\sect</c> as CSS page breaks.</summary>
    public bool RenderPageBreaks { get; set; } = true;

    /// <summary>Guarantee byte-identical HTML output for identical input (stable attribute + CSS ordering).</summary>
    public bool DeterministicOutput { get; set; } = true;

    /// <summary>ANSI code page used to decode <c>\'hh</c> escapes when the RTF does not specify <c>\ansicpg</c>. Default 1252.</summary>
    public int DefaultCodePage { get; set; } = 1252;

    /// <summary>HTML sanitization level (see <see cref="HtmlSanitizationLevel"/>).</summary>
    public HtmlSanitizationLevel Sanitization { get; set; } = HtmlSanitizationLevel.Basic;

    /// <summary>Optional renderer for vector image formats (WMF/EMF). When null, vector images become an inline placeholder + warning.</summary>
    public IImageRenderer? VectorImageRenderer { get; set; }

    /// <summary>Maximum raw byte length allowed for any single embedded image. Larger images produce a warning and are skipped.</summary>
    public int MaxImageBytes { get; set; } = 20 * 1024 * 1024;

    /// <summary>Emit RTF hidden text (\v) as visible HTML. When false (default), \v runs are dropped.</summary>
    public bool RenderHiddenText { get; set; } = false;

    /// <summary>Default options.</summary>
    public static RtfToHtmlOptions Default => new RtfToHtmlOptions();

    /// <summary>Email-friendly options: no full document, inline styles.</summary>
    public static RtfToHtmlOptions EmailSafe => new RtfToHtmlOptions { EmitFullDocument = false, InlineStyles = true };

    /// <summary>Fragment-only options: emit only the body content.</summary>
    public static RtfToHtmlOptions FragmentOnly => new RtfToHtmlOptions { EmitFullDocument = false };
}
