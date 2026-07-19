// ============================================================================
// Product:   RtfBridge
// File:      EncodingBootstrap.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;

namespace RtfBridge;

/// <summary>
/// Registers the CodePagesEncodingProvider once for the process so that legacy Windows codepages
/// (1250..1258, 932, 936, 949, 950, MacRoman, ...) can be resolved via <see cref="Encoding.GetEncoding(int)"/>
/// on modern .NET where they are not registered by default.
/// </summary>
internal static class EncodingBootstrap
{
    private static int _initialized;
    private static readonly ConcurrentDictionary<int, Encoding> _cache = new ConcurrentDictionary<int, Encoding>();

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 0)
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            }
            catch
            {
                // Provider registration failures are non-fatal — GetEncoding will still throw
                // for unsupported codepages and callers will fall back gracefully.
            }
        }
    }

    public static Encoding GetEncoding(int codePage)
    {
        EnsureRegistered();
        return _cache.GetOrAdd(codePage, Encoding.GetEncoding);
    }
}
