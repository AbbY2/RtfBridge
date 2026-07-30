// ============================================================================
// Product:   RtfBridge
// File:      ListNumberingFormatter.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using System;
using System.Text;

namespace RtfBridge.Rtf;

/// <summary>
/// Converts RTF list numbering formats (\levelnfc values) to HTML/CSS list-style-type
/// and generates formatted number strings for various numbering styles.
/// </summary>
internal static class ListNumberingFormatter
{
    /// <summary>
    /// Maps RTF \levelnfc / \levelnfcn values to CSS list-style-type.
    /// Returns null for custom formats that require runtime number generation.
    /// </summary>
    public static string? GetCssListStyleType(int levelnfc)
    {
        return levelnfc switch
        {
            0 => "decimal",                    // Arabic (1, 2, 3, ...)
            1 => "upper-roman",                // Upper Roman (I, II, III, ...)
            2 => "lower-roman",                // Lower Roman (i, ii, iii, ...)
            3 => "upper-alpha",                // Upper Alpha (A, B, C, ...)
            4 => "lower-alpha",                // Lower Alpha (a, b, c, ...)
            5 => "decimal",                    // Ordinal (1st, 2nd, 3rd, ...) - CSS doesn't support, use decimal
            6 => "decimal",                    // Cardinal text (One, Two, Three, ...) - CSS doesn't support
            7 => "decimal",                    // Ordinal text (First, Second, ...) - CSS doesn't support
            10 => "decimal-leading-zero",      // Decimal with leading zero (01, 02, 03, ...)
            22 => "none",                      // No number
            23 => "disc",                      // Bullet
            255 => "none",                     // No number (alternate)
            _ => null                          // Custom or unsupported format
        };
    }

    /// <summary>
    /// Determines if the numbering format is a bullet (non-numeric).
    /// </summary>
    public static bool IsBulletFormat(int levelnfc)
    {
        return levelnfc == 23; // Standard bullet format
    }

    /// <summary>
    /// Formats a number according to the RTF numbering format.
    /// </summary>
    public static string FormatNumber(int number, int levelnfc)
    {
        return levelnfc switch
        {
            0 => number.ToString(),                           // Arabic
            1 => ToRomanUpper(number),                        // Upper Roman
            2 => ToRomanLower(number),                        // Lower Roman
            3 => ToAlphaUpper(number),                        // Upper Alpha
            4 => ToAlphaLower(number),                        // Lower Alpha
            5 => ToOrdinal(number),                           // Ordinal (1st, 2nd, 3rd)
            6 => ToCardinalText(number),                      // Cardinal text (One, Two, Three)
            7 => ToOrdinalText(number),                       // Ordinal text (First, Second)
            10 => number.ToString("D2"),                      // Decimal with leading zero
            22 => string.Empty,                               // No number
            23 => "•",                                        // Bullet (shouldn't be called for bullets, but fallback)
            255 => string.Empty,                              // No number
            _ => number.ToString()                            // Fallback to Arabic
        };
    }

    /// <summary>
    /// Gets common bullet characters by format or level.
    /// </summary>
    public static string GetBulletCharacter(int level)
    {
        return level switch
        {
            0 => "•",  // Bullet
            1 => "○",  // White circle
            2 => "■",  // Black square
            3 => "□",  // White square
            4 => "◆",  // Black diamond
            5 => "◇",  // White diamond
            6 => "►",  // Black right pointer
            7 => "▻",  // White right pointer
            _ => "•"   // Default bullet
        };
    }

    #region Number Formatting Helpers

    private static string ToRomanUpper(int number)
    {
        if (number <= 0) return string.Empty;
        if (number >= 4000) return number.ToString(); // Roman numerals don't go this high

        StringBuilder sb = new StringBuilder();
        int[] values = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        string[] numerals = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };

        for (int i = 0; i < values.Length; i++)
        {
            while (number >= values[i])
            {
                sb.Append(numerals[i]);
                number -= values[i];
            }
        }
        return sb.ToString();
    }

    private static string ToRomanLower(int number)
    {
        return ToRomanUpper(number).ToLowerInvariant();
    }

    private static string ToAlphaUpper(int number)
    {
        if (number <= 0) return string.Empty;
        StringBuilder sb = new StringBuilder();

        // Convert to base-26 (A-Z)
        while (number > 0)
        {
            number--;
            sb.Insert(0, (char)('A' + (number % 26)));
            number /= 26;
        }
        return sb.ToString();
    }

    private static string ToAlphaLower(int number)
    {
        return ToAlphaUpper(number).ToLowerInvariant();
    }

    private static string ToOrdinal(int number)
    {
        if (number <= 0) return string.Empty;

        string suffix = "th";
        int lastDigit = number % 10;
        int lastTwoDigits = number % 100;

        // Special cases for 11th, 12th, 13th
        if (lastTwoDigits >= 11 && lastTwoDigits <= 13)
        {
            suffix = "th";
        }
        else
        {
            suffix = lastDigit switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th"
            };
        }

        return number.ToString() + suffix;
    }

    private static string ToCardinalText(int number)
    {
        if (number <= 0) return string.Empty;
        if (number > 20) return number.ToString(); // Only support 1-20

        string[] cardinals = 
        {
            "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten",
            "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen",
            "Eighteen", "Nineteen", "Twenty"
        };

        return number <= 20 ? cardinals[number] : number.ToString();
    }

    private static string ToOrdinalText(int number)
    {
        if (number <= 0) return string.Empty;
        if (number > 20) return number.ToString(); // Only support 1-20

        string[] ordinals = 
        {
            "", "First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh", "Eighth",
            "Ninth", "Tenth", "Eleventh", "Twelfth", "Thirteenth", "Fourteenth", "Fifteenth",
            "Sixteenth", "Seventeenth", "Eighteenth", "Nineteenth", "Twentieth"
        };

        return number <= 20 ? ordinals[number] : number.ToString();
    }

    #endregion
}
