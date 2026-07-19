// ============================================================================
// Product:   RtfBridge
// File:      HtmlSanitizationLevel.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
namespace RtfBridge;

/// <summary>How aggressively to sanitize the emitted HTML.</summary>
public enum HtmlSanitizationLevel
{
    /// <summary>No sanitization. Text is still HTML-encoded to prevent injection.</summary>
    None = 0,

    /// <summary>Encode all text; strip control characters. Default.</summary>
    Basic = 1,

    /// <summary>Basic + drop any URL whose scheme is not in {http, https, mailto, tel, #}.</summary>
    Strict = 2,
}
