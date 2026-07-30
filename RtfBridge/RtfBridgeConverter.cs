// ============================================================================
// Product:   RtfBridge
// File:      RtfBridgeConverter.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RtfBridge.Html;
using RtfBridge.Rtf;

namespace RtfBridge;

/// <summary>
/// Pure-managed RTF-to-HTML converter. Entry point for the library.
/// Thread-safe: instances hold no mutable state; a single instance may be shared across threads.
/// </summary>
public sealed class RtfBridgeConverter : IRtfBridgeConverter
{
    static RtfBridgeConverter()
    {
        EncodingBootstrap.EnsureRegistered();
    }

    /// <inheritdoc />
    public string ConvertRtfToHtml(string rtfText)
        => ConvertRtfToHtml(rtfText, RtfToHtmlOptions.Default);

    /// <inheritdoc />
    public string ConvertRtfToHtml(string rtfText, RtfToHtmlOptions options)
        => ConvertRtfToHtmlDetailed(rtfText, options).Html;

    /// <inheritdoc />
    public string ConvertRtfToHtml(Stream rtfStream, RtfToHtmlOptions options)
    {
        if (rtfStream == null) throw new ArgumentNullException(nameof(rtfStream));
        if (options == null) throw new ArgumentNullException(nameof(options));

        byte[] bytes = ReadAllBytes(rtfStream);
        return ConvertBytes(bytes, options).Html;
    }

    /// <inheritdoc />
    public RtfConversionResult ConvertRtfToHtmlDetailed(string rtfText, RtfToHtmlOptions options)
    {
        if (rtfText == null) throw new ArgumentNullException(nameof(rtfText));
        if (options == null) throw new ArgumentNullException(nameof(options));

        if (rtfText.Length == 0)
        {
            return new RtfConversionResult(options.EmitFullDocument
                ? "<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body></body></html>"
                : string.Empty);
        }

        // RTF is a 7-bit ASCII envelope; codepage escapes are handled inside the parser.
        // We tokenize on bytes so \'hh is decoded correctly per the document's ANSI codepage.
        byte[] bytes = Encoding.GetEncoding(1252).GetBytes(rtfText);
        return ConvertBytes(bytes, options);
    }

    private static RtfConversionResult ConvertBytes(byte[] bytes, RtfToHtmlOptions options)
    {
        try
        {
            var tokenizer = new RtfTokenizer(bytes);
            var tokens = new List<RtfToken>(tokenizer.Tokenize());

            // Outlook RTF-encapsulated HTML (\fromhtml1) carries the original markup inside
            // \*\htmltag destinations — extract it directly instead of re-deriving HTML.
            if (options.DecodeHtmlEncapsulation && HtmlDeEncapsulator.IsEncapsulated(tokens))
            {
                return new RtfConversionResult(HtmlDeEncapsulator.Extract(tokens, options.DefaultCodePage));
            }

            var parser = new RtfParser(tokens, options.DefaultCodePage, options);
            var document = parser.Parse();
            var emitter = new HtmlEmitter(document, options);
            string html = emitter.Emit();
            return new RtfConversionResult(html, parser.Warnings);
        }
        catch (RtfParseException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RtfParseException("RTF conversion failed: " + ex.Message, 0, ex);
        }
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        if (stream is MemoryStream ms) return ms.ToArray();
        using (var buffer = new MemoryStream())
        {
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }
}
