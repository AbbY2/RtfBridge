// ============================================================================
// Product:   RtfBridge
// File:      RtfToken.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
namespace RtfBridge.Rtf;

internal enum RtfTokenType
{
    /// <summary>Opening group brace <c>{</c>.</summary>
    GroupStart,
    /// <summary>Closing group brace <c>}</c>.</summary>
    GroupEnd,
    /// <summary>Control word, e.g. <c>\b</c> or <c>\fs24</c>.</summary>
    ControlWord,
    /// <summary>Control symbol, e.g. <c>\*</c>, <c>\~</c>, <c>\-</c>, <c>\_</c>, <c>\\</c>, <c>\{</c>, <c>\}</c>.</summary>
    ControlSymbol,
    /// <summary>Raw text.</summary>
    Text,
    /// <summary>End of input.</summary>
    Eof,
}

internal readonly struct RtfToken
{
    public RtfTokenType Type { get; }

    /// <summary>Control-word name (without leading backslash) or control-symbol character, or literal text.</summary>
    public string Value { get; }

    /// <summary>Numeric parameter for control words (null when absent).</summary>
    public int? Parameter { get; }

    /// <summary>Byte offset in the source where this token started.</summary>
    public int Position { get; }

    /// <summary>Raw binary payload for <c>\binN</c> control words (null otherwise).</summary>
    public byte[]? BinaryData { get; }

    public RtfToken(RtfTokenType type, string value, int? parameter, int position, byte[]? binaryData = null)
    {
        Type = type;
        Value = value;
        Parameter = parameter;
        Position = position;
        BinaryData = binaryData;
    }

    public override string ToString() =>
        Type switch
        {
            RtfTokenType.ControlWord => Parameter.HasValue ? $"\\{Value}{Parameter.Value}" : $"\\{Value}",
            RtfTokenType.ControlSymbol => $"\\{Value}",
            RtfTokenType.Text => $"\"{Value}\"",
            RtfTokenType.GroupStart => "{",
            RtfTokenType.GroupEnd => "}",
            _ => Type.ToString(),
        };
}
