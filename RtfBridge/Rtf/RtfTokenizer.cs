// ============================================================================
// Product:   RtfBridge
// File:      RtfTokenizer.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using System;
using System.Collections.Generic;
using System.Text;

namespace RtfBridge.Rtf;

/// <summary>
/// Streaming RTF tokenizer. Produces <see cref="RtfToken"/>s from an RTF byte payload.
/// Reads bytes (not chars) because RTF is a 7-bit ASCII envelope with codepage/Unicode escapes
/// (<c>\'hh</c> and <c>\uN?</c>) that decode to text.
/// </summary>
internal sealed class RtfTokenizer
{
    private readonly byte[] _data;
    private int _pos;

    public RtfTokenizer(byte[] data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public IEnumerable<RtfToken> Tokenize()
    {
        while (_pos < _data.Length)
        {
            byte b = _data[_pos];

            if (b == (byte)'{')
            {
                yield return new RtfToken(RtfTokenType.GroupStart, "{", null, _pos);
                _pos++;
            }
            else if (b == (byte)'}')
            {
                yield return new RtfToken(RtfTokenType.GroupEnd, "}", null, _pos);
                _pos++;
            }
            else if (b == (byte)'\\')
            {
                yield return ReadControl();
            }
            else if (b == (byte)'\r' || b == (byte)'\n')
            {
                // CR/LF outside a control sequence are ignored per RTF spec.
                _pos++;
            }
            else
            {
                yield return ReadText();
            }
        }

        yield return new RtfToken(RtfTokenType.Eof, "", null, _pos);
    }

    private RtfToken ReadControl()
    {
        int start = _pos;
        _pos++; // consume '\'

        if (_pos >= _data.Length)
        {
            return new RtfToken(RtfTokenType.ControlSymbol, "", null, start);
        }

        byte first = _data[_pos];

        // Control word: '\' followed by ASCII letters, optional signed integer, optional single space delimiter.
        if (IsAsciiLetter(first))
        {
            var name = new StringBuilder(16);
            while (_pos < _data.Length && IsAsciiLetter(_data[_pos]))
            {
                name.Append((char)_data[_pos]);
                _pos++;
            }

            int? parameter = null;
            if (_pos < _data.Length && (_data[_pos] == (byte)'-' || IsAsciiDigit(_data[_pos])))
            {
                int numStart = _pos;
                if (_data[_pos] == (byte)'-') _pos++;
                while (_pos < _data.Length && IsAsciiDigit(_data[_pos])) _pos++;
                var slice = new string(BytesToChars(_data, numStart, _pos - numStart));
                if (int.TryParse(slice, out int p)) parameter = p;
            }

            // Optional single space delimiter (consumed as part of the control word).
            if (_pos < _data.Length && _data[_pos] == (byte)' ') _pos++;

            string cw = name.ToString();

            // \binN — the next N bytes are a raw binary payload. Capture them on the token
            // (consumers like PictParser use them); they must not be re-tokenized as
            // text/control content.
            if (cw == "bin" && parameter.HasValue && parameter.Value > 0)
            {
                int take = parameter.Value;
                int available = _data.Length - _pos;
                if (take > available) take = available;
                var payload = new byte[take];
                Array.Copy(_data, _pos, payload, 0, take);
                _pos += take;
                return new RtfToken(RtfTokenType.ControlWord, cw, parameter, start, payload);
            }

            return new RtfToken(RtfTokenType.ControlWord, cw, parameter, start);
        }

        // Control symbol: '\' followed by a single non-alphanumeric char (or a special escape).
        char sym = (char)first;
        _pos++;

        // \'hh — hex byte escape. Treat as ControlSymbol with Value="'" and Parameter=byte value.
        if (sym == '\'')
        {
            if (_pos + 1 < _data.Length &&
                TryHex(_data[_pos], out int hi) &&
                TryHex(_data[_pos + 1], out int lo))
            {
                _pos += 2;
                return new RtfToken(RtfTokenType.ControlSymbol, "'", (hi << 4) | lo, start);
            }
            return new RtfToken(RtfTokenType.ControlSymbol, "'", null, start);
        }

        return new RtfToken(RtfTokenType.ControlSymbol, sym.ToString(), null, start);
    }

    private RtfToken ReadText()
    {
        int start = _pos;
        var sb = new StringBuilder();
        while (_pos < _data.Length)
        {
            byte b = _data[_pos];
            if (b == (byte)'\\' || b == (byte)'{' || b == (byte)'}') break;
            if (b == (byte)'\r' || b == (byte)'\n') { _pos++; continue; }
            sb.Append((char)b);
            _pos++;
        }
        return new RtfToken(RtfTokenType.Text, sb.ToString(), null, start);
    }

    private static bool IsAsciiLetter(byte b) => (b >= (byte)'a' && b <= (byte)'z') || (b >= (byte)'A' && b <= (byte)'Z');
    private static bool IsAsciiDigit(byte b) => b >= (byte)'0' && b <= (byte)'9';

    private static bool TryHex(byte b, out int value)
    {
        if (b >= (byte)'0' && b <= (byte)'9') { value = b - (byte)'0'; return true; }
        if (b >= (byte)'a' && b <= (byte)'f') { value = b - (byte)'a' + 10; return true; }
        if (b >= (byte)'A' && b <= (byte)'F') { value = b - (byte)'A' + 10; return true; }
        value = 0;
        return false;
    }

    private static char[] BytesToChars(byte[] src, int start, int len)
    {
        var chars = new char[len];
        for (int i = 0; i < len; i++) chars[i] = (char)src[start + i];
        return chars;
    }
}
