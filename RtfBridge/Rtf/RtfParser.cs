// ============================================================================
// Product:   RtfBridge
// File:      RtfParser.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using System;
using System.Collections.Generic;
using System.Text;
using RtfBridge.Rtf.Model;

namespace RtfBridge.Rtf;

/// <summary>
/// Parses a token stream from <see cref="RtfTokenizer"/> into an <see cref="RtfDocument"/>.
/// Alpha-1 coverage: character formatting (b/i/ul/strike/super/sub, font/size/color),
/// paragraph formatting (alignment, indent, spacing), Unicode (\uN?, \'hh), tabs, line breaks,
/// font/color tables, common special-character escapes.
/// Higher features (lists, tables, images, fields, headers/footers) are consumed but not yet
/// emitted; they will land in subsequent phases.
/// </summary>
internal sealed class RtfParser
{
    private readonly List<RtfToken> _tokens;
    private readonly int _defaultCodePage;
    private readonly RtfToHtmlOptions _options;
    private readonly List<RtfConversionWarning> _warnings = new List<RtfConversionWarning>();

    private int _index;
    private readonly RtfDocument _doc = new RtfDocument();

    // Current parser state stack
    private readonly Stack<GroupState> _stack = new Stack<GroupState>();
    private GroupState _state;

    // Current paragraph being built
    private RtfParagraph _paragraph = new RtfParagraph();

    // Unicode-escape skip count (\uc). Default is 1.
    private int _unicodeSkipCount = 1;

    // Toggle set by \* when the very next control word starts an optional destination.
    private bool _nextIsOptionalDestination;

    // In-progress table state
    private TableBuildState? _tableState;

    // In-progress hyperlink field state.
    private FieldState? _fieldState;

    // When a \shppict group has been consumed, remember the parent stack depth so we can
    // skip its sibling \nonshppict alternative (LibreOffice/Word emit both; we prefer the
    // modern PNG/JPEG variant carried by \shppict).
    private int? _shppictSeenAtDepth;

    public IReadOnlyList<RtfConversionWarning> Warnings => _warnings;

    public RtfParser(IEnumerable<RtfToken> tokens, int defaultCodePage)
        : this(tokens, defaultCodePage, RtfToHtmlOptions.Default) { }

    public RtfParser(IEnumerable<RtfToken> tokens, int defaultCodePage, RtfToHtmlOptions options)
    {
        _tokens = new List<RtfToken>(tokens);
        _defaultCodePage = defaultCodePage;
        _options = options ?? RtfToHtmlOptions.Default;
        _state = new GroupState();
        _doc.AnsiCodePage = defaultCodePage;
    }

    public RtfDocument Parse()
    {
        _paragraph = new RtfParagraph { Format = _state.ParagraphFormat.Clone() };

        while (_index < _tokens.Count)
        {
            var tok = _tokens[_index++];
            switch (tok.Type)
            {
                case RtfTokenType.GroupStart:
                    PushGroup();
                    break;

                case RtfTokenType.GroupEnd:
                    PopGroup();
                    break;

                case RtfTokenType.ControlWord:
                    HandleControlWord(tok);
                    break;

                case RtfTokenType.ControlSymbol:
                    HandleControlSymbol(tok);
                    break;

                case RtfTokenType.Text:
                    if (!_state.SkipDestination)
                    {
                        AppendText(tok.Value);
                    }
                    break;

                case RtfTokenType.Eof:
                    FlushParagraph(forceEmit: false);
                    CollapseListParagraphs();
                    return _doc;
            }
        }

        FlushParagraph(forceEmit: false);
        CollapseListParagraphs();
        return _doc;
    }

    // ---------------- group state ----------------

    private void PushGroup()
    {
        _stack.Push(_state);
        _state = _state.Clone();
    }

    private void PopGroup()
    {
        if (_stack.Count == 0) return;
        _state = _stack.Pop();
    }

    // ---------------- text emission ----------------

    private void AppendText(string text)
    {
        if (text.Length == 0) return;

        // Drop hidden text (\v) unless the caller explicitly opted in.
        if (_state.CharFormat.Hidden && !_options.RenderHiddenText) return;

        // If the active run uses a Symbol/Wingdings-style font, remap the PUA codepoints
        // (0xF000..0xF0FF) that LibreOffice / Word emit for bullets and dingbats to their
        // real Unicode equivalents. Without this, e.g. Symbol \u61623? (0xF0B7) would leak
        // through as an unmappable character.
        if (IsSymbolLikeActiveFont())
        {
            text = RemapSymbolFontText(text);
        }

        // Merge with the previous run if the character format matches; otherwise create a new run.
        var current = LastTextRunOrNull();
        if (current != null && FormatsEqual(current.Format, _state.CharFormat))
        {
            current.Text += text;
        }
        else
        {
            _paragraph.Runs.Add(new RtfTextRun { Text = text, Format = _state.CharFormat.Clone() });
        }
    }

    private RtfTextRun? LastTextRunOrNull()
    {
        if (_paragraph.Runs.Count == 0) return null;
        return _paragraph.Runs[_paragraph.Runs.Count - 1] as RtfTextRun;
    }

    private static bool FormatsEqual(CharFormat a, CharFormat b) =>
        a.Bold == b.Bold &&
        a.Italic == b.Italic &&
        a.Underline == b.Underline &&
        a.Strike == b.Strike &&
        a.VerticalPosition == b.VerticalPosition &&
        a.FontIndex == b.FontIndex &&
        a.FontSizeHalfPoints == b.FontSizeHalfPoints &&
        a.ForegroundColorIndex == b.ForegroundColorIndex &&
        a.HighlightColorIndex == b.HighlightColorIndex &&
        a.AllCaps == b.AllCaps &&
        a.SmallCaps == b.SmallCaps &&
        a.Hidden == b.Hidden &&
        a.RightToLeft == b.RightToLeft &&
        a.UnderlineStyle == b.UnderlineStyle &&
        a.LanguageId == b.LanguageId &&
        a.BaselineShiftHalfPoints == b.BaselineShiftHalfPoints;

    // Returns true when the currently active font is Symbol / Wingdings / Webdings, which
    // encode glyphs in the Private Use Area (0xF000..0xF0FF).
    private bool IsSymbolLikeActiveFont()
    {
        int fontIdx = _state.CharFormat.FontIndex ?? _doc.DefaultFontIndex;
        var font = _doc.Fonts.Get(fontIdx);
        if (font == null) return false;
        if (font.CharSet == 2) return true; // Symbol charset
        var name = font.Name ?? string.Empty;
        return name.StartsWith("Symbol", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Wingdings", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Webdings", StringComparison.OrdinalIgnoreCase);
    }

    // Maps well-known Symbol/Wingdings PUA codepoints to their Unicode equivalents so bullets
    // and common dingbats render even outside their proprietary fonts. Everything else in the
    // PUA range is passed through unchanged.
    private static string RemapSymbolFontText(string text)
    {
        StringBuilder? sb = null;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            char? mapped = MapSymbolPuaChar(c);
            if (mapped.HasValue)
            {
                if (sb == null) { sb = new StringBuilder(text.Length); sb.Append(text, 0, i); }
                sb.Append(mapped.Value);
            }
            else if (sb != null)
            {
                sb.Append(c);
            }
        }
        return sb == null ? text : sb.ToString();
    }

    private static char? MapSymbolPuaChar(char c)
    {
        // Common Symbol-font PUA codepoints used by LibreOffice/Word for bullets and math.
        switch ((int)c)
        {
            case 0xF0B7: return '\u2022'; // • bullet (Symbol)
            case 0xF0A7: return '\u25AA'; // ▪ black small square (Symbol filled square)
            case 0xF0A8: return '\u25E6'; // ◦ white bullet (Symbol open bullet)
            case 0xF0A6: return '\u2666'; // ♦ diamond
            case 0xF0D8: return '\u2192'; // → right arrow
            case 0xF0DC: return '\u2190'; // ← left arrow
            case 0xF0AE: return '\u2192'; // → right arrow (alt)
            case 0xF0FC: return '\u2713'; // ✓ check mark (Wingdings)
            case 0xF0FE: return '\u2717'; // ✗ ballot X (Wingdings)
            case 0xF0A0: return '\u00A0'; // nbsp
            // Also map the plain ASCII Symbol-font bullet glyph position (0xB7) to • so that
            // bullet code points delivered as \'b7 in a Symbol run render correctly.
            case 0x00B7: return '\u2022';
        }
        return null;
    }

    // ---------------- control words ----------------

    private void HandleControlWord(RtfToken tok)
    {
        string cw = tok.Value;
        int? param = tok.Parameter;

        // Destination-changing / optional-destination handling
        if (_nextIsOptionalDestination)
        {
            _nextIsOptionalDestination = false;
            if (!IsKnownDestination(cw))
            {
                _state.SkipDestination = true;
                return;
            }
        }

        switch (cw)
        {
            // ---------- document / header ----------
            case "rtf": break;
            case "ansi": break;
            case "mac":  _doc.AnsiCodePage = 10000; break;
            case "pc":   _doc.AnsiCodePage = 437;   break;
            case "pca":  _doc.AnsiCodePage = 850;   break;
            case "ansicpg":
                if (param.HasValue) _doc.AnsiCodePage = param.Value;
                break;
            case "deff":
                if (param.HasValue) _doc.DefaultFontIndex = param.Value;
                break;
            case "uc":
                _unicodeSkipCount = param ?? 1;
                _state.UnicodeSkipCount = _unicodeSkipCount;
                break;

            // ---------- destinations we currently skip ----------
            case "fonttbl":
                ParseFontTable();
                break;
            case "colortbl":
                ParseColorTable();
                break;

            // ---------- pictures ----------
            case "pict":
                HandlePict();
                break;
            case "shppict":
                // Modern picture wrapper. Remember this so we can skip a sibling \nonshppict
                // (which carries a redundant legacy WMF/EMF copy of the same image).
                _shppictSeenAtDepth = _stack.Count;
                break;
            case "nonshppict":
                // Legacy fallback. If a sibling \shppict at the same nesting depth was just
                // consumed, skip this whole destination to avoid emitting a duplicate image.
                if (_shppictSeenAtDepth.HasValue && _shppictSeenAtDepth.Value == _stack.Count)
                {
                    _state.SkipDestination = true;
                    _shppictSeenAtDepth = null;
                }
                break;

            // ---------- list table ----------
            case "listtable":
            case "listoverridetable":
                ParseListTable();
                break;

            // ---------- field ----------
            case "field":
                HandleField();
                break;
            case "fldinst":
            case "fldrslt":
                // Should only be reached outside a properly-formed field group; ignore quietly.
                _state.SkipDestination = true;
                break;

            // ---------- list-item association ----------
            case "ls":
                if (param.HasValue) _state.ListId = param.Value;
                break;
            case "ilvl":
                _state.ListLevel = param ?? 0;
                break;
            case "pntext":
            case "pntxta":
            case "pntxtb":
            case "listtext":
                // Auto-generated bullet/number text — skip; we render our own bullets.
                _state.SkipDestination = true;
                break;

            // ---------- table row/cell ----------
            case "trowd":
                BeginTableRow();
                break;
            case "cellx":
                RecordCellBoundary(param ?? 0);
                break;
            case "clmgf":
                MarkCellMergeStart();
                break;
            case "clmrg":
                MarkCellMergeContinuation();
                break;
            case "cell":
                CloseCell();
                break;
            case "row":
            case "nestrow":
                CloseRow();
                break;
            case "intbl":
                _state.InTable = true;
                break;

            // ---------- row-level formatting ----------
            case "trrh":
                if (_tableState != null && param.HasValue)
                {
                    int v = param.Value;
                    _tableState.RowDef.HeightTwips = v < 0 ? -v : v;
                    _tableState.RowDef.HeightExact = v < 0;
                }
                break;
            case "trql": if (_tableState != null) _tableState.RowDef.Alignment = TextAlignment.Left;   break;
            case "trqc": if (_tableState != null) _tableState.RowDef.Alignment = TextAlignment.Center; break;
            case "trqr": if (_tableState != null) _tableState.RowDef.Alignment = TextAlignment.Right;  break;
            case "trhdr": if (_tableState != null) _tableState.RowDef.IsHeader = true; break;
            case "trpaddl": if (_tableState != null && param.HasValue) _tableState.RowDef.PaddingLeftTwips   = param.Value; break;
            case "trpaddt": if (_tableState != null && param.HasValue) _tableState.RowDef.PaddingTopTwips    = param.Value; break;
            case "trpaddr": if (_tableState != null && param.HasValue) _tableState.RowDef.PaddingRightTwips  = param.Value; break;
            case "trpaddb": if (_tableState != null && param.HasValue) _tableState.RowDef.PaddingBottomTwips = param.Value; break;

            // ---------- cell-level formatting (accumulates until next \cellx) ----------
            case "clvertalt": if (_tableState != null) _tableState.PendingCell.VerticalAlignment = CellVerticalAlignment.Top;    break;
            case "clvertalc": if (_tableState != null) _tableState.PendingCell.VerticalAlignment = CellVerticalAlignment.Center; break;
            case "clvertalb": if (_tableState != null) _tableState.PendingCell.VerticalAlignment = CellVerticalAlignment.Bottom; break;
            case "clcbpat":
                if (_tableState != null && param.HasValue) _tableState.PendingCell.BackgroundColorIndex = param.Value;
                break;
            case "clwWidth":
            case "clwwidth":
                if (_tableState != null && param.HasValue) _tableState.PendingCell.WidthTwips = param.Value;
                break;

            // ---------- border-target selectors ----------
            case "clbrdrt": if (_tableState != null) { _tableState.CurrentBorderTarget = BorderTarget.CellTop;    _paraBoxMode = false; } break;
            case "clbrdrr": if (_tableState != null) { _tableState.CurrentBorderTarget = BorderTarget.CellRight;  _paraBoxMode = false; } break;
            case "clbrdrb": if (_tableState != null) { _tableState.CurrentBorderTarget = BorderTarget.CellBottom; _paraBoxMode = false; } break;
            case "clbrdrl": if (_tableState != null) { _tableState.CurrentBorderTarget = BorderTarget.CellLeft;   _paraBoxMode = false; } break;
            case "brdrt":   SetParaBorderTarget(BorderTarget.ParaTop);    _paraBoxMode = false; break;
            case "brdrr":   SetParaBorderTarget(BorderTarget.ParaRight);  _paraBoxMode = false; break;
            case "brdrb":   SetParaBorderTarget(BorderTarget.ParaBottom); _paraBoxMode = false; break;
            case "brdrl":   SetParaBorderTarget(BorderTarget.ParaLeft);   _paraBoxMode = false; break;
            case "brdrbox": // Apply subsequent border style/width/color to ALL four paragraph sides.
                _paraBoxMode = true; break;

            // ---------- border style / width / color (applied to current target) ----------
            case "brdrs":     ApplyBorderStyle(BorderStyle.Single); break;
            case "brdrdb":    ApplyBorderStyle(BorderStyle.Double); break;
            case "brdrdot":   ApplyBorderStyle(BorderStyle.Dotted); break;
            case "brdrdash":  ApplyBorderStyle(BorderStyle.Dashed); break;
            case "brdrhair":  ApplyBorderStyle(BorderStyle.Hair);   break;
            case "brdrth":    ApplyBorderStyle(BorderStyle.Thick);  break;
            case "brdrnone":  ApplyBorderStyle(BorderStyle.None);   break;
            case "brdrw":     if (param.HasValue) ApplyBorderWidth(param.Value); break;
            case "brdrcf":    if (param.HasValue) ApplyBorderColor(param.Value); break;

            // ---------- paragraph shading ----------
            case "cbpat":
                if (param.HasValue) _state.ParagraphFormat.BackgroundColorIndex = param.Value;
                break;

            // ---------- header / footer / footnote / bookmarks ----------
            case "header":
            case "headerl":
            case "headerr":
            case "headerf":
                HandleHeaderFooter(isHeader: true);
                break;
            case "footer":
            case "footerl":
            case "footerr":
            case "footerf":
                HandleHeaderFooter(isHeader: false);
                break;
            case "footnote":
                HandleFootnote();
                break;
            case "bkmkstart":
                HandleBookmarkStart();
                break;
            case "bkmkend":
                _state.SkipDestination = true;
                break;

            case "stylesheet":
            case "info":
            case "revtbl":
            case "rsidtbl":
            case "generator":
            case "xmlnstbl":
            case "themedata":
            case "colorschememapping":
            case "latentstyles":
            case "datastore":
            case "wgrffmtfilter":
                _state.SkipDestination = true;
                break;

            // ---------- drawing shape ----------
            case "shp":
                HandleShape();
                break;

            // ---------- character formatting ----------
            case "plain":
                _state.CharFormat = new CharFormat();
                break;
            case "b":       _state.CharFormat.Bold      = param != 0; break;
            case "i":       _state.CharFormat.Italic    = param != 0; break;
            case "ul":
                _state.CharFormat.Underline = param != 0;
                if (param != 0 && _state.CharFormat.UnderlineStyle == UnderlineStyle.None)
                    _state.CharFormat.UnderlineStyle = UnderlineStyle.Single;
                if (param == 0)
                    _state.CharFormat.UnderlineStyle = UnderlineStyle.None;
                break;
            case "ulnone":
                _state.CharFormat.Underline = false;
                _state.CharFormat.UnderlineStyle = UnderlineStyle.None;
                break;
            case "uld":
            case "uldash":
            case "uldashd":
            case "uldashdd":
                _state.CharFormat.Underline = true;
                _state.CharFormat.UnderlineStyle = UnderlineStyle.Dashed;
                break;
            case "uldb":
                _state.CharFormat.Underline = true;
                _state.CharFormat.UnderlineStyle = UnderlineStyle.Double;
                break;
            case "uldot":
            case "uldotdash":
            case "uldotdotdash":
                _state.CharFormat.Underline = true;
                _state.CharFormat.UnderlineStyle = UnderlineStyle.Dotted;
                break;
            case "ulwave":
            case "ulhwave":
            case "ululdbwave":
                _state.CharFormat.Underline = true;
                _state.CharFormat.UnderlineStyle = UnderlineStyle.Wave;
                break;
            case "ulth":
            case "ulthd":
            case "ulthdash":
                _state.CharFormat.Underline = true;
                _state.CharFormat.UnderlineStyle = UnderlineStyle.Thick;
                break;
            case "ulw":
                // Word-only underline (skip whitespace) — treat as single.
                _state.CharFormat.Underline = true;
                _state.CharFormat.UnderlineStyle = UnderlineStyle.Single;
                break;
            case "caps":    _state.CharFormat.AllCaps   = param != 0; break;
            case "scaps":   _state.CharFormat.SmallCaps = param != 0; break;
            case "v":       _state.CharFormat.Hidden    = param != 0; break;
            case "rtlch":   _state.CharFormat.RightToLeft = true;  break;
            case "ltrch":   _state.CharFormat.RightToLeft = false; break;
            case "lang":
            case "langfe":
            case "langnp":
                if (param.HasValue) _state.CharFormat.LanguageId = param.Value;
                break;
            case "strike":  _state.CharFormat.Strike    = param != 0; break;
            case "super":   _state.CharFormat.VerticalPosition = VerticalPosition.Superscript; break;
            case "sub":     _state.CharFormat.VerticalPosition = VerticalPosition.Subscript;   break;
            case "nosupersub": _state.CharFormat.VerticalPosition = VerticalPosition.Normal;   break;
            case "up":
                // \upN raises baseline by N half-points (default 6).
                _state.CharFormat.BaselineShiftHalfPoints = param ?? 6;
                break;
            case "dn":
                // \dnN lowers baseline by N half-points (default 6).
                _state.CharFormat.BaselineShiftHalfPoints = -(param ?? 6);
                break;
            case "f":
                if (param.HasValue) _state.CharFormat.FontIndex = param.Value;
                break;
            case "fs":
                if (param.HasValue) _state.CharFormat.FontSizeHalfPoints = param.Value;
                break;
            case "cf":
                _state.CharFormat.ForegroundColorIndex = param;
                break;
            case "highlight":
            case "cb":
            case "chcbpat":
                _state.CharFormat.HighlightColorIndex = param;
                break;

            // ---------- paragraph formatting ----------
            case "pard":
                _state.ParagraphFormat = new ParagraphFormat();
                _state.ListId = null;
                _state.ListLevel = 0;
                _state.InTable = false;
                _paraBorderTarget = BorderTarget.None;
                _paraBoxMode = false;
                break;
            case "ql": _state.ParagraphFormat.Alignment = TextAlignment.Left;    break;
            case "qc": _state.ParagraphFormat.Alignment = TextAlignment.Center;  break;
            case "qr": _state.ParagraphFormat.Alignment = TextAlignment.Right;   break;
            case "qj": _state.ParagraphFormat.Alignment = TextAlignment.Justify; break;
            case "li": _state.ParagraphFormat.LeftIndentTwips      = param ?? 0; break;
            case "ri": _state.ParagraphFormat.RightIndentTwips     = param ?? 0; break;
            case "fi": _state.ParagraphFormat.FirstLineIndentTwips = param ?? 0; break;
            case "sb": _state.ParagraphFormat.SpaceBeforeTwips     = param ?? 0; break;
            case "sa": _state.ParagraphFormat.SpaceAfterTwips      = param ?? 0; break;
            case "sl":
                {
                    int v = param ?? 0;
                    if (v < 0) { _state.ParagraphFormat.LineHeightTwips = -v; _state.ParagraphFormat.LineHeightExact = true; }
                    else       { _state.ParagraphFormat.LineHeightTwips = v;  _state.ParagraphFormat.LineHeightExact = false; }
                }
                break;
            case "slmult":
                _state.ParagraphFormat.LineHeightMultiple = (param ?? 0) != 0;
                break;
            case "rtlpar":  _state.ParagraphFormat.RightToLeft = true;  break;
            case "ltrpar":  _state.ParagraphFormat.RightToLeft = false; break;
            case "keep":    _state.ParagraphFormat.KeepTogether = true; break;
            case "keepn":   _state.ParagraphFormat.KeepWithNext = true; break;
            case "pagebb":  _state.ParagraphFormat.PageBreakBefore = true; break;
            case "posx":     if (param.HasValue) _state.ParagraphFormat.PositionXTwips = param.Value; break;
            case "posnegx":  if (param.HasValue) _state.ParagraphFormat.PositionXTwips = -param.Value; break;
            case "posy":     if (param.HasValue) _state.ParagraphFormat.PositionYTwips = param.Value; break;
            case "posnegy":  if (param.HasValue) _state.ParagraphFormat.PositionYTwips = -param.Value; break;
            case "posxl":    _state.ParagraphFormat.NamedHorizontalPosition = NamedHorizontalPosition.Left;   break;
            case "posxc":    _state.ParagraphFormat.NamedHorizontalPosition = NamedHorizontalPosition.Center; break;
            case "posxr":    _state.ParagraphFormat.NamedHorizontalPosition = NamedHorizontalPosition.Right;  break;
            case "posxi":    _state.ParagraphFormat.NamedHorizontalPosition = NamedHorizontalPosition.Left;   break; // inside
            case "posxo":    _state.ParagraphFormat.NamedHorizontalPosition = NamedHorizontalPosition.Right;  break; // outside
            case "posyt":    _state.ParagraphFormat.NamedVerticalPosition = NamedVerticalPosition.Top;    break;
            case "posyc":    _state.ParagraphFormat.NamedVerticalPosition = NamedVerticalPosition.Center; break;
            case "posyb":    _state.ParagraphFormat.NamedVerticalPosition = NamedVerticalPosition.Bottom; break;
            case "posyil":   _state.ParagraphFormat.NamedVerticalPosition = NamedVerticalPosition.Top;    break; // in-line
            case "phpg":     _state.ParagraphFormat.HorizontalReference = PositionReference.Page;   break;
            case "phcol":    _state.ParagraphFormat.HorizontalReference = PositionReference.Column; break;
            case "phmrg":    _state.ParagraphFormat.HorizontalReference = PositionReference.Margin; break;
            case "pvpg":     _state.ParagraphFormat.VerticalReference = PositionReference.Page;      break;
            case "pvpara":   _state.ParagraphFormat.VerticalReference = PositionReference.Paragraph; break;
            case "pvmrg":    _state.ParagraphFormat.VerticalReference = PositionReference.Margin;    break;
            case "widctlpar":
            case "nowidctlpar":
            case "hyphpar":
            case "noline":
                // Recognised but not represented in HTML output.
                break;

            case "par":
                FlushParagraph(forceEmit: true);
                break;

            case "line":
                if (!_state.SkipDestination) _paragraph.Runs.Add(new RtfLineBreak { Format = _state.CharFormat.Clone() });
                break;

            case "tab":
                if (!_state.SkipDestination) _paragraph.Runs.Add(new RtfTab { Format = _state.CharFormat.Clone() });
                break;

            // ---------- unicode ----------
            case "u":
                HandleUnicode(param ?? 0);
                break;

            // ---------- structural ----------
            case "page":
                FlushParagraph(forceEmit: true);
                _doc.Body.Add(new RtfPageBreak());
                break;
            case "sect":
                FlushParagraph(forceEmit: true);
                _doc.Body.Add(new RtfSectionBreak());
                break;

            // ---------- special char control words ----------
            case "emdash":     AppendText("\u2014"); break;
            case "endash":     AppendText("\u2013"); break;
            case "emspace":    AppendText("\u2003"); break;
            case "enspace":    AppendText("\u2002"); break;
            case "qmspace":    AppendText("\u2005"); break;
            case "bullet":     AppendText("\u2022"); break;
            case "lquote":     AppendText("\u2018"); break;
            case "rquote":     AppendText("\u2019"); break;
            case "ldblquote":  AppendText("\u201C"); break;
            case "rdblquote":  AppendText("\u201D"); break;
            case "zwj":        AppendText("\u200D"); break;
            case "zwnj":       AppendText("\u200C"); break;
            case "ltrmark":    AppendText("\u200E"); break;
            case "rtlmark":    AppendText("\u200F"); break;
            case "chdate":
            case "chtime":
            case "chpgn":
            case "chatn":
            case "chftn":
                break;

            default:
                // Unknown control word — no-op. This is safe per the RTF spec.
                break;
        }
    }

    private void HandleControlSymbol(RtfToken tok)
    {
        switch (tok.Value)
        {
            case "*":
                _nextIsOptionalDestination = true;
                break;
            case "\\": AppendText("\\"); break;
            case "{":  AppendText("{");  break;
            case "}":  AppendText("}");  break;
            case "~":  AppendText("\u00A0"); break; // non-breaking space
            case "-":  AppendText("\u00AD"); break; // optional (soft) hyphen
            case "_":  AppendText("\u2011"); break; // non-breaking hyphen
            case "'":
                if (tok.Parameter.HasValue && !_state.SkipDestination)
                {
                    DecodeAnsiByte((byte)tok.Parameter.Value);
                }
                break;
            case "\n":
            case "\r":
                if (!_state.SkipDestination)
                {
                    _paragraph.Runs.Add(new RtfLineBreak { Format = _state.CharFormat.Clone() });
                }
                break;
            default:
                // Ignore unknown control symbols.
                break;
        }
    }

    private void DecodeAnsiByte(byte b)
    {
        try
        {
            var enc = EncodingBootstrap.GetEncoding(_doc.AnsiCodePage);
            string s = enc.GetString(new[] { b });
            AppendText(s);
        }
        catch (Exception ex)
        {
            _warnings.Add(new RtfConversionWarning("ENC_FALLBACK",
                $"Failed to decode byte 0x{b:X2} using codepage {_doc.AnsiCodePage}: {ex.Message}"));
            AppendText(((char)b).ToString());
        }
    }

    private void HandleUnicode(int codePoint)
    {
        if (_state.SkipDestination) { SkipUnicodeFallback(); return; }

        // Negative values represent unsigned via 16-bit wrap-around per spec.
        if (codePoint < 0) codePoint += 0x10000;

        if (codePoint >= 0 && codePoint <= 0xFFFF)
        {
            AppendText(char.ConvertFromUtf32(codePoint));
        }

        SkipUnicodeFallback();
    }

    private void SkipUnicodeFallback()
    {
        int skip = _state.UnicodeSkipCount;
        while (skip > 0 && _index < _tokens.Count)
        {
            var next = _tokens[_index];
            if (next.Type == RtfTokenType.Text)
            {
                if (next.Value.Length > skip)
                {
                    // Consume 'skip' chars from this text token: replace with the remainder.
                    _tokens[_index] = new RtfToken(RtfTokenType.Text, next.Value.Substring(skip), null, next.Position);
                    return;
                }
                skip -= next.Value.Length;
                _index++;
            }
            else if (next.Type == RtfTokenType.ControlSymbol && next.Value == "'")
            {
                _index++; skip--;
            }
            else if (next.Type == RtfTokenType.ControlWord)
            {
                _index++; skip--;
            }
            else
            {
                return;
            }
        }
    }

    // ---------------- font / color tables ----------------

    private void ParseFontTable()
    {
        // We're positioned right after \fonttbl inside its group.
        // Read until matching close of the current group.
        int depth = 1;
        RtfFont? current = null;
        var name = new StringBuilder();

        while (_index < _tokens.Count && depth > 0)
        {
            var t = _tokens[_index++];
            switch (t.Type)
            {
                case RtfTokenType.GroupStart:
                    depth++;
                    if (depth == 2) { current = new RtfFont(); name.Clear(); }
                    break;
                case RtfTokenType.GroupEnd:
                    depth--;
                    if (depth == 1 && current != null)
                    {
                        current.Name = name.ToString().TrimEnd(';').Trim();
                        _doc.Fonts.Set(current.Index, current);
                        current = null;
                    }
                    if (depth == 0) return;
                    break;
                case RtfTokenType.ControlWord:
                    if (current == null) break;
                    switch (t.Value)
                    {
                        case "f":       if (t.Parameter.HasValue) current.Index = t.Parameter.Value; break;
                        case "fcharset": current.CharSet = t.Parameter ?? 0; break;
                        case "fprq":     current.PitchAndFamily = t.Parameter ?? 0; break;
                    }
                    break;
                case RtfTokenType.Text:
                    if (current != null) name.Append(t.Value);
                    break;
            }
        }
    }

    private void ParseColorTable()
    {
        int depth = 1;
        byte r = 0, g = 0, b = 0;
        bool anySet = false;

        while (_index < _tokens.Count && depth > 0)
        {
            var t = _tokens[_index++];
            switch (t.Type)
            {
                case RtfTokenType.GroupStart: depth++; break;
                case RtfTokenType.GroupEnd:
                    depth--;
                    if (depth == 0) return;
                    break;
                case RtfTokenType.ControlWord:
                    switch (t.Value)
                    {
                        case "red":   r = (byte)(t.Parameter ?? 0); anySet = true; break;
                        case "green": g = (byte)(t.Parameter ?? 0); anySet = true; break;
                        case "blue":  b = (byte)(t.Parameter ?? 0); anySet = true; break;
                    }
                    break;
                case RtfTokenType.Text:
                    // Semicolons separate color-table entries.
                    foreach (char ch in t.Value)
                    {
                        if (ch == ';')
                        {
                            _doc.Colors.Add(anySet ? new RtfColor(r, g, b) : RtfColor.Auto);
                            r = g = b = 0; anySet = false;
                        }
                    }
                    break;
            }
        }
    }

    // ---------------- paragraph flush ----------------

    private void FlushParagraph(bool forceEmit)
    {
        // Paragraph formatting (\qc, \li, \sb, ...) applies to the paragraph terminated
        // by \par, so we capture the current paragraph format here rather than at start.
        _paragraph.Format = _state.ParagraphFormat.Clone();
        _paragraph.ListId = _state.ListId;
        _paragraph.ListLevel = _state.ListLevel;

        // Skip empty leading paragraphs entirely.
        if (_paragraph.Runs.Count == 0 && !forceEmit)
        {
            return;
        }

        AddBlock(_paragraph);
        _paragraph = new RtfParagraph { Format = _state.ParagraphFormat.Clone() };
    }

    /// <summary>Route a completed block into either the active table cell or the document body.</summary>
    private void AddBlock(RtfBlock block)
    {
        if (_tableState != null && _tableState.CurrentCellBlocks != null)
        {
            _tableState.CurrentCellBlocks.Add(block);
        }
        else
        {
            _doc.Body.Add(block);
        }
    }

    // ---------- pict ----------
    private void HandlePict()
    {
        // We're inside the {\pict ...} group; PictParser consumes tokens until the group closes.
        // Depth starts at 1 because our own GroupStart already pushed a state.
        var img = PictParser.ReadPictGroup(_tokens, ref _index, depth: 1, _options, _warnings);
        // PictParser consumed the matching }; pop the group state now (parser's main loop won't see it).
        PopGroup();
        if (img == null) return;
        if (_state.SkipDestination) return;

        // Attach to the current paragraph as an inline run.
        img.Format = _state.CharFormat.Clone();
        _paragraph.Runs.Add(img);
    }

    // ---------- shape (\shp / \shpinst) ----------
    // LibreOffice (and Word for wrapped images) embed pictures inside a drawing shape as
    // {\shp{\*\shpinst ... {\sp{\sn pib}{\sv {\pict ...}}} ...}}. We scan the shape group,
    // ignoring every property name/value except a nested \pict, which we extract as a
    // normal inline image. All other shape content is discarded (we don't render vector
    // drawings).
    private void HandleShape()
    {
        // We're inside the {\shp ...} group. Scan tokens until we exit the group.
        // depth == 1 corresponds to being immediately inside \shp.
        int depth = 1;
        ImageWrapMode shapeWrap = ImageWrapMode.Inline;
        while (_index < _tokens.Count && depth > 0)
        {
            var t = _tokens[_index++];
            switch (t.Type)
            {
                case RtfTokenType.GroupStart:
                    depth++;
                    break;
                case RtfTokenType.GroupEnd:
                    depth--;
                    break;
                case RtfTokenType.ControlWord:
                    if (t.Value == "shpwr" && t.Parameter.HasValue)
                    {
                        shapeWrap = t.Parameter.Value switch
                        {
                            1 => ImageWrapMode.TopBottom,
                            2 => ImageWrapMode.Around,
                            3 => ImageWrapMode.None,
                            4 => ImageWrapMode.Tight,
                            5 => ImageWrapMode.Through,
                            _ => ImageWrapMode.Inline,
                        };
                    }
                    else if (t.Value == "pict")
                    {
                        // Push a group state to mirror what HandlePict expects (it pops on exit).
                        PushGroup();
                        var img = PictParser.ReadPictGroup(_tokens, ref _index, depth: 1, _options, _warnings);
                        PopGroup();
                        // PictParser consumed the matching '}' of the \pict group.
                        depth--;
                        if (img != null && !_state.SkipDestination)
                        {
                            img.Format = _state.CharFormat.Clone();
                            img.WrapMode = shapeWrap;
                            _paragraph.Runs.Add(img);
                        }
                    }
                    break;
                // Text / ControlSymbol tokens inside shape property groups are metadata
                // (property names, numeric values, colours). Ignore them.
            }
        }
        // Our main-loop caller expects to still be inside the \shp group state; when this
        // returns, the next token consumed by the caller is the token AFTER the shape group's
        // closing '}'. But our main loop hasn't yet called PopGroup for the \shp group — it
        // will, because the closing '}' is what drove depth to 0 and we consumed it. So we
        // must pop the group state now to keep the stack balanced.
        PopGroup();
    }

    // ---------- list table ----------
    private void ParseListTable()
    {
        // We're positioned inside a \listtable or \listoverridetable group. Read to matching close.
        // \listtable  -> {\list \listid N ... {\listlevel \levelnfc X ...} ...}
        // \listoverridetable -> {\listoverride \listid N \ls M ...}
        int depth = 1;
        bool isOverrideEntry = false;   // true when the current inner group is a \listoverride
        bool isListEntry = false;       // true when the current inner group is a \list
        int? currentListId = null;
        int? currentLs = null;
        bool currentOrdered = false;
        int entryDepth = -1;            // depth at which the current entry started

        while (_index < _tokens.Count && depth > 0)
        {
            var t = _tokens[_index++];
            switch (t.Type)
            {
                case RtfTokenType.GroupStart: depth++; break;
                case RtfTokenType.GroupEnd:
                    depth--;
                    if (depth == entryDepth - 1)
                    {
                        // Closing the current entry.
                        if (isListEntry && currentListId.HasValue)
                            _doc.Lists.SetDefinition(currentListId.Value, new RtfListDefinition { Ordered = currentOrdered });
                        else if (isOverrideEntry && currentListId.HasValue && currentLs.HasValue)
                            _doc.Lists.MapOverride(currentLs.Value, currentListId.Value);

                        isListEntry = false;
                        isOverrideEntry = false;
                        currentListId = null;
                        currentLs = null;
                        currentOrdered = false;
                        entryDepth = -1;
                    }
                    if (depth == 0) return;
                    break;
                case RtfTokenType.ControlWord:
                    switch (t.Value)
                    {
                        case "list":
                            isListEntry = true;
                            entryDepth = depth;
                            break;
                        case "listoverride":
                            isOverrideEntry = true;
                            entryDepth = depth;
                            break;
                        case "listid":
                            if ((isListEntry || isOverrideEntry) && t.Parameter.HasValue)
                                currentListId = t.Parameter.Value;
                            break;
                        case "ls":
                            if (isOverrideEntry && t.Parameter.HasValue)
                                currentLs = t.Parameter.Value;
                            break;
                        case "levelnfc":
                        case "levelnfcn":
                            // 23 = bullet, anything else = numeric-ish.
                            if (isListEntry && t.Parameter.HasValue && t.Parameter.Value != 23)
                                currentOrdered = true;
                            break;
                    }
                    break;
            }
        }
    }

    // ---------- field / hyperlink ----------
    private void HandleField()
    {
        // We're inside a {\field ...} group. Structure:
        //   {\field {\*\fldinst FIELDTYPE args...} {\fldrslt <runs>}}
        _fieldState = new FieldState();

        int depth = 1;
        while (_index < _tokens.Count && depth > 0)
        {
            var t = _tokens[_index++];
            switch (t.Type)
            {
                case RtfTokenType.GroupStart: depth++; break;
                case RtfTokenType.GroupEnd:
                    depth--;
                    if (depth == 0)
                    {
                        EmitCompletedField();
                        _fieldState = null;
                        return;
                    }
                    break;
                case RtfTokenType.ControlWord:
                    if (t.Value == "fldinst")
                    {
                        ParseFldInst(ref depth);
                    }
                    else if (t.Value == "fldrslt")
                    {
                        ReadFldRslt(ref depth);
                    }
                    break;
            }
        }
    }

    private void EmitCompletedField()
    {
        var fs = _fieldState!;

        switch (fs.Kind)
        {
            case FieldKind.Hyperlink:
                if (fs.Argument.Length > 0 && fs.ResultRuns.Count > 0)
                {
                    var link = new RtfHyperlinkRun { Url = fs.Argument };
                    link.Children.AddRange(fs.ResultRuns);
                    _paragraph.Runs.Add(link);
                }
                else
                {
                    _paragraph.Runs.AddRange(fs.ResultRuns);
                }
                break;

            case FieldKind.PageRef:
            case FieldKind.Ref:
                if (fs.Argument.Length > 0)
                {
                    var link = new RtfHyperlinkRun { Url = "#" + fs.Argument };
                    if (fs.ResultRuns.Count > 0)
                        link.Children.AddRange(fs.ResultRuns);
                    else
                        link.Children.Add(new RtfTextRun { Text = fs.Argument, Format = _state.CharFormat.Clone() });
                    _paragraph.Runs.Add(link);
                }
                else
                {
                    _paragraph.Runs.AddRange(fs.ResultRuns);
                }
                break;

            case FieldKind.Date:
            case FieldKind.Time:
            case FieldKind.Page:
            case FieldKind.NumPages:
                if (fs.ResultRuns.Count > 0)
                {
                    _paragraph.Runs.AddRange(fs.ResultRuns);
                }
                else
                {
                    _paragraph.Runs.Add(new RtfTextRun
                    {
                        Text = "[" + fs.Kind.ToString().ToUpperInvariant() + "]",
                        Format = _state.CharFormat.Clone(),
                    });
                }
                break;

            default:
                // Unknown/other field: emit fldrslt as plain runs.
                _paragraph.Runs.AddRange(fs.ResultRuns);
                break;
        }
    }

    private void ParseFldInst(ref int depth)
    {
        // Consume the fldinst destination content up to its group close.
        var sb = new StringBuilder();
        int startDepth = depth;
        while (_index < _tokens.Count && depth >= startDepth)
        {
            var t = _tokens[_index++];
            switch (t.Type)
            {
                case RtfTokenType.GroupStart: depth++; break;
                case RtfTokenType.GroupEnd:
                    depth--;
                    if (depth < startDepth) goto Done;
                    break;
                case RtfTokenType.Text: sb.Append(t.Value); break;
            }
        }
    Done:
        string inst = sb.ToString().Trim();
        if (inst.Length == 0) return;

        // Extract first whitespace-separated token as the field type.
        int space = inst.IndexOfAny(new[] { ' ', '\t' });
        string typeToken = space > 0 ? inst.Substring(0, space) : inst;
        string args = space > 0 ? inst.Substring(space + 1).TrimStart() : "";

        var fs = _fieldState!;
        fs.Kind = ClassifyField(typeToken);
        fs.Argument = ExtractFirstArgument(args);
    }

    private static FieldKind ClassifyField(string typeToken)
    {
        if (typeToken.Equals("HYPERLINK", StringComparison.OrdinalIgnoreCase)) return FieldKind.Hyperlink;
        if (typeToken.Equals("PAGEREF",   StringComparison.OrdinalIgnoreCase)) return FieldKind.PageRef;
        if (typeToken.Equals("REF",       StringComparison.OrdinalIgnoreCase)) return FieldKind.Ref;
        if (typeToken.Equals("DATE",      StringComparison.OrdinalIgnoreCase) ||
            typeToken.Equals("CREATEDATE",StringComparison.OrdinalIgnoreCase) ||
            typeToken.Equals("SAVEDATE",  StringComparison.OrdinalIgnoreCase) ||
            typeToken.Equals("PRINTDATE", StringComparison.OrdinalIgnoreCase)) return FieldKind.Date;
        if (typeToken.Equals("TIME",      StringComparison.OrdinalIgnoreCase)) return FieldKind.Time;
        if (typeToken.Equals("PAGE",      StringComparison.OrdinalIgnoreCase)) return FieldKind.Page;
        if (typeToken.Equals("NUMPAGES",  StringComparison.OrdinalIgnoreCase)) return FieldKind.NumPages;
        return FieldKind.Unknown;
    }

    private static string ExtractFirstArgument(string args)
    {
        if (args.Length == 0) return "";
        if (args[0] == '"')
        {
            int end = args.IndexOf('"', 1);
            if (end > 1) return args.Substring(1, end - 1);
            return args.Substring(1);
        }
        int space = args.IndexOfAny(new[] { ' ', '\t' });
        return space > 0 ? args.Substring(0, space) : args;
    }

    private void ReadFldRslt(ref int depth)
    {
        // Parse the fldrslt content as regular runs into a temporary paragraph.
        int startDepth = depth;
        var savedParagraph = _paragraph;
        _paragraph = new RtfParagraph { Format = _state.ParagraphFormat.Clone() };

        while (_index < _tokens.Count && depth >= startDepth)
        {
            var t = _tokens[_index++];
            switch (t.Type)
            {
                case RtfTokenType.GroupStart: PushGroup(); depth++; break;
                case RtfTokenType.GroupEnd:
                    PopGroup(); depth--;
                    if (depth < startDepth)
                    {
                        _fieldState!.ResultRuns.AddRange(_paragraph.Runs);
                        _paragraph = savedParagraph;
                        return;
                    }
                    break;
                case RtfTokenType.ControlWord: HandleControlWord(t); break;
                case RtfTokenType.ControlSymbol: HandleControlSymbol(t); break;
                case RtfTokenType.Text:
                    if (!_state.SkipDestination) AppendText(t.Value);
                    break;
            }
        }

        _fieldState!.ResultRuns.AddRange(_paragraph.Runs);
        _paragraph = savedParagraph;
    }

    // ---------- header / footer / footnote / bookmarks ----------

    /// <summary>
    /// Reads runs from an inner destination group (we are positioned inside the group; depth starts at 1)
    /// into a target run list. The matching close-brace is consumed and PopGroup is called.
    /// </summary>
    private void ReadRunsIntoDestination(List<RtfRun> target)
    {
        int depth = 1;
        var savedParagraph = _paragraph;
        _paragraph = new RtfParagraph { Format = _state.ParagraphFormat.Clone() };
        var oldSkip = _state.SkipDestination;
        _state.SkipDestination = false;

        while (_index < _tokens.Count && depth > 0)
        {
            var t = _tokens[_index++];
            switch (t.Type)
            {
                case RtfTokenType.GroupStart: PushGroup(); depth++; break;
                case RtfTokenType.GroupEnd:
                    depth--;
                    if (depth == 0)
                    {
                        target.AddRange(_paragraph.Runs);
                        _paragraph = savedParagraph;
                        _state.SkipDestination = oldSkip;
                        PopGroup(); // pop the state that our caller's GroupStart pushed
                        return;
                    }
                    PopGroup();
                    break;
                case RtfTokenType.ControlWord: HandleControlWord(t); break;
                case RtfTokenType.ControlSymbol: HandleControlSymbol(t); break;
                case RtfTokenType.Text:
                    if (!_state.SkipDestination) AppendText(t.Value);
                    break;
            }
        }

        target.AddRange(_paragraph.Runs);
        _paragraph = savedParagraph;
        _state.SkipDestination = oldSkip;
    }

    private void HandleHeaderFooter(bool isHeader)
    {
        if (_state.SkipDestination) return;
        // Capture only the first occurrence; subsequent ones are skipped.
        var target = isHeader ? _doc.Header : _doc.Footer;
        if (target.Count > 0)
        {
            _state.SkipDestination = true;
            return;
        }
        ReadRunsIntoDestination(target);
    }

    private void HandleFootnote()
    {
        if (_state.SkipDestination) return;
        var note = new RtfFootnote { Number = _doc.Footnotes.Count + 1 };
        _doc.Footnotes.Add(note);
        // Emit an inline reference where the footnote appears.
        _paragraph.Runs.Add(new RtfFootnoteRef
        {
            Number = note.Number,
            Format = _state.CharFormat.Clone(),
        });
        ReadRunsIntoDestination(note.Runs);
    }

    private void HandleBookmarkStart()
    {
        // {\*\bkmkstart NAME} — capture the bookmark name as inline text, emit anchor run, skip destination text.
        int depth = 1;
        var sb = new StringBuilder();
        while (_index < _tokens.Count && depth > 0)
        {
            var t = _tokens[_index++];
            switch (t.Type)
            {
                case RtfTokenType.GroupStart: depth++; break;
                case RtfTokenType.GroupEnd:
                    depth--;
                    if (depth == 0)
                    {
                        PopGroup();
                        var name = sb.ToString().Trim();
                        if (name.Length > 0)
                        {
                            _paragraph.Runs.Add(new RtfBookmarkAnchor
                            {
                                Name = name,
                                Format = _state.CharFormat.Clone(),
                            });
                        }
                        return;
                    }
                    break;
                case RtfTokenType.Text: sb.Append(t.Value); break;
            }
        }
    }

    // ---------- tables ----------
    private void SetParaBorderTarget(BorderTarget t)
    {
        if (_tableState != null) _tableState.CurrentBorderTarget = t;
        else _paraBorderTarget = t;
    }

    private BorderSide? CurrentBorderSide()
    {
        var t = _tableState != null ? _tableState.CurrentBorderTarget : _paraBorderTarget;
        return t switch
        {
            BorderTarget.CellTop    => _tableState?.PendingCell.BorderTop,
            BorderTarget.CellRight  => _tableState?.PendingCell.BorderRight,
            BorderTarget.CellBottom => _tableState?.PendingCell.BorderBottom,
            BorderTarget.CellLeft   => _tableState?.PendingCell.BorderLeft,
            BorderTarget.ParaTop    => _state.ParagraphFormat.BorderTop,
            BorderTarget.ParaRight  => _state.ParagraphFormat.BorderRight,
            BorderTarget.ParaBottom => _state.ParagraphFormat.BorderBottom,
            BorderTarget.ParaLeft   => _state.ParagraphFormat.BorderLeft,
            _ => null,
        };
    }

    private IEnumerable<BorderSide> AllTargetedSides()
    {
        if (_paraBoxMode)
        {
            yield return _state.ParagraphFormat.BorderTop;
            yield return _state.ParagraphFormat.BorderRight;
            yield return _state.ParagraphFormat.BorderBottom;
            yield return _state.ParagraphFormat.BorderLeft;
            yield break;
        }
        var one = CurrentBorderSide();
        if (one != null) yield return one;
    }

    private void ApplyBorderStyle(BorderStyle s)
    {
        foreach (var side in AllTargetedSides()) side.Style = s;
    }

    private void ApplyBorderWidth(int twips)
    {
        foreach (var side in AllTargetedSides()) side.WidthTwips = twips;
    }

    private void ApplyBorderColor(int colorIndex)
    {
        foreach (var side in AllTargetedSides()) side.ColorIndex = colorIndex;
    }

    private BorderTarget _paraBorderTarget = BorderTarget.None;
    private bool _paraBoxMode;

    private void BeginTableRow()
    {
        if (_tableState == null)
        {
            _tableState = new TableBuildState();
        }
        _tableState.CurrentRowBoundaries.Clear();
        _tableState.PendingMergeStart = false;
        _tableState.PendingMergeContinue = false;
        // Prepare buffer for first cell's blocks.
        _tableState.CurrentCellBlocks = new List<RtfBlock>();
        _tableState.CurrentRowCells.Clear();
        _tableState.CurrentRowCellStates.Clear();
        _tableState.PendingCell = new CellDefState();
        _tableState.RowDef = new RowDefState();
        _tableState.CurrentBorderTarget = BorderTarget.None;
    }

    private void RecordCellBoundary(int rightTwips)
    {
        if (_tableState == null) return;
        _tableState.CurrentRowBoundaries.Add(rightTwips);

        // Snapshot the pending cell format (accumulated \cl* control words) and attach
        // the boundary + merge flags. Then reset the scratch for the next cell.
        var snap = _tableState.PendingCell.Snapshot();
        snap.RightEdgeTwips = rightTwips;
        snap.MergeStart = _tableState.PendingMergeStart;
        snap.MergeContinue = _tableState.PendingMergeContinue;
        _tableState.CurrentRowCellStates.Add(snap);

        _tableState.PendingMergeStart = false;
        _tableState.PendingMergeContinue = false;
        _tableState.PendingCell = new CellDefState();
        _tableState.CurrentBorderTarget = BorderTarget.None;
    }

    private void MarkCellMergeStart() { if (_tableState != null) _tableState.PendingMergeStart = true; }
    private void MarkCellMergeContinuation() { if (_tableState != null) _tableState.PendingMergeContinue = true; }

    private void CloseCell()
    {
        if (_tableState == null) return;

        // Flush any pending paragraph runs into the cell buffer.
        if (_paragraph.Runs.Count > 0)
        {
            _paragraph.Format = _state.ParagraphFormat.Clone();
            _tableState.CurrentCellBlocks!.Add(_paragraph);
            _paragraph = new RtfParagraph { Format = _state.ParagraphFormat.Clone() };
        }

        _tableState.CurrentRowCells.Add(_tableState.CurrentCellBlocks!);
        _tableState.CurrentCellBlocks = new List<RtfBlock>();
    }

    private void CloseRow()
    {
        if (_tableState == null) return;

        var rd = _tableState.RowDef;
        var row = new RtfRow
        {
            HeightTwips = rd.HeightTwips,
            HeightExact = rd.HeightExact,
            Alignment = rd.Alignment,
            IsHeader = rd.IsHeader,
            PaddingLeftTwips = rd.PaddingLeftTwips,
            PaddingTopTwips = rd.PaddingTopTwips,
            PaddingRightTwips = rd.PaddingRightTwips,
            PaddingBottomTwips = rd.PaddingBottomTwips,
        };
        int cellIndex = 0;
        int pendingSpan = 1;
        for (int i = 0; i < _tableState.CurrentRowCells.Count; i++, cellIndex++)
        {
            var blocks = _tableState.CurrentRowCells[i];
            var def = cellIndex < _tableState.CurrentRowCellStates.Count
                ? _tableState.CurrentRowCellStates[cellIndex]
                : new CellDefState();

            if (def.MergeContinue)
            {
                // This cell merges into the previous cell — increase its colspan and drop content.
                if (row.Cells.Count > 0)
                    row.Cells[row.Cells.Count - 1].ColSpan++;
                continue;
            }

            var cell = new RtfCell
            {
                RightEdgeTwips = def.RightEdgeTwips,
                ColSpan = pendingSpan,
                WidthTwips = def.WidthTwips,
                VerticalAlignment = def.VerticalAlignment,
                BackgroundColorIndex = def.BackgroundColorIndex,
                BorderTop = def.BorderTop.Clone(),
                BorderRight = def.BorderRight.Clone(),
                BorderBottom = def.BorderBottom.Clone(),
                BorderLeft = def.BorderLeft.Clone(),
            };
            cell.Blocks.AddRange(blocks);
            row.Cells.Add(cell);
        }

        // Attach row to a table. If the previous body block is a table with matching column count-ish, append.
        RtfTable? table = null;
        if (_doc.Body.Count > 0 && _doc.Body[_doc.Body.Count - 1] is RtfTable lastTable)
        {
            table = lastTable;
        }
        if (table == null)
        {
            table = new RtfTable();
            _doc.Body.Add(table);
        }
        table.Rows.Add(row);

        // Reset for next row.
        _tableState.CurrentRowCells.Clear();
        _tableState.CurrentRowCellStates.Clear();
        _tableState.CurrentRowBoundaries.Clear();
        _tableState.CurrentCellBlocks = null;
        _tableState.PendingCell = new CellDefState();
        _tableState.RowDef = new RowDefState();
        _tableState.CurrentBorderTarget = BorderTarget.None;
        _state.InTable = false;
    }

    private sealed class TableBuildState
    {
        public List<int> CurrentRowBoundaries { get; } = new List<int>();
        public List<List<RtfBlock>> CurrentRowCells { get; } = new List<List<RtfBlock>>();
        public List<CellDefState> CurrentRowCellStates { get; } = new List<CellDefState>();
        public List<RtfBlock>? CurrentCellBlocks { get; set; }
        public bool PendingMergeStart { get; set; }
        public bool PendingMergeContinue { get; set; }

        /// <summary>Scratch cell format being accumulated for the next \cellx.</summary>
        public CellDefState PendingCell { get; set; } = new CellDefState();

        /// <summary>Row-level state accumulated between \trowd and \row.</summary>
        public RowDefState RowDef { get; set; } = new RowDefState();

        /// <summary>Which entity/side the current \brdr* control words are targeting.</summary>
        public BorderTarget CurrentBorderTarget { get; set; } = BorderTarget.None;
    }

    private sealed class CellDefState
    {
        public int RightEdgeTwips { get; set; }
        public int WidthTwips { get; set; }
        public bool MergeStart { get; set; }
        public bool MergeContinue { get; set; }
        public CellVerticalAlignment VerticalAlignment { get; set; } = CellVerticalAlignment.Top;
        public int? BackgroundColorIndex { get; set; }
        public BorderSide BorderTop    { get; set; } = new BorderSide();
        public BorderSide BorderRight  { get; set; } = new BorderSide();
        public BorderSide BorderBottom { get; set; } = new BorderSide();
        public BorderSide BorderLeft   { get; set; } = new BorderSide();

        public CellDefState Snapshot() => new CellDefState
        {
            RightEdgeTwips = RightEdgeTwips,
            WidthTwips = WidthTwips,
            MergeStart = MergeStart,
            MergeContinue = MergeContinue,
            VerticalAlignment = VerticalAlignment,
            BackgroundColorIndex = BackgroundColorIndex,
            BorderTop = BorderTop.Clone(),
            BorderRight = BorderRight.Clone(),
            BorderBottom = BorderBottom.Clone(),
            BorderLeft = BorderLeft.Clone(),
        };
    }

    private sealed class RowDefState
    {
        public int HeightTwips { get; set; }
        public bool HeightExact { get; set; }
        public TextAlignment Alignment { get; set; } = TextAlignment.Left;
        public bool IsHeader { get; set; }
        public int PaddingLeftTwips { get; set; }
        public int PaddingTopTwips { get; set; }
        public int PaddingRightTwips { get; set; }
        public int PaddingBottomTwips { get; set; }
    }

    /// <summary>Identifies the border side that upcoming \brdr* control words modify.</summary>
    private enum BorderTarget
    {
        None,
        CellTop, CellRight, CellBottom, CellLeft,
        ParaTop, ParaRight, ParaBottom, ParaLeft,
    }

    private enum FieldKind
    {
        Unknown,
        Hyperlink,
        PageRef,
        Ref,
        Date,
        Time,
        Page,
        NumPages,
    }

    private sealed class FieldState
    {
        public FieldKind Kind { get; set; } = FieldKind.Unknown;
        public string Argument { get; set; } = "";
        public List<RtfRun> ResultRuns { get; } = new List<RtfRun>();
    }

    // ---------- post-processing: group list-flagged paragraphs into RtfList blocks ----------
    private void CollapseListParagraphs()
    {
        var body = _doc.Body;
        for (int i = 0; i < body.Count; i++)
        {
            if (body[i] is RtfParagraph p && p.ListId.HasValue)
            {
                int listId = p.ListId.Value;
                bool ordered = _doc.Lists.TryGet(listId, out var def) && def.Ordered;
                var list = new RtfList { Ordered = ordered };

                int j = i;
                while (j < body.Count && body[j] is RtfParagraph pp && pp.ListId == listId)
                {
                    var item = new RtfListItem { Level = pp.ListLevel, Format = pp.Format };
                    item.Runs.AddRange(pp.Runs);
                    list.Items.Add(item);
                    j++;
                }

                body.RemoveRange(i, j - i);
                body.Insert(i, list);
            }
        }
    }

    private static bool IsKnownDestination(string cw) =>
        cw == "fonttbl" || cw == "colortbl" || cw == "stylesheet" ||
        cw == "info" || cw == "listtable" || cw == "listoverridetable" ||
        cw == "generator" || cw == "pict" || cw == "field" ||
        cw == "fldinst" || cw == "fldrslt" || cw == "footnote" ||
        cw == "bkmkstart" || cw == "bkmkend" || cw == "header" ||
        cw == "footer" || cw == "revtbl" || cw == "rsidtbl" ||
        cw == "themedata" || cw == "datastore" || cw == "xmlnstbl" ||
        cw == "latentstyles" || cw == "wgrffmtfilter" ||
        cw == "colorschememapping" || cw == "shppict" || cw == "nonshppict" ||
        cw == "shp" || cw == "shpinst" ||
        cw == "pntext" || cw == "pntxta" || cw == "pntxtb" || cw == "listtext";

    // ---------------- nested state ----------------

    private sealed class GroupState
    {
        public CharFormat CharFormat { get; set; } = new CharFormat();
        public ParagraphFormat ParagraphFormat { get; set; } = new ParagraphFormat();
        public bool SkipDestination { get; set; }
        public int UnicodeSkipCount { get; set; } = 1;
        public int? ListId { get; set; }
        public int ListLevel { get; set; }
        public bool InTable { get; set; }

        public GroupState Clone() => new GroupState
        {
            CharFormat = CharFormat.Clone(),
            ParagraphFormat = ParagraphFormat.Clone(),
            SkipDestination = SkipDestination,
            UnicodeSkipCount = UnicodeSkipCount,
            ListId = ListId,
            ListLevel = ListLevel,
            InTable = InTable,
        };
    }
}
