// ============================================================================
// Product:   RtfBridge
// File:      RtfParseException.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using System;

namespace RtfBridge;

/// <summary>Thrown for unrecoverable RTF parse errors (truncated stream, invalid encoding, etc.).</summary>
public sealed class RtfParseException : Exception
{
    public int Position { get; }

    public RtfParseException(string message, int position) : base(message)
    {
        Position = position;
    }

    public RtfParseException(string message, int position, Exception innerException)
        : base(message, innerException)
    {
        Position = position;
    }
}
