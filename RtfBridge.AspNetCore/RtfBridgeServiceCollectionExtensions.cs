// ============================================================================
// Product:   RtfBridge.AspNetCore
// File:      RtfBridgeServiceCollectionExtensions.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace RtfBridge.AspNetCore;

/// <summary>
/// Dependency injection extensions for registering <see cref="IRtfBridgeConverter"/> and
/// <see cref="RtfToHtmlOptions"/> with an ASP.NET Core application.
/// </summary>
public static class RtfBridgeServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IRtfBridgeConverter"/> as a singleton along with the supplied
    /// <see cref="RtfToHtmlOptions"/> (also as a singleton). The converter itself is stateless
    /// and safe to share across requests.
    /// </summary>
    public static IServiceCollection AddRtfBridge(
        this IServiceCollection services,
        Action<RtfToHtmlOptions>? configure = null)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));

        var options = new RtfToHtmlOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<IRtfBridgeConverter, RtfBridgeConverter>();
        return services;
    }
}
