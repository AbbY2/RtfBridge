// ============================================================================
// Product:   RtfBridge.AspNetCore.Tests
// File:      EndpointTests.cs
// Author:    Amit Tiwari
// Company:   Amit Tiwari
// Copyright: Copyright (c) 2026 Amit Tiwari. All rights reserved.
// Version:   2.0
// License:   MIT
// ============================================================================
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using RtfBridge;
using RtfBridge.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace RtfBridgeAspNetCoreTests;

public class EndpointTests
{
    private static TestServer CreateServer()
    {
        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddRtfBridge(o => o.EmitFullDocument = true);
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(e => e.MapRtfToHtml());
            });
        });
        var host = builder.Start();
        return host.GetTestServer();
    }

    [Fact]
    public async Task Post_ValidRtf_ReturnsHtmlDocument()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        var rtf = "{\\rtf1\\ansi Hello \\b World\\b0 !}";
        var content = new ByteArrayContent(Encoding.ASCII.GetBytes(rtf));
        content.Headers.Add("Content-Type", "application/rtf");

        var resp = await client.PostAsync("/convert/rtf-to-html", content);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("text/html", resp.Content.Headers.ContentType?.MediaType);

        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("<html", body);
        Assert.Contains("Hello", body);
        Assert.Contains("World", body);
    }

    [Fact]
    public async Task Post_EmptyBody_Returns400()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        var resp = await client.PostAsync("/convert/rtf-to-html", new ByteArrayContent(System.Array.Empty<byte>()));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Post_FragmentOverride_ReturnsFragment()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        var rtf = "{\\rtf1\\ansi hi}";
        var content = new ByteArrayContent(Encoding.ASCII.GetBytes(rtf));
        content.Headers.Add("Content-Type", "application/rtf");

        var resp = await client.PostAsync("/convert/rtf-to-html?fullDocument=false", content);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("<!DOCTYPE", body);
        Assert.Contains("hi", body);
    }
}

public class DiTests
{
    [Fact]
    public void AddRtfBridge_RegistersSingletons()
    {
        var services = new ServiceCollection();
        services.AddRtfBridge(o => o.EmitFullDocument = false);
        var sp = services.BuildServiceProvider();

        var c1 = sp.GetRequiredService<IRtfBridgeConverter>();
        var c2 = sp.GetRequiredService<IRtfBridgeConverter>();
        Assert.Same(c1, c2);

        var opts = sp.GetRequiredService<RtfToHtmlOptions>();
        Assert.False(opts.EmitFullDocument);
    }
}
