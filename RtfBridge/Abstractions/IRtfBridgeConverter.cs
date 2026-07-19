// ============================================================================
// Product:   RtfBridge
// File:      IRtfBridgeConverter.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using System.IO;

namespace RtfBridge;

/// <summary>
/// Converts Rich Text Format (RTF) documents into HTML.
/// </summary>
public interface IRtfBridgeConverter
{
    /// <summary>Converts an RTF string to a full HTML document using default options.</summary>
    string ConvertRtfToHtml(string rtfText);

    /// <summary>Converts an RTF string to HTML using the supplied options.</summary>
    string ConvertRtfToHtml(string rtfText, RtfToHtmlOptions options);

    /// <summary>Converts an RTF byte stream to HTML using the supplied options.</summary>
    string ConvertRtfToHtml(Stream rtfStream, RtfToHtmlOptions options);

    /// <summary>Converts RTF to HTML and returns detailed conversion metadata (warnings, embedded images).</summary>
    RtfConversionResult ConvertRtfToHtmlDetailed(string rtfText, RtfToHtmlOptions options);
}
