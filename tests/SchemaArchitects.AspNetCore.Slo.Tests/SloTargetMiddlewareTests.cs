using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace SchemaArchitects.AspNetCore.Slo.Tests;

[Collection(SloTargetHookCollection.Name)]
public class SloTargetMiddlewareTests
{
    private async Task<(HttpClient Client, IHost Host)> CreateTestServerAsync()
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();

                        // Programmatic setup equivalent to appsettings.json binding
                        services.AddSloTargets(options =>
                        {
                            options.ApmId = "APM00000001";
                            options.DefaultLatencyMs = 300;
                            options.Endpoints.Add(new SloEndpointTarget
                            {
                                Route = "/api/orders/{id}",
                                TargetMs = 150
                            });
                            options.Endpoints.Add(new SloEndpointTarget
                            {
                                Route = "/api/reports",
                                TargetMs = 800
                            });
                        });
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseSloTargets();

                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet("/api/orders/{id}", () => Results.Ok(new { Status = "Success" }));
                            endpoints.MapGet("/api/reports", () => Results.Ok(new { Report = "Monthly" }));
                            endpoints.MapGet("/api/unconfigured", () => Results.Ok(new { Message = "Using Default" }));
                            endpoints.MapGet("/healthz", () => Results.Ok("healthy"));
                        });
                    });
            })
            .StartAsync();

        return (host.GetTestClient(), host);
    }

    [Fact]
    public async Task Request_ToDynamicRoute_ExtractsRoutePattern_AndCustomTarget()
    {
        string? capturedRoute = null;
        double capturedTargetMs = 0;

        SloTargetMiddleware.OnTargetRecorded = t =>
        {
            capturedRoute = t.Route;
            capturedTargetMs = t.TargetMs;
        };

        var (client, host) = await CreateTestServerAsync();

        // Simulate request with dynamic URL parameter
        var response = await client.GetAsync("/api/orders/ORD-99482");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Verify route template grouping to avoid high cardinality in Dynatrace
        Assert.Equal("/api/orders/{id}", capturedRoute);
        Assert.Equal(150, capturedTargetMs);

        await host.StopAsync();
    }

    [Fact]
    public async Task Request_ToReports_ResolvesConfiguredTarget()
    {
        string? capturedRoute = null;
        double capturedTargetMs = 0;

        SloTargetMiddleware.OnTargetRecorded = t =>
        {
            capturedRoute = t.Route;
            capturedTargetMs = t.TargetMs;
        };

        var (client, host) = await CreateTestServerAsync();

        var response = await client.GetAsync("/api/reports");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/api/reports", capturedRoute);
        Assert.Equal(800, capturedTargetMs);

        await host.StopAsync();
    }

    [Fact]
    public async Task Request_ToUnconfiguredEndpoint_AppliesDefaultLatencyTarget()
    {
        string? capturedRoute = null;
        double capturedTargetMs = 0;

        SloTargetMiddleware.OnTargetRecorded = t =>
        {
            capturedRoute = t.Route;
            capturedTargetMs = t.TargetMs;
        };

        var (client, host) = await CreateTestServerAsync();

        var response = await client.GetAsync("/api/unconfigured");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/api/unconfigured", capturedRoute);
        Assert.Equal(300, capturedTargetMs); // Fallback to DefaultLatencyMs

        await host.StopAsync();
    }

    [Fact]
    public async Task Request_ToExcludedPath_DoesNotTriggerHookMethod()
    {
        bool hookTriggered = false;

        SloTargetMiddleware.OnTargetRecorded = _ =>
        {
            hookTriggered = true;
        };

        var (client, host) = await CreateTestServerAsync();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(hookTriggered, "The excluded endpoint (/healthz) should not trigger the hook method.");

        await host.StopAsync();
    }
}