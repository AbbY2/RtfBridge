// ============================================================================
// Product:   RtfBridge.AspNetCore
// File:      RtfBridgeEndpointRouteBuilderExtensions.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace RtfBridge.AspNetCore;

/// <summary>
/// Endpoint routing extensions for exposing the RTF→HTML conversion service over HTTP.
/// </summary>
public static class RtfBridgeEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps a POST endpoint that accepts an RTF payload (request body) and returns HTML.
    /// Accepts <c>Content-Type: application/rtf</c>, <c>text/rtf</c>, or <c>text/plain</c>.
    /// Returns <c>text/html; charset=utf-8</c>. Use the query string flag <c>?fullDocument=false</c>
    /// to emit an HTML fragment instead of a full document.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">Route pattern. Defaults to <c>/convert/rtf-to-html</c>.</param>
    /// <returns>The convention builder for the mapped endpoint.</returns>
    public static IEndpointConventionBuilder MapRtfToHtml(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/convert/rtf-to-html")
    {
        if (endpoints == null) throw new ArgumentNullException(nameof(endpoints));
        if (string.IsNullOrWhiteSpace(pattern)) throw new ArgumentException("Pattern must not be empty.", nameof(pattern));

        return endpoints.MapPost(pattern, HandleAsync);
    }

    private static async Task HandleAsync(HttpContext context)
    {
        var converter = context.RequestServices.GetRequiredService<IRtfBridgeConverter>();
        var baseOptions = context.RequestServices.GetRequiredService<RtfToHtmlOptions>();

        // Per-request override for fullDocument via query string.
        var options = new RtfToHtmlOptions
        {
            EmitFullDocument = baseOptions.EmitFullDocument,
            InlineStyles = baseOptions.InlineStyles,
            PreserveListNesting = baseOptions.PreserveListNesting,
            RenderHeadersFooters = baseOptions.RenderHeadersFooters,
            RenderFootnotesAsList = baseOptions.RenderFootnotesAsList,
            RenderPageBreaks = baseOptions.RenderPageBreaks,
            DeterministicOutput = baseOptions.DeterministicOutput,
            DefaultCodePage = baseOptions.DefaultCodePage,
            Sanitization = baseOptions.Sanitization,
            VectorImageRenderer = baseOptions.VectorImageRenderer,
            MaxImageBytes = baseOptions.MaxImageBytes,
        };
        if (context.Request.Query.TryGetValue("fullDocument", out var flag)
            && bool.TryParse(flag, out var full))
        {
            options.EmitFullDocument = full;
        }

        // Read request body as bytes so \pict binary/hex payloads survive intact.
        using var ms = new MemoryStream();
        await context.Request.Body.CopyToAsync(ms, context.RequestAborted).ConfigureAwait(false);
        var bytes = ms.ToArray();

        if (bytes.Length == 0)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Empty RTF payload.", context.RequestAborted).ConfigureAwait(false);
            return;
        }

        string html;
        try
        {
            using var input = new MemoryStream(bytes, writable: false);
            html = converter.ConvertRtfToHtml(input, options);
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            await context.Response.WriteAsync("RTF conversion failed: " + ex.Message, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(html, context.RequestAborted).ConfigureAwait(false);
    }
}
