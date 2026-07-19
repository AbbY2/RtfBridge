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

    public HtmlEmitter(RtfDocument doc, RtfToHtmlOptions options)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Emit()
    {
        var sb = new StringBuilder(1024);

        if (_options.EmitFullDocument)
        {
            sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body>");
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

    private void EmitParagraph(StringBuilder sb, RtfParagraph p)
    {
        string style = BuildParagraphStyle(p.Format);
        sb.Append("<p");
        if (p.Format.RightToLeft) sb.Append(" dir=\"rtl\"");
        if (style.Length > 0)
        {
            sb.Append(" style=\"").Append(style).Append('"');
        }
        sb.Append('>');

        if (p.Runs.Count == 0)
        {
            // Preserve empty paragraphs as a visual line.
            sb.Append("<br>");
        }
        else
        {
            foreach (var run in p.Runs)
            {
                EmitRun(sb, run);
            }
        }

        sb.Append("</p>");
    }

    private void EmitRun(StringBuilder sb, RtfRun run)
    {
        switch (run)
        {
            case RtfLineBreak _:
                sb.Append("<br>");
                return;
            case RtfTab _:
                // Preserve tab as an em-space; keeps whitespace deterministic in HTML.
                sb.Append("&emsp;");
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
        sb.Append("<a href=\"");
        AppendAttributeEscaped(sb, link.Url);
        sb.Append("\">");
        foreach (var child in link.Children) EmitRun(sb, child);
        sb.Append("</a>");
    }

    private void EmitList(StringBuilder sb, RtfList list)
    {
        string tag = list.Ordered ? "ol" : "ul";
        int currentLevel = 0;
        sb.Append('<').Append(tag).Append('>');
        foreach (var item in list.Items)
        {
            int lvl = item.Level < 0 ? 0 : item.Level;
            while (currentLevel < lvl)
            {
                sb.Append('<').Append(tag).Append('>');
                currentLevel++;
            }
            while (currentLevel > lvl)
            {
                sb.Append("</").Append(tag).Append('>');
                currentLevel--;
            }
            sb.Append("<li>");
            foreach (var run in item.Runs) EmitRun(sb, run);
            sb.Append("</li>");
        }
        while (currentLevel > 0)
        {
            sb.Append("</").Append(tag).Append('>');
            currentLevel--;
        }
        sb.Append("</").Append(tag).Append('>');
    }

    private void EmitTable(StringBuilder sb, RtfTable table)
    {
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
            sb.Append('<').Append(tag).Append(" style=\"");
            AppendCellStyle(sb, cell, row);
            sb.Append('"');
            if (cell.ColSpan > 1)
                sb.Append(" colspan=\"").Append(cell.ColSpan.ToString(CultureInfo.InvariantCulture)).Append('"');
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

        string? lang = LcidToBcp47(fmt.LanguageId);
        bool needSpan = style.Length > 0 || fmt.RightToLeft || lang != null;

        if (needSpan)
        {
            sb.Append("<span");
            if (fmt.RightToLeft) sb.Append(" dir=\"rtl\"");
            if (lang != null)    sb.Append(" lang=\"").Append(lang).Append('"');
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
        if (f.KeepWithNext)    parts.Add("page-break-after:avoid");

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
            parts.Add("font-size:" + (f.FontSizeHalfPoints.Value / 2.0).ToString("0.##", CultureInfo.InvariantCulture) + "pt");

        if (f.FontIndex.HasValue && _doc.Fonts.TryGet(f.FontIndex.Value, out var font) && !string.IsNullOrEmpty(font.Name))
            parts.Add("font-family:'" + EscapeCssString(font.Name) + "'");

        if (f.ForegroundColorIndex.HasValue)
        {
            var c = _doc.Colors.GetOrAuto(f.ForegroundColorIndex.Value);
            if (!c.IsAuto) parts.Add("color:" + ToCssHex(c));
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
