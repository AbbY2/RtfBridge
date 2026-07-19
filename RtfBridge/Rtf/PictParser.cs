// ============================================================================
// Product:   RtfBridge
// File:      PictParser.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using System;
using System.Collections.Generic;
using System.Text;
using RtfBridge.Rtf.Model;

namespace RtfBridge.Rtf;

/// <summary>
/// Parses the body of a <c>\pict</c> destination: consumes text tokens as hex nibbles
/// (or a preceding <c>\binN</c> raw payload; those bytes are already skipped by the
/// tokenizer, so here we only see hex-encoded payloads) and sniffs the image format.
/// </summary>
internal static class PictParser
{
    public enum PictKind { Png, Jpeg, EmfBlip, WmfBlip, Dib, Wbitmap, MacPict, Unknown }

    public sealed class PictInfo
    {
        public PictKind Kind { get; set; } = PictKind.Unknown;
        public int WidthPx { get; set; }
        public int HeightPx { get; set; }
        /// <summary>Twips width from \picwgoal (preferred display size).</summary>
        public int GoalWidthTwips { get; set; }
        public int GoalHeightTwips { get; set; }
        /// <summary>Horizontal scale factor in percent (\picscalex), default 100.</summary>
        public int ScaleXPercent { get; set; } = 100;
        /// <summary>Vertical scale factor in percent (\picscaley), default 100.</summary>
        public int ScaleYPercent { get; set; } = 100;
    }

    /// <summary>
    /// Consumes tokens starting immediately after the opening <c>{</c> of a <c>\pict</c> group
    /// (i.e. positioned so the next token is <c>\pict</c> itself or the first inner control word),
    /// through the matching <c>}</c>. Returns the produced <see cref="RtfImageRun"/> and appends
    /// warnings on decode/skip issues.
    /// </summary>
    /// <param name="tokens">Full token list.</param>
    /// <param name="index">Index reference: on entry, points at the first token inside the group. On exit, points at the token AFTER the matching close.</param>
    /// <param name="depth">Depth of the group at entry (>=1). Reads until this depth returns to 0.</param>
    /// <param name="options">Conversion options (max byte limit, etc.).</param>
    /// <param name="warnings">Sink for non-fatal issues.</param>
    public static RtfImageRun? ReadPictGroup(
        List<RtfToken> tokens,
        ref int index,
        int depth,
        RtfToHtmlOptions options,
        List<RtfConversionWarning> warnings)
    {
        var info = new PictInfo();
        var hex = new StringBuilder();
        bool tooLarge = false;
        int approxHalfBytes = 0;
        int maxHalfBytes = options.MaxImageBytes * 2; // 2 hex chars per byte

        while (index < tokens.Count && depth > 0)
        {
            var t = tokens[index++];
            switch (t.Type)
            {
                case RtfTokenType.GroupStart:
                    depth++;
                    break;
                case RtfTokenType.GroupEnd:
                    depth--;
                    break;
                case RtfTokenType.ControlWord:
                    switch (t.Value)
                    {
                        case "pngblip":    info.Kind = PictKind.Png; break;
                        case "jpegblip":   info.Kind = PictKind.Jpeg; break;
                        case "emfblip":    info.Kind = PictKind.EmfBlip; break;
                        case "wmetafile":  info.Kind = PictKind.WmfBlip; break;
                        case "dibitmap":   info.Kind = PictKind.Dib; break;
                        case "wbitmap":    info.Kind = PictKind.Wbitmap; break;
                        case "macpict":    info.Kind = PictKind.MacPict; break;
                        case "picw":       if (t.Parameter.HasValue) info.WidthPx = t.Parameter.Value; break;
                        case "pich":       if (t.Parameter.HasValue) info.HeightPx = t.Parameter.Value; break;
                        case "picwgoal":   if (t.Parameter.HasValue) info.GoalWidthTwips = t.Parameter.Value; break;
                        case "pichgoal":   if (t.Parameter.HasValue) info.GoalHeightTwips = t.Parameter.Value; break;
                        case "picscalex":  if (t.Parameter.HasValue && t.Parameter.Value > 0) info.ScaleXPercent = t.Parameter.Value; break;
                        case "picscaley":  if (t.Parameter.HasValue && t.Parameter.Value > 0) info.ScaleYPercent = t.Parameter.Value; break;
                    }
                    break;
                case RtfTokenType.Text:
                    if (!tooLarge)
                    {
                        foreach (char c in t.Value)
                        {
                            if (IsHex(c))
                            {
                                if (approxHalfBytes >= maxHalfBytes) { tooLarge = true; break; }
                                hex.Append(c);
                                approxHalfBytes++;
                            }
                        }
                    }
                    break;
            }
        }

        if (tooLarge)
        {
            warnings.Add(new RtfConversionWarning(
                "IMG_TOO_LARGE",
                $"Embedded image exceeds MaxImageBytes ({options.MaxImageBytes}); replaced with placeholder."));
            return new RtfImageRun { IsPlaceholder = true };
        }

        byte[] bytes = HexToBytes(hex);

        // Sniff magic bytes for PNG/JPEG/BMP regardless of the declared blip flag — some producers lie.
        string sniffed = SniffMediaType(bytes);
        string mediaType = sniffed;

        if (mediaType.Length == 0)
        {
            switch (info.Kind)
            {
                case PictKind.Png:  mediaType = "image/png"; break;
                case PictKind.Jpeg: mediaType = "image/jpeg"; break;
                case PictKind.Dib:  mediaType = "image/bmp"; bytes = WrapDibAsBmp(bytes); break;
                case PictKind.Wbitmap: mediaType = "image/bmp"; bytes = WrapDibAsBmp(bytes); break;
                default: break;
            }
        }

        if (mediaType.Length == 0 || bytes.Length == 0)
        {
            // Try the pluggable vector renderer for WMF/EMF/MacPict.
            if (options.VectorImageRenderer != null && bytes.Length > 0)
            {
                RtfImageKind pubKind = info.Kind switch
                {
                    PictKind.EmfBlip => RtfImageKind.Emf,
                    PictKind.WmfBlip => RtfImageKind.Wmf,
                    PictKind.MacPict => RtfImageKind.PmMetafile,
                    _ => RtfImageKind.Unknown,
                };
                if (options.VectorImageRenderer.CanRender(pubKind))
                {
                    try
                    {
                        var rendered = options.VectorImageRenderer.Render(bytes, pubKind);
                        if (rendered != null && rendered.Bytes.Length > 0)
                        {
                            return new RtfImageRun
                            {
                                Bytes = rendered.Bytes,
                                MediaType = rendered.MediaType,
                                WidthPx = ApplyScale(rendered.Width > 0 ? rendered.Width : EffectiveWidthPx(info), info.ScaleXPercent),
                                HeightPx = ApplyScale(rendered.Height > 0 ? rendered.Height : EffectiveHeightPx(info), info.ScaleYPercent),
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        warnings.Add(new RtfConversionWarning(
                            "IMG_RENDER_FAILED",
                            $"Vector renderer failed for '{info.Kind}': {ex.Message}"));
                    }
                }
            }

            warnings.Add(new RtfConversionWarning(
                "IMG_UNSUPPORTED",
                $"Embedded picture kind '{info.Kind}' is not supported without a vector renderer; placeholder emitted."));
            return new RtfImageRun
            {
                IsPlaceholder = true,
                WidthPx = ApplyScale(EffectiveWidthPx(info), info.ScaleXPercent),
                HeightPx = ApplyScale(EffectiveHeightPx(info), info.ScaleYPercent),
            };
        }

        return new RtfImageRun
        {
            Bytes = bytes,
            MediaType = mediaType,
            WidthPx = ApplyScale(EffectiveWidthPx(info), info.ScaleXPercent),
            HeightPx = ApplyScale(EffectiveHeightPx(info), info.ScaleYPercent),
        };
    }

    // Prefer \picwgoal (twips) — that is the *display* size. Fall back to \picw (already px).
    private static int EffectiveWidthPx(PictInfo info) =>
        info.GoalWidthTwips > 0 ? TwipsToPx(info.GoalWidthTwips) : info.WidthPx;

    private static int EffectiveHeightPx(PictInfo info) =>
        info.GoalHeightTwips > 0 ? TwipsToPx(info.GoalHeightTwips) : info.HeightPx;

    private static int ApplyScale(int px, int percent)
    {
        if (px <= 0) return 0;
        if (percent <= 0 || percent == 100) return px;
        return (int)Math.Round(px * (percent / 100.0));
    }

    private static int TwipsToPx(int twips) => twips > 0 ? (int)Math.Round(twips / 15.0) : 0;

    private static bool IsHex(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    private static byte[] HexToBytes(StringBuilder hex)
    {
        int len = hex.Length & ~1; // ignore trailing odd nibble
        if (len == 0) return Array.Empty<byte>();
        var buf = new byte[len / 2];
        for (int i = 0; i < len; i += 2)
        {
            int hi = FromHex(hex[i]);
            int lo = FromHex(hex[i + 1]);
            buf[i / 2] = (byte)((hi << 4) | lo);
        }
        return buf;
    }

    private static int FromHex(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        return c - 'A' + 10;
    }

    private static string SniffMediaType(byte[] b)
    {
        if (b.Length >= 8 &&
            b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 &&
            b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A)
            return "image/png";
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
            return "image/jpeg";
        if (b.Length >= 2 && b[0] == (byte)'B' && b[1] == (byte)'M')
            return "image/bmp";
        if (b.Length >= 6 &&
            (b[0] == (byte)'G' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'8'))
            return "image/gif";
        return "";
    }

    /// <summary>
    /// A DIB has no 14-byte BMP file header. Wrap it so browsers can render it as image/bmp.
    /// </summary>
    private static byte[] WrapDibAsBmp(byte[] dib)
    {
        if (dib.Length < 4) return Array.Empty<byte>();

        // Read biSize (first DWORD of BITMAPINFOHEADER-style DIB).
        int headerSize = dib[0] | (dib[1] << 8) | (dib[2] << 16) | (dib[3] << 24);
        if (headerSize < 12 || headerSize > dib.Length) return Array.Empty<byte>();

        int fileSize = 14 + dib.Length;
        int pixelOffset = 14 + headerSize; // ignore palette-precise sizing (browsers tolerate)

        var buf = new byte[fileSize];
        buf[0] = (byte)'B'; buf[1] = (byte)'M';
        WriteI32(buf, 2, fileSize);
        WriteI32(buf, 6, 0);
        WriteI32(buf, 10, pixelOffset);
        Buffer.BlockCopy(dib, 0, buf, 14, dib.Length);
        return buf;
    }

    private static void WriteI32(byte[] buf, int offset, int value)
    {
        buf[offset]     = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        buf[offset + 2] = (byte)((value >> 16) & 0xFF);
        buf[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
