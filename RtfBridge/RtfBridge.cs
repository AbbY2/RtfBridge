// ============================================================================
// Product:   RtfBridge
// File:      RtfBridge.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using System;
using System.IO;

namespace RtfBridge;

/// <summary>
/// Backwards-compatibility alias for <see cref="RtfBridgeConverter"/>.
/// The class was renamed because a type sharing its namespace's name forces consumers to
/// write <c>global::RtfBridge.RtfBridge</c>.
/// </summary>
[Obsolete("Use RtfBridgeConverter instead. This alias will be removed in a future major version.")]
public sealed class RtfBridge : IRtfBridgeConverter
{
    private readonly RtfBridgeConverter _inner = new RtfBridgeConverter();

    /// <inheritdoc />
    public string ConvertRtfToHtml(string rtfText) => _inner.ConvertRtfToHtml(rtfText);

    /// <inheritdoc />
    public string ConvertRtfToHtml(string rtfText, RtfToHtmlOptions options) => _inner.ConvertRtfToHtml(rtfText, options);

    /// <inheritdoc />
    public string ConvertRtfToHtml(Stream rtfStream, RtfToHtmlOptions options) => _inner.ConvertRtfToHtml(rtfStream, options);

    /// <inheritdoc />
    public RtfConversionResult ConvertRtfToHtmlDetailed(string rtfText, RtfToHtmlOptions options) => _inner.ConvertRtfToHtmlDetailed(rtfText, options);
}
