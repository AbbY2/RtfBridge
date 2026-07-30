// ============================================================================
// Product:   RtfBridge
// File:      HtmlEmitter.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RtfBridge.Rtf;
using RtfBridge.Rtf.Model;

namespace RtfBridge.Html;

/// <summary>
/// Deterministic HTML5 emitter for a parsed <see cref="RtfDocument"/>.
/// Paragraph formatting maps to inline CSS on &lt;p&gt;/&lt;span&gt; so output is email-safe.
/// </summary>
internal sealed class HtmlEmitter
{
    private readonly RtfDocument _doc;
    private readonly RtfToHtmlOptions _options;

    // Body defaults computed from the Normal (\s0) style.
    // Null = not computed / fragment mode — no suppression applied.
    private string? _bodyFontFamily;
    private string? _bodyFontSizePt;
    private string? _bodyColorHex;

    public HtmlEmitter(RtfDocument doc, RtfToHtmlOptions options)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    private void ComputeBodyDefaults()
    {
        if (_doc.Fonts.TryGet(_doc.DefaultFontIndex, out var defFont) && !string.IsNullOrEmpty(defFont.Name))
            _bodyFontFamily = "'" + EscapeCssString(defFont.Name) + "'";

        int halfPts = _doc.DefaultBodyFontSizeHalfPoints > 0 ? _doc.DefaultBodyFontSizeHalfPoints : 24;
        _bodyFontSizePt = (halfPts / 2.0).ToString("0.##", CultureInfo.InvariantCulture) + "pt";

        if (_doc.DefaultBodyColorIndex.HasValue)
        {
            var c = _doc.Colors.GetOrAuto(_doc.DefaultBodyColorIndex.Value);
            if (!c.IsAuto) _bodyColorHex = ToCssHex(c);
        }
        _bodyColorHex ??= "#000000";
    }

    public string Emit()
    {
        var sb = new StringBuilder(1024);

        if (_options.EmitFullDocument)
        {
            ComputeBodyDefaults();
            sb.Append("<!DOCTYPE html><html");
            string? docLang = LcidToBcp47(_doc.DefaultLanguageId);
            if (docLang != null) sb.Append(" lang=\"").Append(docLang).Append('"');
            sb.Append("><head><meta charset=\"utf-8\">");
            EmitBodyStyleBlock(sb);
            sb.Append("</head><body>");
        }

        if (_options.RenderHeadersFooters && _doc.Header.Count > 0)
        {
            sb.Append("<header>");
            foreach (var run in _doc.Header) EmitRun(sb, run);
            sb.Append("</header>");
        }

        foreach (var block in _doc.Body)
        {
            switch (block)
            {
                case RtfParagraph p:
                    EmitParagraph(sb, p);
                    break;
                case RtfList l:
                    EmitList(sb, l);
                    break;
                case RtfTable t:
                    EmitTable(sb, t);
                    break;
                case RtfPageBreak _:
                    if (_options.RenderPageBreaks)
                        sb.Append("<hr style=\"page-break-after:always;border:0;\">");
                    break;
                case RtfSectionBreak _:
                    if (_options.RenderPageBreaks)
                        sb.Append("<hr style=\"page-break-after:always;border:0;\">");
                    break;
            }
        }

        if (_options.RenderFootnotesAsList && _doc.Footnotes.Count > 0)
        {
            sb.Append("<hr><ol class=\"footnotes\">");
            foreach (var note in _doc.Footnotes)
            {
                sb.Append("<li id=\"fn-").Append(note.Number.ToString(CultureInfo.InvariantCulture)).Append("\">");
                foreach (var run in note.Runs) EmitRun(sb, run);
                sb.Append("</li>");
            }
            sb.Append("</ol>");
        }

        if (_options.RenderHeadersFooters && _doc.Footer.Count > 0)
        {
            sb.Append("<footer>");
            foreach (var run in _doc.Footer) EmitRun(sb, run);
            sb.Append("</footer>");
        }

        if (_options.EmitFullDocument)
        {
            sb.Append("</body></html>");
        }

        return sb.ToString();
    }

    private void EmitBodyStyleBlock(StringBuilder sb)
    {
        sb.Append("<style>");
        sb.Append("body{");
        if (_bodyFontFamily != null) sb.Append("font-family:").Append(_bodyFontFamily).Append(';');
        if (_bodyFontSizePt  != null) sb.Append("font-size:").Append(_bodyFontSizePt).Append(';');
        if (_bodyColorHex    != null) sb.Append("color:").Append(_bodyColorHex).Append(';');
        sb.Append("widows:0;orphans:0}");
        sb.Append("h1,h2,h3,h4,h5,h6,p{margin:0}");
        sb.Append("li,table{margin-top:0;margin-bottom:0}");
        sb.Append("img{max-width:100%;height:auto}");
        sb.Append("table{border-collapse:collapse}");
        sb.Append("</style>");
    }

    private void EmitParagraph(StringBuilder sb, RtfParagraph p)
    {
        // Outline levels 0..5 (\outlinelevelN or "heading N" styles) become <h1>..<h6>.
        string tag = p.Format.OutlineLevel is int ol && ol >= 0 && ol <= 5 && p.Runs.Count > 0
            ? "h" + (ol + 1).ToString(CultureInfo.InvariantCulture)
            : "p";

        string style = BuildParagraphStyle(p.Format);
        sb.Append('<').Append(tag);
        if (p.Format.RightToLeft) sb.Append(" dir=\"rtl\"");
        if (style.Length > 0)
        {
            sb.Append(" style=\"").Append(style).Append('"');
        }
        sb.Append('>');

        if (p.Runs.Count == 0)
        {
            // Preserve empty paragraphs — &nbsp; keeps the line height without injecting a block element.
            sb.Append("&nbsp;");
        }
        else
        {
            foreach (var run in MergeRuns(p.Runs))
            {
                EmitRun(sb, run);
            }
        }

        sb.Append("</").Append(tag).Append('>');
    }

    private void EmitRun(StringBuilder sb, RtfRun run)
    {
        switch (run)
        {
            case RtfLineBreak _:
                sb.Append("<br>");
                return;
            case RtfTab tab:
                if (tab.WidthTwips > 0)
                {
                    // Tab resolved to a defined stop: emit a sized spacer, adding a baseline
                    // leader line when the stop specifies one (e.g. TOC dot leaders).
                    sb.Append("<span style=\"display:inline-block;width:")
                      .Append(TwipsToPt(tab.WidthTwips)).Append("pt");
                    string leaderCss = TabLeaderCss(tab.Leader);
                    if (leaderCss.Length > 0) sb.Append(';').Append(leaderCss);
                    sb.Append("\"></span>");
                }
                else
                {
                    // Default tab: preserve as an em-space; keeps whitespace deterministic.
                    sb.Append("&emsp;");
                }
                return;
            case RtfImageRun img:
                EmitImage(sb, img);
                return;
            case RtfHyperlinkRun link:
                EmitHyperlink(sb, link);
                return;
            case RtfBookmarkAnchor bm:
                sb.Append("<a id=\"");
                AppendAttributeEscaped(sb, bm.Name);
                sb.Append("\"></a>");
                return;
            case RtfFootnoteRef fn:
                sb.Append("<sup><a href=\"#fn-")
                    .Append(fn.Number.ToString(CultureInfo.InvariantCulture))
                    .Append("\">")
                    .Append(fn.Number.ToString(CultureInfo.InvariantCulture))
                    .Append("</a></sup>");
                return;
            case RtfTextRun t:
                EmitTextRun(sb, t);
                return;
        }
    }

    private void EmitImage(StringBuilder sb, RtfImageRun img)
    {
        string? wrapStyle = img.WrapMode switch
        {
            ImageWrapMode.Around or ImageWrapMode.Tight or ImageWrapMode.Through => "float:left",
            ImageWrapMode.TopBottom => "clear:both;display:block",
            _ => null,
        };

        if (img.IsPlaceholder || img.Bytes.Length == 0 || img.MediaType.Length == 0)
        {
            sb.Append("<span data-rtf-image=\"unsupported\"");
            if (wrapStyle != null) sb.Append(" style=\"").Append(wrapStyle).Append('"');
            sb.Append(">[image]</span>");
            return;
        }
        sb.Append("<img src=\"data:").Append(img.MediaType).Append(";base64,");
        sb.Append(Convert.ToBase64String(img.Bytes));
        sb.Append('"');
        if (img.WidthPx > 0)  sb.Append(" width=\"").Append(img.WidthPx.ToString(CultureInfo.InvariantCulture)).Append('"');
        if (img.HeightPx > 0) sb.Append(" height=\"").Append(img.HeightPx.ToString(CultureInfo.InvariantCulture)).Append('"');
        if (wrapStyle != null) sb.Append(" style=\"").Append(wrapStyle).Append('"');
        sb.Append(" alt=\"\">");
    }

    private void EmitHyperlink(StringBuilder sb, RtfHyperlinkRun link)
    {
        if (!IsAllowedUrl(link.Url))
        {
            // Dangerous or disallowed scheme — keep the link text, drop the anchor.
            foreach (var c in link.Children) EmitRun(sb, c);
            return;
        }
        sb.Append("<a href=\"");
        AppendAttributeEscaped(sb, link.Url);
        sb.Append("\">");
        foreach (var child in link.Children) EmitRun(sb, child);
        sb.Append("</a>");
    }

    /// <summary>
    /// URL policy per <see cref="HtmlSanitizationLevel"/>:
    /// None   — everything passes through;
    /// Basic  — block known script-execution schemes (javascript:, vbscript:, data:, ...);
    /// Strict — allow only http, https, mailto, tel, and fragment (#) links.
    /// </summary>
    private bool IsAllowedUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        if (_options.Sanitization == HtmlSanitizationLevel.None) return true;

        string trimmed = url.TrimStart();
        if (trimmed.StartsWith("#", StringComparison.Ordinal)) return true;

        int colon = trimmed.IndexOf(':');
        if (colon < 0)
        {
            // Relative URL — allowed at Basic, blocked at Strict (spec: scheme allowlist only).
            return _options.Sanitization == HtmlSanitizationLevel.Basic;
        }

        string scheme = trimmed.Substring(0, colon).Trim().ToLowerInvariant();
        if (_options.Sanitization == HtmlSanitizationLevel.Strict)
        {
            return scheme == "http" || scheme == "https" || scheme == "mailto" || scheme == "tel";
        }

        // Basic: deny-list of script-capable schemes.
        return scheme != "javascript" && scheme != "vbscript" && scheme != "data" && scheme != "file";
    }

    private void EmitList(StringBuilder sb, RtfList list)
    {
        if (list.Items.Count == 0) return;

        // For nested lists, we need to track the nesting stack
        var nestingStack = new Stack<ListLevelInfo>();
        int currentLevel = -1;

        for (int i = 0; i < list.Items.Count; i++)
        {
            var item = list.Items[i];
            int itemLevel = _options.PreserveListNesting ? item.Level : 0;

            // Close lists deeper than the current level
            while (currentLevel > itemLevel && nestingStack.Count > 0)
            {
                var closingInfo = nestingStack.Pop();
                sb.Append("</").Append(closingInfo.Tag).Append('>');
                currentLevel--;
            }

            // Open new list levels up to the item level
            while (currentLevel < itemLevel)
            {
                currentLevel++;

                // Determine the tag and style for this level
                string tag;
                string? styleAttr = null;
                RtfListLevelDefinition? levelDef = null;

                if (list.Definition != null && currentLevel < list.Definition.Levels.Count)
                {
                    levelDef = list.Definition.Levels[currentLevel];
                    bool isBullet = ListNumberingFormatter.IsBulletFormat(levelDef.NumberFormat);
                    tag = isBullet ? "ul" : "ol";

                    // Get CSS list-style-type; only emit it when it differs from the tag's
                    // browser default (disc for ul, decimal for ol) to keep output clean.
                    var cssStyle = ListNumberingFormatter.GetCssListStyleType(levelDef.NumberFormat);
                    string defaultStyle = isBullet ? "disc" : "decimal";
                    if (!string.IsNullOrEmpty(cssStyle) && cssStyle != defaultStyle)
                    {
                        styleAttr = $"list-style-type:{cssStyle};";
                    }
                }
                else
                {
                    // Fallback: use default bullet/numbering based on level
                    tag = list.Ordered ? "ol" : "ul";
                }

                nestingStack.Push(new ListLevelInfo { Tag = tag, Level = currentLevel, LevelDef = levelDef });

                // Open the list tag
                sb.Append('<').Append(tag);
                if (!string.IsNullOrEmpty(styleAttr))
                {
                    sb.Append(" style=\"").Append(styleAttr).Append('"');
                }

                // Add start attribute for ordered lists with a custom start value.
                if (tag == "ol" && levelDef != null && levelDef.StartAt > 1)
                {
                    sb.Append(" start=\"").Append(levelDef.StartAt.ToString(CultureInfo.InvariantCulture)).Append('"');
                }

                sb.Append('>');
            }

            // Emit the list item
            sb.Append("<li>");

            // For custom numbering formats that CSS doesn't support, prepend the formatted number
            if (nestingStack.Count > 0)
            {
                var currentListInfo = nestingStack.Peek();
                if (currentListInfo.LevelDef != null)
                {
                    var cssStyle = ListNumberingFormatter.GetCssListStyleType(currentListInfo.LevelDef.NumberFormat);

                    // If CSS doesn't support this format (returns null), render custom number
                    if (cssStyle == null && !ListNumberingFormatter.IsBulletFormat(currentListInfo.LevelDef.NumberFormat))
                    {
                        string formattedNum = ListNumberingFormatter.FormatNumber(item.Number, currentListInfo.LevelDef.NumberFormat);
                        if (!string.IsNullOrEmpty(formattedNum))
                        {
                            sb.Append("<span class=\"list-number\">");
                            AppendEscaped(sb, formattedNum);
                            sb.Append(". </span>");
                        }
                    }
                    // Bullets are rendered via the <ul>'s CSS list-style-type (disc/circle/square),
                    // matching how Word and other converters display them. We deliberately do NOT
                    // emit the raw \leveltext glyph, because it is typically an unmappable
                    // Symbol/Wingdings code point plus separator markers that would leak as noise
                    // (e.g. "?;;") in front of every item.
                }
            }

            foreach (var run in item.Runs)
            {
                EmitRun(sb, run);
            }
            sb.Append("</li>");
        }

        // Close all remaining open lists
        while (nestingStack.Count > 0)
        {
            var closingInfo = nestingStack.Pop();
            sb.Append("</").Append(closingInfo.Tag).Append('>');
        }
    }

    private sealed class ListLevelInfo
    {
        public string Tag { get; set; } = "ul";
        public int Level { get; set; }
        public RtfListLevelDefinition? LevelDef { get; set; }
    }

    private void EmitTable(StringBuilder sb, RtfTable table)
    {
        ResolveVerticalMerges(table);

        sb.Append("<table style=\"border-collapse:collapse;\">");

        // Group leading header rows into a <thead>; the remainder (or all rows if none are
        // header rows) into <tbody>.
        int firstNonHeader = 0;
        while (firstNonHeader < table.Rows.Count && table.Rows[firstNonHeader].IsHeader)
            firstNonHeader++;

        if (firstNonHeader > 0)
        {
            sb.Append("<thead>");
            for (int i = 0; i < firstNonHeader; i++) EmitTableRow(sb, table.Rows[i], isHeader: true);
            sb.Append("</thead>");
        }
        if (firstNonHeader < table.Rows.Count)
        {
            sb.Append("<tbody>");
            for (int i = firstNonHeader; i < table.Rows.Count; i++)
                EmitTableRow(sb, table.Rows[i], isHeader: table.Rows[i].IsHeader);
            sb.Append("</tbody>");
        }
        sb.Append("</table>");
    }

    private static void ResolveVerticalMerges(RtfTable table)
    {
        // For each cell that continues a vertical merge (\clvmrg), find the origin cell in
        // an earlier row at the same column (matched by right edge, falling back to index),
        // grow its RowSpan, and mark the continuation cell so it is not rendered.
        for (int r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            for (int c = 0; c < row.Cells.Count; c++)
            {
                var cell = row.Cells[c];
                if (!cell.VerticalMergeContinue || cell.SuppressRender)
                    continue;

                RtfCell? origin = FindMergeOrigin(table, r, c, cell);
                if (origin != null)
                {
                    origin.RowSpan++;
                    cell.SuppressRender = true;
                }
            }
        }
    }

    private static RtfCell? FindMergeOrigin(RtfTable table, int rowIndex, int colIndex, RtfCell cont)
    {
        for (int r = rowIndex - 1; r >= 0; r--)
        {
            var row = table.Rows[r];
            RtfCell? match = null;
            if (cont.RightEdgeTwips > 0)
            {
                foreach (var candidate in row.Cells)
                {
                    if (candidate.RightEdgeTwips == cont.RightEdgeTwips)
                    {
                        match = candidate;
                        break;
                    }
                }
            }
            if (match == null && colIndex < row.Cells.Count)
                match = row.Cells[colIndex];

            if (match == null)
                continue;

            if (match.VerticalMergeContinue && !match.VerticalMergeStart)
                continue; // keep walking up to the true origin
            return match;
        }
        return null;
    }

    private void EmitTableRow(StringBuilder sb, RtfRow row, bool isHeader)
    {
        var rowStyle = new List<string>(4);
        if (row.HeightTwips > 0) rowStyle.Add("height:" + TwipsToPt(row.HeightTwips) + "pt");
        switch (row.Alignment)
        {
            case TextAlignment.Center: rowStyle.Add("text-align:center"); break;
            case TextAlignment.Right:  rowStyle.Add("text-align:right");  break;
        }
        sb.Append("<tr");
        if (rowStyle.Count > 0) sb.Append(" style=\"").Append(string.Join(";", rowStyle)).Append('"');
        sb.Append('>');

        string tag = isHeader ? "th" : "td";
        foreach (var cell in row.Cells)
        {
            if (cell.SuppressRender)
                continue;
            sb.Append('<').Append(tag).Append(" style=\"");
            AppendCellStyle(sb, cell, row);
            sb.Append('"');
            if (cell.ColSpan > 1)
                sb.Append(" colspan=\"").Append(cell.ColSpan.ToString(CultureInfo.InvariantCulture)).Append('"');
            if (cell.RowSpan > 1)
                sb.Append(" rowspan=\"").Append(cell.RowSpan.ToString(CultureInfo.InvariantCulture)).Append('"');
            sb.Append('>');
            foreach (var block in cell.Blocks)
            {
                switch (block)
                {
                    case RtfParagraph p: EmitParagraph(sb, p); break;
                    case RtfList l:      EmitList(sb, l); break;
                }
            }
            sb.Append("</").Append(tag).Append('>');
        }
        sb.Append("</tr>");
    }

    private void AppendCellStyle(StringBuilder sb, RtfCell cell, RtfRow row)
    {
        // Borders — per side if any set, otherwise a subtle default so tables remain visible.
        bool anyBorder = cell.BorderTop.IsVisible || cell.BorderRight.IsVisible
                      || cell.BorderBottom.IsVisible || cell.BorderLeft.IsVisible;
        if (anyBorder)
        {
            AppendBorderSide(sb, "border-top",    cell.BorderTop);
            AppendBorderSide(sb, "border-right",  cell.BorderRight);
            AppendBorderSide(sb, "border-bottom", cell.BorderBottom);
            AppendBorderSide(sb, "border-left",   cell.BorderLeft);
        }
        else
        {
            sb.Append("border:1px solid #ccc;");
        }

        // Padding: prefer row-level padding, fall back to a small default.
        if (row.PaddingLeftTwips   > 0) sb.Append("padding-left:")   .Append(TwipsToPt(row.PaddingLeftTwips))  .Append("pt;");
        if (row.PaddingTopTwips    > 0) sb.Append("padding-top:")    .Append(TwipsToPt(row.PaddingTopTwips))   .Append("pt;");
        if (row.PaddingRightTwips  > 0) sb.Append("padding-right:")  .Append(TwipsToPt(row.PaddingRightTwips)) .Append("pt;");
        if (row.PaddingBottomTwips > 0) sb.Append("padding-bottom:") .Append(TwipsToPt(row.PaddingBottomTwips)).Append("pt;");
        if (row.PaddingLeftTwips == 0 && row.PaddingTopTwips == 0
            && row.PaddingRightTwips == 0 && row.PaddingBottomTwips == 0)
        {
            sb.Append("padding:2px 4px;");
        }

        // Vertical alignment.
        sb.Append("vertical-align:");
        sb.Append(cell.VerticalAlignment switch
        {
            CellVerticalAlignment.Center => "middle",
            CellVerticalAlignment.Bottom => "bottom",
            _ => "top",
        });
        sb.Append(';');

        // Explicit width.
        if (cell.WidthTwips > 0)
            sb.Append("width:").Append(TwipsToPt(cell.WidthTwips)).Append("pt;");

        // Background shading.
        if (cell.BackgroundColorIndex.HasValue)
        {
            var c = _doc.Colors.GetOrAuto(cell.BackgroundColorIndex.Value);
            if (!c.IsAuto) sb.Append("background-color:").Append(ToCssHex(c)).Append(';');
        }
    }

    private void AppendBorderSide(StringBuilder sb, string prop, BorderSide side)
    {
        if (!side.IsVisible) { sb.Append(prop).Append(":none;"); return; }
        // \brdrwN is in twips; a sane minimum is 1px so hairline borders remain visible.
        double px = side.WidthTwips > 0 ? side.WidthTwips / 20.0 * (96.0 / 72.0) : 1.0;
        if (px < 1) px = 1;
        string styleName = side.Style switch
        {
            BorderStyle.Double => "double",
            BorderStyle.Dotted => "dotted",
            BorderStyle.Dashed => "dashed",
            BorderStyle.Hair   => "solid",
            BorderStyle.Thick  => "solid",
            _ => "solid",
        };
        string color = "#000";
        if (side.ColorIndex.HasValue)
        {
            var c = _doc.Colors.GetOrAuto(side.ColorIndex.Value);
            if (!c.IsAuto) color = ToCssHex(c);
        }
        sb.Append(prop).Append(':').Append(px.ToString("0.##", CultureInfo.InvariantCulture))
          .Append("px ").Append(styleName).Append(' ').Append(color).Append(';');
    }

    private void EmitTextRun(StringBuilder sb, RtfTextRun run)
    {
        if (string.IsNullOrEmpty(run.Text)) return;

        var fmt = run.Format;
        string style = BuildRunStyle(fmt);
        var tags = new List<string>();
        if (fmt.Bold)      tags.Add("strong");
        if (fmt.Italic)    tags.Add("em");
        if (fmt.Underline) tags.Add("u");
        if (fmt.Strike)    tags.Add("s");
        if (fmt.VerticalPosition == VerticalPosition.Superscript) tags.Add("sup");
        if (fmt.VerticalPosition == VerticalPosition.Subscript)   tags.Add("sub");

        // lang is set document-wide on <html>; per-span lang is noise for same-language runs.
        bool needSpan = style.Length > 0 || fmt.RightToLeft;

        if (needSpan)
        {
            sb.Append("<span");
            if (fmt.RightToLeft) sb.Append(" dir=\"rtl\"");
            if (style.Length > 0) sb.Append(" style=\"").Append(style).Append('"');
            sb.Append('>');
        }
        foreach (var tag in tags) sb.Append('<').Append(tag).Append('>');

        // Apply ALL CAPS at emit time (text-transform:uppercase covers it via CSS as well,
        // but explicit casing keeps plaintext copies readable).
        string text = run.Text;
        if (fmt.AllCaps) text = text.ToUpperInvariant();
        AppendEscaped(sb, text);

        for (int i = tags.Count - 1; i >= 0; i--) sb.Append("</").Append(tags[i]).Append('>');
        if (needSpan) sb.Append("</span>");
    }

    /// <summary>
    /// Merges consecutive <see cref="RtfTextRun"/> entries that share the same <see cref="CharFormat"/>
    /// into a single run. Non-text runs (images, tabs, line-breaks, …) are passed through unchanged
    /// and break any merge sequence. This significantly reduces span count in typical Word output.
    /// </summary>
    private static IReadOnlyList<RtfRun> MergeRuns(IReadOnlyList<RtfRun> runs)
    {
        if (runs.Count <= 1) return runs;

        var result = new List<RtfRun>(runs.Count);
        RtfTextRun? pending = null;

        foreach (var run in runs)
        {
            if (run is RtfTextRun t)
            {
                if (pending == null)
                {
                    pending = new RtfTextRun { Text = t.Text, Format = t.Format };
                }
                else if (CharFormatsEqual(pending.Format, t.Format))
                {
                    pending = new RtfTextRun { Text = pending.Text + t.Text, Format = pending.Format };
                }
                else
                {
                    result.Add(pending);
                    pending = new RtfTextRun { Text = t.Text, Format = t.Format };
                }
            }
            else
            {
                if (pending != null) { result.Add(pending); pending = null; }
                result.Add(run);
            }
        }
        if (pending != null) result.Add(pending);
        return result;
    }

    private static bool CharFormatsEqual(CharFormat a, CharFormat b) =>
        a.Bold                    == b.Bold
     && a.Italic                  == b.Italic
     && a.Underline               == b.Underline
     && a.Strike                  == b.Strike
     && a.AllCaps                 == b.AllCaps
     && a.SmallCaps               == b.SmallCaps
     && a.Hidden                  == b.Hidden
     && a.RightToLeft             == b.RightToLeft
     && a.VerticalPosition        == b.VerticalPosition
     && a.UnderlineStyle          == b.UnderlineStyle
     && a.FontSizeHalfPoints      == b.FontSizeHalfPoints
     && a.FontIndex               == b.FontIndex
     && a.ForegroundColorIndex    == b.ForegroundColorIndex
     && a.HighlightColorIndex     == b.HighlightColorIndex
     && a.BaselineShiftHalfPoints == b.BaselineShiftHalfPoints;

    private string BuildParagraphStyle(ParagraphFormat f)
    {
        var parts = new List<string>(8);
        // margin: top right bottom left
        if (f.SpaceBeforeTwips != 0) parts.Add("margin-top:" + TwipsToPt(f.SpaceBeforeTwips) + "pt");
        if (f.SpaceAfterTwips  != 0) parts.Add("margin-bottom:" + TwipsToPt(f.SpaceAfterTwips) + "pt");
        if (f.LeftIndentTwips  != 0) parts.Add("margin-left:" + TwipsToPt(f.LeftIndentTwips) + "pt");
        if (f.RightIndentTwips != 0) parts.Add("margin-right:" + TwipsToPt(f.RightIndentTwips) + "pt");
        if (f.FirstLineIndentTwips != 0) parts.Add("text-indent:" + TwipsToPt(f.FirstLineIndentTwips) + "pt");
        switch (f.Alignment)
        {
            case TextAlignment.Center:  parts.Add("text-align:center");  break;
            case TextAlignment.Right:   parts.Add("text-align:right");   break;
            case TextAlignment.Justify: parts.Add("text-align:justify"); break;
        }
        if (f.LineHeightTwips > 0)
        {
            if (f.LineHeightMultiple)
            {
                // \slmult1: 240 twips = 1.0x single spacing.
                double mult = f.LineHeightTwips / 240.0;
                parts.Add("line-height:" + mult.ToString("0.##", CultureInfo.InvariantCulture));
            }
            else
            {
                parts.Add("line-height:" + TwipsToPt(f.LineHeightTwips) + "pt");
            }
        }
        if (f.PageBreakBefore) parts.Add("page-break-before:always");
        if (f.KeepTogether)    parts.Add("page-break-inside:avoid");
        // KeepWithNext (\keepn) is a Word print-layout hint; for web HTML it is noise.

        // Floating / absolute positioning. Any of these signals the paragraph is a
        // free-floating frame anchored via \posx*/\posy*/reference-frame controls.
        bool hasNumericPos = f.PositionXTwips != 0 || f.PositionYTwips != 0;
        bool hasNamedPos   = f.NamedHorizontalPosition != NamedHorizontalPosition.None
                          || f.NamedVerticalPosition   != NamedVerticalPosition.None;
        if (hasNumericPos || hasNamedPos)
        {
            parts.Add("position:absolute");
            if (f.PositionXTwips != 0)
                parts.Add("left:" + TwipsToPt(f.PositionXTwips) + "pt");
            if (f.PositionYTwips != 0)
                parts.Add("top:" + TwipsToPt(f.PositionYTwips) + "pt");
            switch (f.NamedHorizontalPosition)
            {
                case NamedHorizontalPosition.Left:   parts.Add("left:0"); break;
                case NamedHorizontalPosition.Center: parts.Add("left:50%"); parts.Add("transform:translateX(-50%)"); break;
                case NamedHorizontalPosition.Right:  parts.Add("right:0"); break;
            }
            switch (f.NamedVerticalPosition)
            {
                case NamedVerticalPosition.Top:    parts.Add("top:0"); break;
                case NamedVerticalPosition.Center: parts.Add("top:50%"); break;
                case NamedVerticalPosition.Bottom: parts.Add("bottom:0"); break;
            }
        }

        // Paragraph shading background.
        if (f.BackgroundColorIndex.HasValue)
        {
            var c = _doc.Colors.GetOrAuto(f.BackgroundColorIndex.Value);
            if (!c.IsAuto) parts.Add("background-color:" + ToCssHex(c));
        }

        // Paragraph borders (per side; only emit sides that are actually set).
        AppendParaBorder(parts, "border-top",    f.BorderTop);
        AppendParaBorder(parts, "border-right",  f.BorderRight);
        AppendParaBorder(parts, "border-bottom", f.BorderBottom);
        AppendParaBorder(parts, "border-left",   f.BorderLeft);

        return string.Join(";", parts);
    }

    private void AppendParaBorder(List<string> parts, string prop, BorderSide side)
    {
        if (!side.IsVisible) return;
        double px = side.WidthTwips > 0 ? side.WidthTwips / 20.0 * (96.0 / 72.0) : 1.0;
        if (px < 1) px = 1;
        string styleName = side.Style switch
        {
            BorderStyle.Double => "double",
            BorderStyle.Dotted => "dotted",
            BorderStyle.Dashed => "dashed",
            BorderStyle.Hair   => "solid",
            BorderStyle.Thick  => "solid",
            _ => "solid",
        };
        string color = "#000";
        if (side.ColorIndex.HasValue)
        {
            var c = _doc.Colors.GetOrAuto(side.ColorIndex.Value);
            if (!c.IsAuto) color = ToCssHex(c);
        }
        parts.Add(prop + ":" + px.ToString("0.##", CultureInfo.InvariantCulture) + "px " + styleName + " " + color);
    }

    private string BuildRunStyle(CharFormat f)
    {
        var parts = new List<string>(6);

        if (f.FontSizeHalfPoints.HasValue)
        {
            string sizePt = (f.FontSizeHalfPoints.Value / 2.0).ToString("0.##", CultureInfo.InvariantCulture) + "pt";
            if (_bodyFontSizePt == null || sizePt != _bodyFontSizePt)
                parts.Add("font-size:" + sizePt);
        }

        if (f.FontIndex.HasValue && _doc.Fonts.TryGet(f.FontIndex.Value, out var font) && !string.IsNullOrEmpty(font.Name))
        {
            string ff = "'" + EscapeCssString(font.Name) + "'";
            if (_bodyFontFamily == null || ff != _bodyFontFamily)
                parts.Add("font-family:" + ff);
        }

        if (f.ForegroundColorIndex.HasValue)
        {
            var c = _doc.Colors.GetOrAuto(f.ForegroundColorIndex.Value);
            if (!c.IsAuto)
            {
                string hex = ToCssHex(c);
                if (_bodyColorHex == null || hex != _bodyColorHex)
                    parts.Add("color:" + hex);
            }
        }
        if (f.HighlightColorIndex.HasValue)
        {
            var c = _doc.Colors.GetOrAuto(f.HighlightColorIndex.Value);
            if (!c.IsAuto) parts.Add("background-color:" + ToCssHex(c));
        }
        if (f.SmallCaps) parts.Add("font-variant:small-caps");
        // AllCaps is applied as ToUpper on the text; we still emit text-transform for CSS-only
        // consumers copying styled runs. Use uppercase to also protect against downstream text
        // re-casing.
        if (f.AllCaps) parts.Add("text-transform:uppercase");
        if (f.Hidden)  parts.Add("display:none");
        switch (f.UnderlineStyle)
        {
            case UnderlineStyle.Double:
                parts.Add("text-decoration:underline double"); break;
            case UnderlineStyle.Dotted:
                parts.Add("text-decoration:underline dotted"); break;
            case UnderlineStyle.Dashed:
                parts.Add("text-decoration:underline dashed"); break;
            case UnderlineStyle.Wave:
                parts.Add("text-decoration:underline wavy"); break;
            case UnderlineStyle.Thick:
                parts.Add("text-decoration:underline; text-decoration-thickness:2px"); break;
        }
        if (f.BaselineShiftHalfPoints != 0)
        {
            // half-points → pt
            double pt = f.BaselineShiftHalfPoints / 2.0;
            parts.Add("vertical-align:" + pt.ToString("0.##", CultureInfo.InvariantCulture) + "pt");
        }
        return string.Join(";", parts);
    }

    // Very small LCID → BCP-47 mapping table for the languages most likely to appear in RTF.
    // Unmapped LCIDs fall back to null so we don't emit a bogus lang attribute.
    private static string? LcidToBcp47(int? lcid)
    {
        if (!lcid.HasValue || lcid.Value <= 0) return null;
        switch (lcid.Value)
        {
            case 1033: return "en-US";
            case 2057: return "en-GB";
            case 3081: return "en-AU";
            case 4105: return "en-CA";
            case 1036: return "fr-FR";
            case 2060: return "fr-BE";
            case 3084: return "fr-CA";
            case 1031: return "de-DE";
            case 2055: return "de-CH";
            case 1034: return "es-ES";
            case 2058: return "es-MX";
            case 1040: return "it-IT";
            case 1046: return "pt-BR";
            case 2070: return "pt-PT";
            case 1043: return "nl-NL";
            case 1053: return "sv-SE";
            case 1044: return "nb-NO";
            case 1030: return "da-DK";
            case 1035: return "fi-FI";
            case 1045: return "pl-PL";
            case 1029: return "cs-CZ";
            case 1050: return "hr-HR";
            case 1051: return "sk-SK";
            case 1060: return "sl-SI";
            case 1048: return "ro-RO";
            case 1049: return "ru-RU";
            case 1058: return "uk-UA";
            case 1055: return "tr-TR";
            case 1032: return "el-GR";
            case 1037: return "he-IL";
            case 1025: return "ar-SA";
            case 5121: return "ar-DZ";
            case 3073: return "ar-EG";
            case 1041: return "ja-JP";
            case 1042: return "ko-KR";
            case 2052: return "zh-CN";
            case 1028: return "zh-TW";
            case 3076: return "zh-HK";
            case 1054: return "th-TH";
            case 1066: return "vi-VN";
            case 1057: return "id-ID";
            case 1086: return "ms-MY";
            case 1081: return "hi-IN";
            case 1038: return "hu-HU";
            case 1052: return "sq-AL";
            case 1026: return "bg-BG";
            case 1039: return "is-IS";
            case 1061: return "et-EE";
            case 1062: return "lv-LV";
            case 1063: return "lt-LT";
        }
        return null;
    }

    private static string TwipsToPt(int twips) =>
        (twips / 20.0).ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Approximates an RTF tab leader as a CSS baseline border on the spacer span.</summary>
    private static string TabLeaderCss(TabLeader leader)
    {
        switch (leader)
        {
            case TabLeader.Dot:       return "border-bottom:1px dotted";
            case TabLeader.Hyphen:    return "border-bottom:1px dashed";
            case TabLeader.Underline: return "border-bottom:1px solid";
            case TabLeader.Thick:     return "border-bottom:2px solid";
            case TabLeader.Equal:     return "border-bottom:3px double";
            default:                  return "";
        }
    }

    private static string ToCssHex(RtfColor c) =>
        "#" + c.R.ToString("x2", CultureInfo.InvariantCulture)
            + c.G.ToString("x2", CultureInfo.InvariantCulture)
            + c.B.ToString("x2", CultureInfo.InvariantCulture);

    private static string EscapeCssString(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char ch in s)
        {
            if (ch == '\'' || ch == '\\') sb.Append('\\');
            sb.Append(ch);
        }
        return sb.ToString();
    }

    private static void AppendEscaped(StringBuilder sb, string s)
    {
        foreach (char ch in s)
        {
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\u00A0': sb.Append("&nbsp;"); break;
                default:
                    if (ch < 0x20 && ch != '\t')
                    {
                        // control chars — drop.
                    }
                    else sb.Append(ch);
                    break;
            }
        }
    }

    private static void AppendAttributeEscaped(StringBuilder sb, string s)
    {
        foreach (char ch in s)
        {
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '"': sb.Append("&quot;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                default:
                    if (ch < 0x20) { /* drop control */ }
                    else sb.Append(ch);
                    break;
            }
        }
    }
}
