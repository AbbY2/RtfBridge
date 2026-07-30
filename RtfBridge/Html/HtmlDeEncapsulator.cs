// ============================================================================
// Product:   RtfBridge
// File:      HtmlDeEncapsulator.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using System;
using System.Collections.Generic;
using System.Text;
using RtfBridge.Rtf;

namespace RtfBridge.Html;

/// <summary>
/// Extracts HTML from RTF-encapsulated HTML as produced by Outlook (MS-OXRTFEX):
/// documents flagged with <c>\fromhtml1</c> carry the original HTML markup inside
/// <c>{\*\htmltag N ...}</c> destinations, while RTF-only fallback content is bracketed by
/// <c>\htmlrtf ... \htmlrtf0</c> and must be suppressed.
/// </summary>
internal static class HtmlDeEncapsulator
{
    /// <summary>Returns true when the document header declares <c>\fromhtml1</c>.</summary>
    public static bool IsEncapsulated(List<RtfToken> tokens)
    {
        // \fromhtml1 must appear in the header, before document content.
        int limit = Math.Min(tokens.Count, 512);
        for (int i = 0; i < limit; i++)
        {
            var t = tokens[i];
            if (t.Type == RtfTokenType.ControlWord && t.Value == "fromhtml" && t.Parameter == 1)
                return true;
        }
        return false;
    }

    public static string Extract(List<RtfToken> tokens, int defaultCodePage)
    {
        var sb = new StringBuilder(1024);
        var pending = new List<byte>();
        var stack = new Stack<Frame>();
        var cur = new Frame { Uc = 1 };
        int codePage = defaultCodePage;
        char? highSurrogate = null;
        bool nextIsOptionalDestination = false;
        int pendingSkip = 0; // \u fallback characters still to swallow

        void Emit(string s)
        {
            if (cur.Suppress || cur.SkipGroup || s.Length == 0) return;
            if (cur.InHtmlTag)
            {
                sb.Append(s); // htmltag content IS the original HTML markup — raw
            }
            else
            {
                foreach (char ch in s)
                {
                    switch (ch)
                    {
                        case '&': sb.Append("&amp;"); break;
                        case '<': sb.Append("&lt;"); break;
                        case '>': sb.Append("&gt;"); break;
                        case '\u00A0': sb.Append("&nbsp;"); break;
                        default: sb.Append(ch); break;
                    }
                }
            }
        }

        void Flush()
        {
            if (pending.Count == 0) return;
            byte[] raw = pending.ToArray();
            pending.Clear();
            string decoded;
            try { decoded = EncodingBootstrap.GetEncoding(codePage).GetString(raw); }
            catch
            {
                var f = new StringBuilder(raw.Length);
                foreach (byte b in raw) f.Append((char)b);
                decoded = f.ToString();
            }
            Emit(decoded);
        }

        foreach (var t in tokens)
        {
            switch (t.Type)
            {
                case RtfTokenType.GroupStart:
                    Flush();
                    pendingSkip = 0;
                    stack.Push(cur);
                    cur = cur.Clone();
                    break;

                case RtfTokenType.GroupEnd:
                    Flush();
                    pendingSkip = 0;
                    if (stack.Count > 0) cur = stack.Pop();
                    break;

                case RtfTokenType.ControlWord:
                    if (nextIsOptionalDestination)
                    {
                        nextIsOptionalDestination = false;
                        if (t.Value != "htmltag")
                        {
                            cur.SkipGroup = true;
                            break;
                        }
                    }
                    if (pendingSkip > 0) { pendingSkip--; break; }

                    switch (t.Value)
                    {
                        case "ansicpg":
                            if (t.Parameter.HasValue) codePage = t.Parameter.Value;
                            break;
                        case "htmltag":
                            cur.InHtmlTag = true;
                            break;
                        case "htmlrtf":
                            // \htmlrtf / \htmlrtf1 suppress; \htmlrtf0 re-enables.
                            cur.Suppress = t.Parameter != 0;
                            break;
                        case "uc":
                            cur.Uc = t.Parameter ?? 1;
                            break;
                        case "u":
                            Flush();
                            int cp = t.Parameter ?? 0;
                            if (cp < 0) cp += 0x10000;
                            if (cp >= 0xD800 && cp <= 0xDBFF)
                            {
                                highSurrogate = (char)cp;
                            }
                            else if (cp >= 0xDC00 && cp <= 0xDFFF)
                            {
                                if (highSurrogate.HasValue)
                                {
                                    Emit(new string(new[] { highSurrogate.Value, (char)cp }));
                                    highSurrogate = null;
                                }
                            }
                            else if (cp > 0 && cp <= 0xFFFF)
                            {
                                highSurrogate = null;
                                Emit(((char)cp).ToString());
                            }
                            pendingSkip = cur.Uc;
                            break;
                        case "par":
                        case "line":
                            Flush();
                            Emit("\r\n");
                            break;
                        case "tab":
                            Flush();
                            Emit("\t");
                            break;
                        case "fonttbl":
                        case "colortbl":
                        case "stylesheet":
                        case "info":
                        case "generator":
                        case "listtable":
                        case "listoverridetable":
                        case "pntext":
                        case "pict":
                            cur.SkipGroup = true;
                            break;
                        default:
                            // All other control words are formatting noise for de-encapsulation.
                            break;
                    }
                    break;

                case RtfTokenType.ControlSymbol:
                    switch (t.Value)
                    {
                        case "*":
                            nextIsOptionalDestination = true;
                            break;
                        case "'":
                            if (pendingSkip > 0) { pendingSkip--; break; }
                            if (t.Parameter.HasValue && !cur.Suppress && !cur.SkipGroup)
                                pending.Add((byte)t.Parameter.Value);
                            break;
                        case "\\": Flush(); Emit("\\"); break;
                        case "{":  Flush(); Emit("{");  break;
                        case "}":  Flush(); Emit("}");  break;
                        case "~":  Flush(); Emit(" "); break;
                        default: break;
                    }
                    break;

                case RtfTokenType.Text:
                    {
                        string v = t.Value;
                        int start = 0;
                        if (pendingSkip > 0)
                        {
                            int take = Math.Min(pendingSkip, v.Length);
                            pendingSkip -= take;
                            start = take;
                        }
                        if (!cur.Suppress && !cur.SkipGroup)
                        {
                            for (int i = start; i < v.Length; i++) pending.Add((byte)v[i]);
                        }
                    }
                    break;

                case RtfTokenType.Eof:
                    Flush();
                    break;
            }
        }

        Flush();
        return sb.ToString();
    }

    private struct Frame
    {
        public bool Suppress;
        public bool SkipGroup;
        public bool InHtmlTag;
        public int Uc;

        public Frame Clone() => this; // value type — explicit for clarity at call sites
    }
}
