// ============================================================================
// Product:   RtfBridge
// File:      RtfSymbolLibrary.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using System.Collections.Generic;

namespace RtfBridge.Rtf;

/// <summary>
/// Centralized, reusable library for translating RTF symbol control words, control symbols,
/// and Symbol/Wingdings/Webdings font glyphs into Unicode. Consolidates mappings that were
/// previously scattered through the parser and adds broad coverage for legacy Microsoft Word,
/// WordPad, and older RTF-writer output.
/// </summary>
/// <remarks>
/// Designed to compile and run on all supported targets (.NET Framework 4.6.2 / 4.8,
/// .NET Standard 2.0, .NET 8, .NET 10). Uses only plain collections and string/char APIs
/// that exist on every target — no modern-only surface.
/// </remarks>
internal static class RtfSymbolLibrary
{
    // ------------------------------------------------------------------------
    // Text-producing control words (\bullet, \emdash, ...). Structural words that
    // create elements (\par, \line, \tab, \page, \sect) are intentionally excluded;
    // the parser handles those directly.
    // ------------------------------------------------------------------------
    private static readonly Dictionary<string, string> ControlWordSymbols =
        new Dictionary<string, string>
        {
            // Dashes
            { "emdash",    "\u2014" }, // — em dash
            { "endash",    "\u2013" }, // – en dash

            // Spaces
            { "emspace",   "\u2003" }, // em space
            { "enspace",   "\u2002" }, // en space
            { "qmspace",   "\u2005" }, // four-per-em (quarter-em) space

            // Bullets
            { "bullet",    "\u2022" }, // • bullet

            // Smart quotes
            { "lquote",    "\u2018" }, // ‘ left single quote
            { "rquote",    "\u2019" }, // ’ right single quote
            { "ldblquote", "\u201C" }, // “ left double quote
            { "rdblquote", "\u201D" }, // ” right double quote

            // Bi-directional / joiner marks
            { "zwj",       "\u200D" }, // zero-width joiner
            { "zwnj",      "\u200C" }, // zero-width non-joiner
            { "ltrmark",   "\u200E" }, // left-to-right mark
            { "rtlmark",   "\u200F" }, // right-to-left mark

            // Legacy / older Word & WordPad producers
            { "nbsp",      "\u00A0" }, // non-breaking space (some legacy writers)
            { "softline",  "\u000A" }, // soft line break as newline fallback
        };

    // ------------------------------------------------------------------------
    // Control symbols (backslash followed by a single non-alphabetic char).
    // ------------------------------------------------------------------------
    private static readonly Dictionary<char, string> ControlSymbolChars =
        new Dictionary<char, string>
        {
            { '\\', "\\"     },       // literal backslash
            { '{',  "{"      },       // literal open brace
            { '}',  "}"      },       // literal close brace
            { '~',  "\u00A0" },       // non-breaking space
            { '-',  "\u00AD" },       // optional (soft) hyphen
            { '_',  "\u2011" },       // non-breaking hyphen
        };

    /// <summary>
    /// Attempts to resolve a text-producing symbol control word (e.g., <c>bullet</c>,
    /// <c>emdash</c>) to its Unicode representation.
    /// </summary>
    /// <param name="controlWord">The control word without the leading backslash.</param>
    /// <param name="text">The resolved Unicode string when found.</param>
    /// <returns><c>true</c> when the control word is a known text symbol.</returns>
    public static bool TryGetControlWordSymbol(string controlWord, out string text)
    {
        return ControlWordSymbols.TryGetValue(controlWord, out text!);
    }

    /// <summary>
    /// Attempts to resolve a control symbol (e.g., <c>~</c>, <c>-</c>, <c>_</c>) to its
    /// Unicode representation.
    /// </summary>
    /// <param name="symbol">The single symbol character following the backslash.</param>
    /// <param name="text">The resolved Unicode string when found.</param>
    /// <returns><c>true</c> when the symbol is a known text symbol.</returns>
    public static bool TryGetControlSymbol(char symbol, out string text)
    {
        return ControlSymbolChars.TryGetValue(symbol, out text!);
    }

    /// <summary>
    /// Maps well-known Symbol / Wingdings / Webdings Private-Use-Area code points
    /// (0xF000..0xF0FF) — and the legacy ASCII bullet position (0xB7) — that older
    /// Word and LibreOffice emit for bullets and dingbats to their Unicode equivalents.
    /// Returns <c>null</c> for code points that should pass through unchanged.
    /// </summary>
    public static char? MapSymbolFontChar(char c)
    {
        switch ((int)c)
        {
            // Symbol font bullets and common characters
            case 0xF0B7: return '\u2022'; // • bullet (Symbol)
            case 0xF0A7: return '\u25AA'; // ▪ black small square (Symbol filled square)
            case 0xF0A8: return '\u25E6'; // ◦ white bullet (Symbol open bullet)
            case 0xF0A6: return '\u2666'; // ♦ black diamond
            case 0xF0E0: return '\u25C6'; // ◆ white diamond
            case 0xF0A0: return '\u00A0'; // nbsp
            case 0x00B7: return '\u2022'; // • bullet (legacy ASCII position in Symbol font)

            // Arrows (Symbol font)
            case 0xF0D8: return '\u2192'; // → right arrow
            case 0xF0DC: return '\u2190'; // ← left arrow
            case 0xF0D9: return '\u2191'; // ↑ up arrow
            case 0xF0DB: return '\u2193'; // ↓ down arrow
            case 0xF0AB: return '\u21D2'; // ⇒ double right arrow
            case 0xF0AF: return '\u21D0'; // ⇐ double left arrow
            case 0xF0AD: return '\u21D4'; // ⇔ double left-right arrow
            case 0xF0AE: return '\u2194'; // ↔ left-right arrow

            // Wingdings bullets and pointers
            case 0xF06C: return '\u25A0'; // ■ black square (Wingdings)
            case 0xF06D: return '\u25A1'; // □ white square (Wingdings)
            case 0xF06E: return '\u25AA'; // ▪ small black square (Wingdings)
            case 0xF06F: return '\u25AB'; // ▫ small white square (Wingdings)
            case 0xF070: return '\u25C6'; // ◆ black diamond (Wingdings)
            case 0xF071: return '\u25C7'; // ◇ white diamond (Wingdings)
            case 0xF075: return '\u2605'; // ★ black star (Wingdings)
            case 0xF076: return '\u2606'; // ☆ white star (Wingdings)
            case 0xF0FC: return '\u2713'; // ✓ check mark (Wingdings)
            case 0xF0FB: return '\u2714'; // ✔ heavy check mark (Wingdings)
            case 0xF0FE: return '\u2717'; // ✗ ballot X (Wingdings)
            case 0xF0FD: return '\u2718'; // ✘ heavy ballot X (Wingdings)

            // Wingdings arrows and pointers
            case 0xF0E8: return '\u25BA'; // ► black right-pointing pointer
            case 0xF0E9: return '\u25C4'; // ◄ black left-pointing pointer
            case 0xF0EA: return '\u25B2'; // ▲ black up-pointing triangle
            case 0xF0EB: return '\u25BC'; // ▼ black down-pointing triangle
            case 0xF0DA: return '\u25BB'; // ▻ white right-pointing pointer
            case 0xF0D5: return '\u25C5'; // ◅ white left-pointing pointer
            case 0xF0D6: return '\u25B3'; // △ white up-pointing triangle
            case 0xF0D7: return '\u25BD'; // ▽ white down-pointing triangle

            // Webdings bullets and symbols
            case 0xF030: return '\u25CF'; // ● black circle (Webdings '0')
            case 0xF031: return '\u25CB'; // ○ white circle (Webdings '1')
            case 0xF032: return '\u25A0'; // ■ black square (Webdings '2')
            case 0xF033: return '\u25A1'; // □ white square (Webdings '3')
            case 0xF034: return '\u25C6'; // ◆ black diamond (Webdings '4')
            case 0xF035: return '\u25C7'; // ◇ white diamond (Webdings '5')
            case 0xF036: return '\u2605'; // ★ black star (Webdings '6')
            case 0xF037: return '\u2606'; // ☆ white star (Webdings '7')

            // Symbol-font mathematical and special characters
            case 0xF0B1: return '\u00B1'; // ± plus-minus
            case 0xF0B4: return '\u00D7'; // × multiplication
            case 0xF0B8: return '\u00F7'; // ÷ division
            case 0xF0B9: return '\u2260'; // ≠ not equal
            case 0xF0BA: return '\u2261'; // ≡ identical
            case 0xF0BC: return '\u2264'; // ≤ less than or equal
            case 0xF0BE: return '\u2265'; // ≥ greater than or equal
            case 0xF0C5: return '\u221E'; // ∞ infinity
            case 0xF0CD: return '\u2211'; // ∑ summation
            case 0xF0D1: return '\u221A'; // √ square root
            case 0xF0CE: return '\u220F'; // ∏ product
            case 0xF0B5: return '\u221D'; // ∝ proportional to
            case 0xF0C2: return '\u2202'; // ∂ partial derivative
            case 0xF0C4: return '\u2207'; // ∇ nabla
            case 0xF0CA: return '\u222B'; // ∫ integral
            case 0xF0CF: return '\u2212'; // − minus sign
        }
        return null;
    }
}
