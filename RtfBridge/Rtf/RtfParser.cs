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
using System.Linq;
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

    // When ReadRunsIntoDestination is active, AddBlock routes here instead of _doc.Body
    // so that \par flushes inside headers/footers/footnotes don't pollute the body.
    private List<RtfBlock>? _destinationSink;

    // When a \shppict group has been consumed, remember the parent stack depth so we can
    // skip its sibling \nonshppict alternative (LibreOffice/Word emit both; we prefer the
    // modern PNG/JPEG variant carried by \shppict).
    private int? _shppictSeenAtDepth;

    // Bytes accumulated from raw text and \'hh escapes, decoded together on flush so that
    // double-byte codepages (932/936/949/950) and font-charset overrides work correctly.
    private readonly List<byte> _pendingBytes = new List<byte>();

    // High surrogate from a preceding \uN awaiting its low half (Word emits emoji as
    // \uHI?\uLO? UTF-16 pairs).
    private char? _pendingHighSurrogate;

    // Style index -> outline level (0-based) discovered in the stylesheet ("heading N" names
    // or explicit \outlinelevel inside the style definition).
    private readonly Dictionary<int, int> _styleOutlineLevels = new Dictionary<int, int>();

    // Style index -> captured formatting deltas from the \stylesheet, applied when a
    // paragraph references the style via \sN.
    private readonly Dictionary<int, RtfStyleDefinition> _styles = new Dictionary<int, RtfStyleDefinition>();

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
                    AppendTextBytes(tok.Value);
                    break;

                case RtfTokenType.Eof:
                    FlushPendingBytes();
                    FlushParagraph(forceEmit: false);
                    CollapseListParagraphs();
                    return _doc;
            }
        }

        FlushPendingBytes();
        FlushParagraph(forceEmit: false);
        CollapseListParagraphs();
        return _doc;
    }

    // ---------------- group state ----------------

    private void PushGroup()
    {
        FlushPendingBytes();
        _stack.Push(_state);
        _state = _state.Clone();
    }

    private void PopGroup()
    {
        FlushPendingBytes();
        if (_stack.Count == 0) return;
        _state = _stack.Pop();
    }

    // ---------------- text emission ----------------

    /// <summary>
    /// Queues raw text bytes (tokenizer text is a 1:1 byte-to-char mapping) for codepage
    /// decoding. Bytes are flushed together so multi-byte (DBCS/CJK) sequences decode intact.
    /// </summary>
    private void AppendTextBytes(string latin1)
    {
        if (_state.SkipDestination || latin1.Length == 0) return;
        for (int i = 0; i < latin1.Length; i++)
        {
            _pendingBytes.Add((byte)latin1[i]);
        }
    }

    /// <summary>Decodes all queued bytes with the active codepage and emits them as text.</summary>
    private void FlushPendingBytes()
    {
        if (_pendingBytes.Count == 0) return;
        byte[] raw = _pendingBytes.ToArray();
        _pendingBytes.Clear();

        string decoded;
        try
        {
            decoded = EncodingBootstrap.GetEncoding(ActiveCodePage()).GetString(raw);
        }
        catch (Exception ex)
        {
            _warnings.Add(new RtfConversionWarning("ENC_FALLBACK",
                $"Failed to decode text using codepage {ActiveCodePage()}: {ex.Message}"));
            var sb = new StringBuilder(raw.Length);
            foreach (byte b in raw) sb.Append((char)b);
            decoded = sb.ToString();
        }
        AppendText(decoded);
    }

    /// <summary>
    /// Codepage used to decode text bytes: the active font's charset codepage when one is
    /// declared (\fcharset), otherwise the document ANSI codepage.
    /// </summary>
    private int ActiveCodePage()
    {
        int fontIdx = _state.CharFormat.FontIndex ?? _doc.DefaultFontIndex;
        var font = _doc.Fonts.Get(fontIdx);
        if (font != null)
        {
            int? cp = CharsetToCodePage(font.CharSet);
            if (cp.HasValue) return cp.Value;
        }
        return _doc.AnsiCodePage;
    }

    // Windows charset byte (\fcharsetN) -> codepage. Null = use document default
    // (ANSI_CHARSET / DEFAULT_CHARSET / SYMBOL_CHARSET fall back to the document codepage).
    private static int? CharsetToCodePage(int charset) => charset switch
    {
        77  => 10000, // Mac Roman
        128 => 932,   // Shift-JIS
        129 => 949,   // Korean (Hangul)
        130 => 1361,  // Korean (Johab)
        134 => 936,   // Simplified Chinese (GBK)
        136 => 950,   // Traditional Chinese (Big5)
        161 => 1253,  // Greek
        162 => 1254,  // Turkish
        163 => 1258,  // Vietnamese
        177 => 1255,  // Hebrew
        178 => 1256,  // Arabic
        186 => 1257,  // Baltic
        204 => 1251,  // Cyrillic
        222 => 874,   // Thai
        238 => 1250,  // Central European
        254 => 437,   // OEM US
        255 => 850,   // OEM Latin-1
        _   => null,
    };

    private void AppendText(string text)
    {
        if (_state.SkipDestination) return;
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
    // PUA range is passed through unchanged. Mapping is delegated to the centralized
    // RtfSymbolLibrary so all symbol knowledge lives in one place.
    private static string RemapSymbolFontText(string text)
    {
        StringBuilder? sb = null;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            char? mapped = RtfSymbolLibrary.MapSymbolFontChar(c);
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

    // ---------------- control words ----------------

    private void HandleControlWord(RtfToken tok)
    {
        string cw = tok.Value;
        int? param = tok.Parameter;

        // Emit any buffered text bytes before formatting/structure can change.
        FlushPendingBytes();

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

        // Inside a skipped destination nothing may produce output or mutate document
        // structure (\par, \emdash, \trowd, ... would otherwise leak). Only \uc still
        // matters because it is group-scoped state that costs nothing to track.
        if (_state.SkipDestination && cw != "uc")
        {
            return;
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
            case "deflang":
                if (param.HasValue) _doc.DefaultLanguageId = param.Value;
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
            case "pn":
                // Legacy (Word 95 / WordPad) paragraph numbering: {\*\pn\pnlvlblt ...}.
                HandleLegacyPn();
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
            case "clvmgf":
                MarkCellVMergeStart();
                break;
            case "clvmrg":
                MarkCellVMergeContinuation();
                break;
            case "cell":
                CloseCell();
                break;
            case "row":
                CloseRow();
                break;
            case "nestcell":
                // Nested tables are flattened: each nested cell becomes a paragraph inside
                // the outer cell so no content is lost.
                FlushParagraph(forceEmit: true);
                break;
            case "nestrow":
                // End of a flattened nested row — nothing to close at the outer level.
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
                ParseStylesheet();
                break;
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
                _state.LegacyListOrdered = null;
                _paraBorderTarget = BorderTarget.None;
                _paraBoxMode = false;
                _pendingTabAlignment = TabAlignment.Left;
                _pendingTabLeader = TabLeader.None;
                break;
            case "s":
                // Paragraph style reference. Overlay the style's captured formatting (and its
                // \sbasedon chain) onto the current state; explicit control words that follow
                // still override. If the stylesheet declared this style as a heading
                // ("heading N" name or \outlinelevel), carry the level over.
                if (param.HasValue)
                {
                    ApplyStyle(param.Value);
                    if (_styleOutlineLevels.TryGetValue(param.Value, out int styleLevel))
                        _state.ParagraphFormat.OutlineLevel = styleLevel;
                }
                break;
            case "outlinelevel":
                // 0..8; only 0..5 map to <h1>..<h6>. 9 means "body text".
                if (param.HasValue && param.Value >= 0 && param.Value <= 8)
                    _state.ParagraphFormat.OutlineLevel = param.Value;
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

            // ---------- tab stops ----------
            case "tql": _pendingTabAlignment = TabAlignment.Left;    break;
            case "tqr": _pendingTabAlignment = TabAlignment.Right;   break;
            case "tqc": _pendingTabAlignment = TabAlignment.Center;  break;
            case "tqdec": _pendingTabAlignment = TabAlignment.Decimal; break;
            case "tldot": _pendingTabLeader = TabLeader.Dot;       break;
            case "tlhyph": _pendingTabLeader = TabLeader.Hyphen;   break;
            case "tlul": _pendingTabLeader = TabLeader.Underline;  break;
            case "tlth": _pendingTabLeader = TabLeader.Thick;      break;
            case "tleq": _pendingTabLeader = TabLeader.Equal;      break;
            case "tx":
                if (param.HasValue)
                {
                    _state.ParagraphFormat.TabStops.Add(new TabStop
                    {
                        PositionTwips = param.Value,
                        Alignment = _pendingTabAlignment,
                        Leader = _pendingTabLeader,
                    });
                }
                _pendingTabAlignment = TabAlignment.Left;
                _pendingTabLeader = TabLeader.None;
                break;

            case "par":
                FlushParagraph(forceEmit: true);
                break;

            case "line":
                if (!_state.SkipDestination) _paragraph.Runs.Add(new RtfLineBreak { Format = _state.CharFormat.Clone() });
                break;

            case "tab":
                if (!_state.SkipDestination)
                {
                    var tab = new RtfTab { Format = _state.CharFormat.Clone() };
                    ResolveTab(tab);
                    _paragraph.Runs.Add(tab);
                }
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
            case "chdate":
            case "chtime":
            case "chpgn":
            case "chatn":
            case "chftn":
                break;

            default:
                // Text-producing symbol control words (\bullet, \emdash, smart quotes, ...)
                // are resolved through the centralized RtfSymbolLibrary so legacy Word and
                // WordPad output is handled uniformly. Anything unrecognised is a safe no-op
                // per the RTF spec.
                if (RtfSymbolLibrary.TryGetControlWordSymbol(cw, out string symbolText))
                {
                    AppendText(symbolText);
                }
                break;
        }
    }

    private void HandleControlSymbol(RtfToken tok)
    {
        // \'hh contributes a raw byte to the pending buffer (so DBCS pairs stay together);
        // every other symbol is a boundary that must flush buffered bytes first.
        if (tok.Value == "'")
        {
            if (tok.Parameter.HasValue && !_state.SkipDestination)
            {
                _pendingBytes.Add((byte)tok.Parameter.Value);
            }
            return;
        }

        FlushPendingBytes();

        switch (tok.Value)
        {
            case "*":
                _nextIsOptionalDestination = true;
                break;
            case "\n":
            case "\r":
                if (!_state.SkipDestination)
                {
                    _paragraph.Runs.Add(new RtfLineBreak { Format = _state.CharFormat.Clone() });
                }
                break;
            default:
                // Text-producing control symbols (\\, \{, \}, \~, \-, \_) are resolved through
                // the centralized RtfSymbolLibrary. Unknown symbols are ignored.
                if (tok.Value.Length == 1 &&
                    RtfSymbolLibrary.TryGetControlSymbol(tok.Value[0], out string symbolText))
                {
                    AppendText(symbolText);
                }
                break;
        }
    }

    private void HandleUnicode(int codePoint)
    {
        if (_state.SkipDestination) { SkipUnicodeFallback(); return; }

        // Negative values represent unsigned via 16-bit wrap-around per spec.
        if (codePoint < 0) codePoint += 0x10000;

        // Word encodes non-BMP characters (emoji, ...) as two \uN escapes carrying the
        // UTF-16 surrogate halves. char.ConvertFromUtf32 rejects surrogate values, so pair
        // them up manually.
        if (codePoint >= 0xD800 && codePoint <= 0xDBFF)
        {
            _pendingHighSurrogate = (char)codePoint;
            SkipUnicodeFallback();
            return;
        }
        if (codePoint >= 0xDC00 && codePoint <= 0xDFFF)
        {
            if (_pendingHighSurrogate.HasValue)
            {
                AppendText(new string(new[] { _pendingHighSurrogate.Value, (char)codePoint }));
                _pendingHighSurrogate = null;
            }
            // A lone low surrogate is malformed \u2014 drop it.
            SkipUnicodeFallback();
            return;
        }

        _pendingHighSurrogate = null; // unpaired high surrogate \u2014 drop it
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
        // We're positioned right after \fonttbl inside its group. Two legal shapes:
        //   {\fonttbl{\f0\fswiss Arial;}{\f1\froman Times;}}   (braced entries)
        //   {\fonttbl\f0\fswiss Arial;\f1\froman Times;}       (unbraced entries)
        // Entries are terminated by ';'. Name text is only taken at the entry's own depth
        // so nested groups like {\*\falt Helvetica} don't pollute the font name.
        int depth = 1;
        RtfFont? current = null;
        int fontDepth = -1;
        var name = new StringBuilder();

        void Commit()
        {
            if (current == null) return;
            current.Name = name.ToString().TrimEnd(';').Trim();
            _doc.Fonts.Set(current.Index, current);
            current = null;
            name.Clear();
        }

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
                    if (current != null && depth < fontDepth) Commit();
                    if (depth == 0) return;
                    break;
                case RtfTokenType.ControlWord:
                    switch (t.Value)
                    {
                        case "f":
                            if (t.Parameter.HasValue)
                            {
                                // \f starts a new entry in both shapes.
                                if (current != null) Commit();
                                current = new RtfFont { Index = t.Parameter.Value };
                                fontDepth = depth;
                            }
                            break;
                        case "fcharset": if (current != null) current.CharSet = t.Parameter ?? 0; break;
                        case "fprq":     if (current != null) current.PitchAndFamily = t.Parameter ?? 0; break;
                    }
                    break;
                case RtfTokenType.Text:
                    foreach (char ch in t.Value)
                    {
                        if (ch == ';') Commit();
                        else if (current != null && depth == fontDepth) name.Append(ch);
                    }
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

    // ---------------- stylesheet ----------------

    private void ParseStylesheet()
    {
        // We're positioned inside the \stylesheet group. Each entry is {\sN ... Style Name;}.
        // We extract heading info and also capture \s0 (Normal/Default style) properties:
        // font (\f), size (\fs), foreground color (\cf), and language (\lang) — used by the
        // HTML emitter to emit body defaults so individual spans can omit those properties.
        int depth = 1;
        int? styleIndex = null;
        int? outline = null;
        int? styleFont = null;
        int? styleFs = null;
        int? styleCf = null;
        int? styleLang = null;
        var name = new StringBuilder();
        var def = new RtfStyleDefinition();

        void Commit()
        {
            if (styleIndex.HasValue)
            {
                // Store the captured formatting deltas so \sN can apply them later.
                def.Index = styleIndex.Value;
                if (outline.HasValue && outline.Value >= 0 && outline.Value <= 8)
                    def.OutlineLevel = outline.Value;
                _styles[styleIndex.Value] = def;

                // Capture body defaults from \s0 (Normal / Default Paragraph Style).
                if (styleIndex.Value == 0)
                {
                    if (styleFont.HasValue) _doc.DefaultFontIndex = styleFont.Value;
                    if (styleFs.HasValue)   _doc.DefaultBodyFontSizeHalfPoints = styleFs.Value;
                    if (styleCf.HasValue)   _doc.DefaultBodyColorIndex = styleCf.Value;
                    // Fall back to \s0 \lang if \deflang was not set at document level.
                    if (styleLang.HasValue && _doc.DefaultLanguageId == 0)
                        _doc.DefaultLanguageId = styleLang.Value;
                }

                int? level = outline;
                if (!level.HasValue)
                {
                    var n = name.ToString().TrimEnd(';').Trim();
                    if (n.Length >= 9 && n.StartsWith("heading ", StringComparison.OrdinalIgnoreCase)
                        && char.IsDigit(n[8]))
                    {
                        int h = n[8] - '0';
                        if (h >= 1 && h <= 9) level = h - 1;
                    }
                }
                if (level.HasValue && level.Value >= 0 && level.Value <= 8)
                    _styleOutlineLevels[styleIndex.Value] = level.Value;
            }
            styleIndex = null;
            outline = null;
            styleFont = null;
            styleFs = null;
            styleCf = null;
            styleLang = null;
            name.Clear();
            def = new RtfStyleDefinition();
        }

        while (_index < _tokens.Count && depth > 0)
        {
            var t = _tokens[_index++];
            switch (t.Type)
            {
                case RtfTokenType.GroupStart: depth++; break;
                case RtfTokenType.GroupEnd:
                    depth--;
                    if (depth == 1) Commit();
                    if (depth == 0) return;
                    break;
                case RtfTokenType.ControlWord:
                    switch (t.Value)
                    {
                        case "s":            if (t.Parameter.HasValue) styleIndex = t.Parameter.Value; break;
                        case "outlinelevel": if (t.Parameter.HasValue) outline = t.Parameter.Value; break;
                        // Capture \s0 body defaults (depth 2 = directly inside the style entry).
                        case "f":    if (depth == 2 && t.Parameter.HasValue) styleFont = t.Parameter.Value; break;
                        case "fs":   if (depth == 2 && t.Parameter.HasValue) styleFs   = t.Parameter.Value; break;
                        case "cf":   if (depth == 2 && t.Parameter.HasValue) styleCf   = t.Parameter.Value; break;
                        case "lang": if (depth == 2 && t.Parameter.HasValue) styleLang = t.Parameter.Value; break;
                    }

                    // Capture the style's own formatting deltas (only direct entries at depth 2).
                    if (depth == 2)
                        CaptureStyleDelta(def, t.Value, t.Parameter);
                    break;

                case RtfTokenType.Text:
                    if (depth == 2) name.Append(t.Value);
                    break;
            }
        }
    }

    /// <summary>
    /// Records a single control word encountered inside a stylesheet entry as a formatting
    /// delta on <paramref name="def"/>. Only the subset of properties commonly carried by
    /// Word paragraph/character styles is captured.
    /// </summary>
    private static void CaptureStyleDelta(RtfStyleDefinition def, string word, int? param)
    {
        switch (word)
        {
            case "sbasedon": if (param.HasValue) def.BasedOn = param.Value; break;

            // character
            case "b":       def.Bold      = param != 0; break;
            case "i":       def.Italic    = param != 0; break;
            case "strike":  def.Strike    = param != 0; break;
            case "caps":    def.AllCaps   = param != 0; break;
            case "scaps":   def.SmallCaps = param != 0; break;
            case "ul":      def.Underline = param != 0 ? UnderlineStyle.Single : UnderlineStyle.None; break;
            case "ulnone":  def.Underline = UnderlineStyle.None; break;
            case "uldb":    def.Underline = UnderlineStyle.Double; break;
            case "uldot":   def.Underline = UnderlineStyle.Dotted; break;
            case "uld":
            case "uldash":  def.Underline = UnderlineStyle.Dashed; break;
            case "ulwave":  def.Underline = UnderlineStyle.Wave; break;
            case "ulth":    def.Underline = UnderlineStyle.Thick; break;
            case "super":   def.VerticalPosition = Model.VerticalPosition.Superscript; break;
            case "sub":     def.VerticalPosition = Model.VerticalPosition.Subscript; break;
            case "nosupersub": def.VerticalPosition = Model.VerticalPosition.Normal; break;
            case "f":       if (param.HasValue) def.FontIndex = param.Value; break;
            case "fs":      if (param.HasValue) def.FontSizeHalfPoints = param.Value; break;
            case "cf":      def.ForegroundColorIndex = param; break;
            case "highlight": def.HighlightColorIndex = param; break;

            // paragraph
            case "ql": def.Alignment = TextAlignment.Left;    break;
            case "qc": def.Alignment = TextAlignment.Center;  break;
            case "qr": def.Alignment = TextAlignment.Right;   break;
            case "qj": def.Alignment = TextAlignment.Justify; break;
            case "li": def.LeftIndentTwips      = param ?? 0; break;
            case "ri": def.RightIndentTwips     = param ?? 0; break;
            case "fi": def.FirstLineIndentTwips = param ?? 0; break;
            case "sb": def.SpaceBeforeTwips     = param ?? 0; break;
            case "sa": def.SpaceAfterTwips      = param ?? 0; break;
            case "keep":   def.KeepTogether    = true; break;
            case "keepn":  def.KeepWithNext    = true; break;
            case "pagebb": def.PageBreakBefore = true; break;
        }
    }

    /// <summary>
    /// Applies the formatting of the referenced style (and its <c>\sbasedon</c> chain) onto the
    /// current run/paragraph state. Only properties the style explicitly declares are overlaid,
    /// so document-level defaults and later explicit control words remain in effect.
    /// </summary>
    private void ApplyStyle(int styleIndex)
    {
        // Resolve the based-on chain, most-derived last, guarding against cycles.
        var chain = new List<RtfStyleDefinition>();
        var seen = new HashSet<int>();
        int? cursor = styleIndex;
        while (cursor.HasValue && seen.Add(cursor.Value) && _styles.TryGetValue(cursor.Value, out var s))
        {
            chain.Add(s);
            cursor = s.BasedOn;
        }

        var cf = _state.CharFormat;
        var pf = _state.ParagraphFormat;

        // Apply base styles first, then progressively more-derived ones (chain is derived->base).
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            var d = chain[i];

            if (d.Bold.HasValue)      cf.Bold      = d.Bold.Value;
            if (d.Italic.HasValue)    cf.Italic    = d.Italic.Value;
            if (d.Strike.HasValue)    cf.Strike    = d.Strike.Value;
            if (d.AllCaps.HasValue)   cf.AllCaps   = d.AllCaps.Value;
            if (d.SmallCaps.HasValue) cf.SmallCaps = d.SmallCaps.Value;
            if (d.Underline.HasValue)
            {
                cf.UnderlineStyle = d.Underline.Value;
                cf.Underline = d.Underline.Value != UnderlineStyle.None;
            }
            if (d.VerticalPosition.HasValue)     cf.VerticalPosition = d.VerticalPosition.Value;
            if (d.FontIndex.HasValue)            cf.FontIndex = d.FontIndex.Value;
            if (d.FontSizeHalfPoints.HasValue)   cf.FontSizeHalfPoints = d.FontSizeHalfPoints.Value;
            if (d.ForegroundColorIndex.HasValue) cf.ForegroundColorIndex = d.ForegroundColorIndex.Value;
            if (d.HighlightColorIndex.HasValue)  cf.HighlightColorIndex = d.HighlightColorIndex.Value;

            if (d.Alignment.HasValue)            pf.Alignment = d.Alignment.Value;
            if (d.LeftIndentTwips.HasValue)      pf.LeftIndentTwips = d.LeftIndentTwips.Value;
            if (d.RightIndentTwips.HasValue)     pf.RightIndentTwips = d.RightIndentTwips.Value;
            if (d.FirstLineIndentTwips.HasValue) pf.FirstLineIndentTwips = d.FirstLineIndentTwips.Value;
            if (d.SpaceBeforeTwips.HasValue)     pf.SpaceBeforeTwips = d.SpaceBeforeTwips.Value;
            if (d.SpaceAfterTwips.HasValue)      pf.SpaceAfterTwips = d.SpaceAfterTwips.Value;
            if (d.OutlineLevel.HasValue)         pf.OutlineLevel = d.OutlineLevel.Value;
            if (d.KeepTogether.HasValue)         pf.KeepTogether = d.KeepTogether.Value;
            if (d.KeepWithNext.HasValue)         pf.KeepWithNext = d.KeepWithNext.Value;
            if (d.PageBreakBefore.HasValue)      pf.PageBreakBefore = d.PageBreakBefore.Value;
        }
    }

    // ---------------- legacy paragraph numbering (\*\pn) ----------------
    private void HandleLegacyPn()
    {
        // We're inside {\*\pn ...}. Determine bullet vs numbered, consume through the
        // matching close brace, then flag the *parent* group state so following paragraphs
        // are treated as list items until \pard resets.
        int depth = 1;
        bool ordered = true; // default to numbered; \pnlvlblt marks a bullet list
        while (_index < _tokens.Count && depth > 0)
        {
            var t = _tokens[_index++];
            switch (t.Type)
            {
                case RtfTokenType.GroupStart: depth++; break;
                case RtfTokenType.GroupEnd:   depth--; break;
                case RtfTokenType.ControlWord:
                    if (t.Value == "pnlvlblt") ordered = false;
                    break;
            }
        }
        // The main loop pushed a state at the group's '{'; it won't see the '}' we consumed.
        PopGroup();
        _state.LegacyListOrdered = ordered;
    }

    // ---------------- paragraph flush ----------------

    private void FlushParagraph(bool forceEmit)
    {
        FlushPendingBytes();

        // Paragraph formatting (\qc, \li, \sb, ...) applies to the paragraph terminated
        // by \par, so we capture the current paragraph format here rather than at start.
        _paragraph.Format = _state.ParagraphFormat.Clone();
        _paragraph.ListId = _state.ListId;
        _paragraph.ListLevel = _state.ListLevel;

        // Legacy \*\pn numbering: give these paragraphs a synthetic list id so the normal
        // list-collapsing pass groups them into <ul>/<ol>.
        if (_paragraph.ListId == null && _state.LegacyListOrdered.HasValue)
        {
            int syntheticId = _state.LegacyListOrdered.Value ? LegacyOrderedListId : LegacyBulletListId;
            _paragraph.ListId = syntheticId;
            _doc.Lists.SetDefinition(syntheticId, new RtfListDefinition { Ordered = _state.LegacyListOrdered.Value });
            _doc.Lists.MapOverride(syntheticId, syntheticId);
        }

        // Skip empty leading paragraphs entirely.
        if (_paragraph.Runs.Count == 0 && !forceEmit)
        {
            return;
        }

        AddBlock(_paragraph);
        _paragraph = new RtfParagraph { Format = _state.ParagraphFormat.Clone() };
    }

    /// <summary>Route a completed block into either the active table cell, the destination sink, or the document body.</summary>
    private void AddBlock(RtfBlock block)
    {
        if (_destinationSink != null)
            _destinationSink.Add(block);
        else if (_tableState != null && _tableState.CurrentCellBlocks != null)
            _tableState.CurrentCellBlocks.Add(block);
        else
            _doc.Body.Add(block);
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
        bool isListLevel = false;       // true when inside a \listlevel group
        int? currentListId = null;
        int? currentLs = null;
        RtfListDefinition? currentListDef = null;
        RtfListLevelDefinition? currentLevel = null;
        int entryDepth = -1;            // depth at which the current entry started
        int levelDepth = -1;            // depth at which the current level started
        StringBuilder? levelTextBuilder = null; // Accumulates text inside {\leveltext ...}
        bool inLevelText = false;

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

                    // Closing a \listlevel group
                    if (isListLevel && depth == levelDepth - 1)
                    {
                        if (currentLevel != null && currentListDef != null)
                        {
                            // Finalize level text from accumulated text
                            if (inLevelText && levelTextBuilder != null)
                            {
                                currentLevel.LevelText = levelTextBuilder.ToString();
                            }
                            currentListDef.Levels.Add(currentLevel);
                        }
                        isListLevel = false;
                        currentLevel = null;
                        levelDepth = -1;
                        inLevelText = false;
                        levelTextBuilder = null;
                    }

                    // Closing the current \list or \listoverride entry
                    if (depth == entryDepth - 1)
                    {
                        if (isListEntry && currentListId.HasValue && currentListDef != null)
                        {
                            // Determine if ordered based on any level having numeric format
                            currentListDef.Ordered = currentListDef.Levels.Any(lvl => !ListNumberingFormatter.IsBulletFormat(lvl.NumberFormat));
                            _doc.Lists.SetDefinition(currentListId.Value, currentListDef);
                        }
                        else if (isOverrideEntry && currentListId.HasValue && currentLs.HasValue)
                        {
                            _doc.Lists.MapOverride(currentLs.Value, currentListId.Value);
                        }

                        isListEntry = false;
                        isOverrideEntry = false;
                        currentListId = null;
                        currentLs = null;
                        currentListDef = null;
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
                            currentListDef = new RtfListDefinition();
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

                        case "listlevel":
                            if (isListEntry)
                            {
                                isListLevel = true;
                                levelDepth = depth;
                                currentLevel = new RtfListLevelDefinition();
                            }
                            break;

                        case "levelnfc":
                        case "levelnfcn":
                            if (isListLevel && currentLevel != null && t.Parameter.HasValue)
                                currentLevel.NumberFormat = t.Parameter.Value;
                            break;

                        case "levelstartat":
                            if (isListLevel && currentLevel != null && t.Parameter.HasValue)
                                currentLevel.StartAt = t.Parameter.Value;
                            break;

                        case "leveljc":
                        case "leveljcn":
                            if (isListLevel && currentLevel != null && t.Parameter.HasValue)
                                currentLevel.Justification = t.Parameter.Value;
                            break;

                        case "levelfollow":
                            if (isListLevel && currentLevel != null && t.Parameter.HasValue)
                                currentLevel.FollowChar = t.Parameter.Value;
                            break;

                        case "levelnorestart":
                            if (isListLevel && currentLevel != null && t.Parameter.HasValue)
                                currentLevel.Restart = t.Parameter.Value == 0;
                            break;

                        case "levellegal":
                            if (isListLevel && currentLevel != null)
                                currentLevel.Legal = true;
                            break;

                        case "leveltext":
                            // Start capturing level text
                            if (isListLevel)
                            {
                                inLevelText = true;
                                levelTextBuilder = new StringBuilder();
                            }
                            break;

                        case "levelnumbers":
                            // Placeholder positions in level text - we don't need to parse this in detail
                            // as we'll handle it when rendering
                            break;

                        case "f":
                            // Font index - capture if inside a level definition
                            if (isListLevel && currentLevel != null && t.Parameter.HasValue)
                                currentLevel.FontIndex = t.Parameter.Value;
                            break;
                    }
                    break;

                case RtfTokenType.Text:
                    // Capture text inside \leveltext group
                    if (inLevelText && levelTextBuilder != null)
                    {
                        levelTextBuilder.Append(t.Value);
                    }
                    break;

                case RtfTokenType.ControlSymbol:
                    // Handle special characters in level text
                    if (inLevelText && levelTextBuilder != null)
                    {
                        switch (t.Value)
                        {
                            case "'":
                                // Hex-encoded character (e.g., \'b7 for bullet)
                                if (t.Parameter.HasValue)
                                {
                                    levelTextBuilder.Append((char)t.Parameter.Value);
                                }
                                break;
                        }
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
                    AppendTextBytes(t.Value);
                    break;
            }
        }

        FlushPendingBytes();
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

        // Redirect AddBlock calls (from \par flushes, \page, etc.) away from _doc.Body so
        // that header/footer/footnote content does not pollute the document body.
        var sinkBlocks = new List<RtfBlock>();
        var prevSink = _destinationSink;
        _destinationSink = sinkBlocks;

        void Finish()
        {
            FlushPendingBytes();
            // Collect runs from every \par-flushed intermediate paragraph…
            foreach (var b in sinkBlocks)
                if (b is RtfParagraph sp) target.AddRange(sp.Runs);
            // …and the final paragraph (not yet terminated by \par).
            target.AddRange(_paragraph.Runs);
            _paragraph = savedParagraph;
            _state.SkipDestination = oldSkip;
            _destinationSink = prevSink;
        }

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
                        Finish();
                        PopGroup(); // pop the state that our caller's GroupStart pushed
                        return;
                    }
                    PopGroup();
                    break;
                case RtfTokenType.ControlWord: HandleControlWord(t); break;
                case RtfTokenType.ControlSymbol: HandleControlSymbol(t); break;
                case RtfTokenType.Text:
                    AppendTextBytes(t.Value);
                    break;
            }
        }

        Finish();
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

    // Pending tab-stop attributes accumulated before the closing \txN control word.
    private TabAlignment _pendingTabAlignment = TabAlignment.Left;
    private TabLeader _pendingTabLeader = TabLeader.None;

    /// <summary>
    /// Resolves a tab against the current paragraph's defined tab stops, baking the advance
    /// width and leader onto the run. When the tab falls back to a default tab stop, width is
    /// left at 0 so the emitter renders the legacy em-space.
    /// </summary>
    private void ResolveTab(RtfTab tab)
    {
        var stops = _state.ParagraphFormat.TabStops;
        if (stops.Count == 0) return;

        int ordinal = 0;
        foreach (var r in _paragraph.Runs)
            if (r is RtfTab) ordinal++;

        if (ordinal >= stops.Count) return;

        var stop = stops[ordinal];
        int prev = ordinal > 0 ? stops[ordinal - 1].PositionTwips : 0;
        int width = stop.PositionTwips - prev;
        if (width <= 0) return;

        tab.WidthTwips = width;
        tab.Leader = stop.Leader;
    }

    private void BeginTableRow()
    {
        if (_tableState == null)
        {
            _tableState = new TableBuildState();
        }

        // Word emits \trowd twice per row: once to define column boundaries, then again
        // immediately before \row to repeat the definition. Detect that case by checking
        // whether cells have already been collected. When they have, only refresh the
        // row-definition metadata (boundaries, formats) — never discard the accumulated
        // cell content, which \row will consume.
        bool rowHasCells = _tableState.CurrentRowCells.Count > 0;

        _tableState.CurrentRowBoundaries.Clear();
        _tableState.PendingMergeStart = false;
        _tableState.PendingMergeContinue = false;
        _tableState.PendingVMergeStart = false;
        _tableState.PendingVMergeContinue = false;
        _tableState.PendingCell = new CellDefState();
        _tableState.RowDef = new RowDefState();
        _tableState.CurrentBorderTarget = BorderTarget.None;

        if (!rowHasCells)
        {
            // First \trowd for this row: start with clean cell buffers.
            _tableState.CurrentCellBlocks = new List<RtfBlock>();
            _tableState.CurrentRowCells.Clear();
            _tableState.CurrentRowCellStates.Clear();
        }
        else
        {
            // Repeated \trowd (Word pattern): keep accumulated cells; only refresh the
            // cell-state list so the new \cellx declarations re-populate it cleanly.
            _tableState.CurrentRowCellStates.Clear();
        }
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
        snap.VMergeStart = _tableState.PendingVMergeStart;
        snap.VMergeContinue = _tableState.PendingVMergeContinue;
        _tableState.CurrentRowCellStates.Add(snap);

        _tableState.PendingMergeStart = false;
        _tableState.PendingMergeContinue = false;
        _tableState.PendingVMergeStart = false;
        _tableState.PendingVMergeContinue = false;
        _tableState.PendingCell = new CellDefState();
        _tableState.CurrentBorderTarget = BorderTarget.None;
    }

    private void MarkCellMergeStart() { if (_tableState != null) _tableState.PendingMergeStart = true; }
    private void MarkCellMergeContinuation() { if (_tableState != null) _tableState.PendingMergeContinue = true; }
    private void MarkCellVMergeStart() { if (_tableState != null) _tableState.PendingVMergeStart = true; }
    private void MarkCellVMergeContinuation() { if (_tableState != null) _tableState.PendingVMergeContinue = true; }

    private void CloseCell()
    {
        if (_tableState == null) return;
        // Defensive: some malformed documents emit \cell with no open row buffer.
        if (_tableState.CurrentCellBlocks == null)
            _tableState.CurrentCellBlocks = new List<RtfBlock>();

        // Flush any pending paragraph runs into the cell buffer.
        FlushPendingBytes();
        if (_paragraph.Runs.Count > 0)
        {
            _paragraph.Format = _state.ParagraphFormat.Clone();
            _tableState.CurrentCellBlocks.Add(_paragraph);
            _paragraph = new RtfParagraph { Format = _state.ParagraphFormat.Clone() };
        }

        _tableState.CurrentRowCells.Add(_tableState.CurrentCellBlocks);
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
                VerticalMergeStart = def.VMergeStart,
                VerticalMergeContinue = def.VMergeContinue,
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
        public bool PendingVMergeStart { get; set; }
        public bool PendingVMergeContinue { get; set; }

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
        public bool VMergeStart { get; set; }
        public bool VMergeContinue { get; set; }
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
            VMergeStart = VMergeStart,
            VMergeContinue = VMergeContinue,
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
        CollapseBlockList(_doc.Body);
        // Also collapse lists that landed inside table cells.
        foreach (var block in _doc.Body)
        {
            if (block is RtfTable table)
            {
                foreach (var row in table.Rows)
                    foreach (var cell in row.Cells)
                        CollapseBlockList(cell.Blocks);
            }
        }
    }

    private void CollapseBlockList(List<RtfBlock> body)
    {
        for (int i = 0; i < body.Count; i++)
        {
            if (body[i] is RtfParagraph p && p.ListId.HasValue)
            {
                int listId = p.ListId.Value;
                RtfListDefinition? def = null;
                bool ordered = _doc.Lists.TryGet(listId, out def!) && def.Ordered;
                var list = new RtfList { Ordered = ordered, Definition = def, ListId = listId };

                // Track item numbers per level for restart logic
                var levelCounters = new Dictionary<int, int>();

                int j = i;
                while (j < body.Count && body[j] is RtfParagraph pp && pp.ListId == listId)
                {
                    int level = pp.ListLevel;

                    // Reset counters for deeper levels when moving up
                    var keysToRemove = levelCounters.Keys.Where(k => k > level).ToList();
                    foreach (var key in keysToRemove)
                        levelCounters.Remove(key);

                    // Increment counter for this level
                    if (!levelCounters.ContainsKey(level))
                    {
                        // Get start value from definition if available
                        int startAt = 1;
                        if (def != null && level < def.Levels.Count)
                            startAt = def.Levels[level].StartAt;
                        levelCounters[level] = startAt;
                    }
                    else
                    {
                        levelCounters[level]++;
                    }

                    var item = new RtfListItem 
                    { 
                        Level = level, 
                        Format = pp.Format,
                        Number = levelCounters[level]
                    };
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
        cw == "shp" || cw == "shpinst" || cw == "pn" ||
        cw == "pntext" || cw == "pntxta" || cw == "pntxtb" || cw == "listtext";

    // ---------------- nested state ----------------

    private const int LegacyBulletListId = -1001;
    private const int LegacyOrderedListId = -1002;

    private sealed class GroupState
    {
        public CharFormat CharFormat { get; set; } = new CharFormat();
        public ParagraphFormat ParagraphFormat { get; set; } = new ParagraphFormat();
        public bool SkipDestination { get; set; }
        public int UnicodeSkipCount { get; set; } = 1;
        public int? ListId { get; set; }
        public int ListLevel { get; set; }
        public bool InTable { get; set; }

        /// <summary>Legacy \*\pn numbering active for following paragraphs: false = bullet, true = ordered. Null = none.</summary>
        public bool? LegacyListOrdered { get; set; }

        public GroupState Clone() => new GroupState
        {
            CharFormat = CharFormat.Clone(),
            ParagraphFormat = ParagraphFormat.Clone(),
            SkipDestination = SkipDestination,
            UnicodeSkipCount = UnicodeSkipCount,
            ListId = ListId,
            ListLevel = ListLevel,
            InTable = InTable,
            LegacyListOrdered = LegacyListOrdered,
        };
    }
}
