using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SchemaArchitects.AspNetCore.Slo.Tests;

/// <summary>
/// Exclusions, casing, unmatched requests, HTTP method matching and APM ID reporting.
/// </summary>
[Collection(SloTargetHookCollection.Name)]
public class SloRequestMatchingTests
{
    private const string ApmId = "APM00000001";

    private static async Task<IHost> CreateHostAsync()
    {
        return await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddSloTargets(options =>
                        {
                            options.ApmId = ApmId;
                            options.DefaultLatencyMs = 300;
                            options.AdditionalExcludedPaths.Add("/swagger");

                            // POST-only targets
                            options.Endpoints.Add(new SloEndpointTarget { Method = "POST", Route = "/catalogsvc/v1/updateProductStatus", TargetMs = 150 });
                            options.Endpoints.Add(new SloEndpointTarget { Method = "POST", Route = "/catalogsvc/v1/infoProduct", TargetMs = 300 });
                            options.Endpoints.Add(new SloEndpointTarget { Method = "POST", Route = "/catalogsvc/v1/shipProduct", TargetMs = 400 });

                            // Same route, different methods, plus a method-less fallback
                            options.Endpoints.Add(new SloEndpointTarget { Method = "GET", Route = "/api/orders", TargetMs = 200 });
                            options.Endpoints.Add(new SloEndpointTarget { Method = "post", Route = "/api/orders", TargetMs = 500 });
                            options.Endpoints.Add(new SloEndpointTarget { Route = "/api/orders", TargetMs = 250 });

                            options.Endpoints.Add(new SloEndpointTarget { Route = "/api/reports", TargetMs = 800 });
                        });
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseSloTargets();

                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapPost("/catalogsvc/v1/updateProductStatus", () => Results.Ok());
                            endpoints.MapPost("/catalogsvc/v1/infoProduct", () => Results.Ok());
                            endpoints.MapPost("/catalogsvc/v1/shipProduct", () => Results.Ok());
                            endpoints.MapGet("/catalogsvc/v1/shipProduct", () => Results.Ok());

                            endpoints.MapMethods("/api/orders", new[] { "GET", "POST", "DELETE" }, () => Results.Ok());
                            endpoints.MapGet("/api/reports", () => Results.Ok());

                            endpoints.MapGet("/healthz/details", () => Results.Ok("healthy"));
                            endpoints.MapGet("/swagger/index.html", () => Results.Ok("docs"));
                            endpoints.MapGet("/swaggerish", () => Results.Ok("not docs"));
                        });
                    });
            })
            .StartAsync();
    }

    private static async Task<SloRecordedTarget?> SendAsync(HttpMethod method, string path)
    {
        SloRecordedTarget? recorded = null;
        SloTargetMiddleware.OnTargetRecorded = t => recorded = t;

        using var host = await CreateHostAsync();
        var response = await host.GetTestClient().SendAsync(new HttpRequestMessage(method, path));

        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound,
            $"Unexpected status {response.StatusCode} for {method} {path}");
        return recorded;
    }

    // ---- HTTP method + APM ID ----

    [Theory]
    [InlineData("/catalogsvc/v1/updateProductStatus", 150)]
    [InlineData("/catalogsvc/v1/infoProduct", 300)]
    [InlineData("/catalogsvc/v1/shipProduct", 400)]
    public async Task PostTarget_IsRecordedWithApmIdAndMethod(string path, double expectedTargetMs)
    {
        var recorded = await SendAsync(HttpMethod.Post, path);

        Assert.Equal(new SloRecordedTarget(ApmId, "POST", path, expectedTargetMs), recorded);
    }

    [Fact]
    public async Task PostOnlyTarget_DoesNotApplyToOtherMethods()
    {
        var recorded = await SendAsync(HttpMethod.Get, "/catalogsvc/v1/shipProduct");

        Assert.Equal("GET", recorded?.Method);
        Assert.Equal(300, recorded?.TargetMs); // DefaultLatencyMs, not the POST target of 400
    }

    [Theory]
    [InlineData("GET", 200)]     // method-specific
    [InlineData("POST", 500)]    // method-specific, configured in lowercase
    [InlineData("DELETE", 250)]  // falls back to the method-less target for the route
    public async Task SameRoute_ResolvesTargetByMethod(string method, double expectedTargetMs)
    {
        var recorded = await SendAsync(new HttpMethod(method), "/api/orders");

        Assert.Equal(method, recorded?.Method);
        Assert.Equal(expectedTargetMs, recorded?.TargetMs);
    }

    [Fact]
    public async Task MethodlessTarget_AppliesToAnyMethod()
    {
        var recorded = await SendAsync(HttpMethod.Get, "/api/reports");

        Assert.Equal(new SloRecordedTarget(ApmId, "GET", "/api/reports", 800), recorded);
    }

    // ---- Exclusions, casing, unmatched ----

    [Theory]
    [InlineData("/swagger/index.html")]   // child of a configured exclusion
    [InlineData("/SWAGGER/INDEX.HTML")]   // case-insensitive
    [InlineData("/healthz/details")]      // child of a default probe
    public async Task Request_BeneathExcludedPath_IsNotRecorded(string path)
    {
        Assert.Null(await SendAsync(HttpMethod.Get, path));
    }

    [Fact]
    public async Task Request_ToPathSharingExclusionPrefix_IsStillRecorded()
    {
        // "/swaggerish" starts with "/swagger" but is not a sub-segment of it
        var recorded = await SendAsync(HttpMethod.Get, "/swaggerish");

        Assert.Equal("/swaggerish", recorded?.Route);
    }

    [Fact]
    public async Task Request_WithDifferentCasing_ResolvesConfiguredTarget()
    {
        var recorded = await SendAsync(HttpMethod.Post, "/CATALOGSVC/V1/SHIPPRODUCT");

        Assert.Equal(400, recorded?.TargetMs);
    }

    [Fact]
    public async Task Request_ToUnmatchedPath_IsNotRecorded()
    {
        // No endpoint matches, so recording the raw path would explode cardinality
        Assert.Null(await SendAsync(HttpMethod.Get, "/wp-admin/random-scanner-path-8f3a2c"));
    }
}
