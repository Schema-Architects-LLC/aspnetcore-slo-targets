using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SchemaArchitects.AspNetCore.Slo.Tests;

[ApiController]
[Route("api/[controller]")]
public class InvoicesController : ControllerBase
{
    [HttpGet("{id:int}")]
    public IActionResult GetById(int id) => Ok(new { id });

    [HttpGet]
    public IActionResult List() => Ok(Array.Empty<int>());
}

/// <summary>
/// Controller route templates have no leading slash and keep their constraints
/// ("api/Invoices/{id:int}"), unlike minimal APIs. Configuration written the README way must still match.
/// </summary>
[Collection(SloTargetHookCollection.Name)]
public class SloControllerRouteTests
{
    private static async Task<IHost> CreateControllerHostAsync()
    {
        return await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddControllers().AddApplicationPart(typeof(InvoicesController).Assembly);

                        services.AddSloTargets(options =>
                        {
                            options.ApmId = "APM00000001";
                            options.DefaultLatencyMs = 300;
                            options.Endpoints.Add(new SloEndpointTarget { Route = "/api/invoices/{id}", TargetMs = 150 });
                            options.Endpoints.Add(new SloEndpointTarget { Route = "api/invoices", TargetMs = 400 });
                        });
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseSloTargets();
                        app.UseEndpoints(endpoints => endpoints.MapControllers());
                    });
            })
            .StartAsync();
    }

    [Fact]
    public async Task ControllerRoute_WithConstraint_MatchesConfiguredTarget()
    {
        string? capturedRoute = null;
        double capturedTargetMs = 0;

        SloTargetMiddleware.OnTargetRecorded = t =>
        {
            capturedRoute = t.Route;
            capturedTargetMs = t.TargetMs;
        };

        using var host = await CreateControllerHostAsync();

        var response = await host.GetTestClient().GetAsync("/api/invoices/42");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Reported in the same canonical form as minimal API routes
        Assert.Equal("/api/Invoices/{id}", capturedRoute);
        Assert.Equal(150, capturedTargetMs);
    }

    [Fact]
    public async Task ControllerRoute_ConfiguredWithoutLeadingSlash_MatchesConfiguredTarget()
    {
        double capturedTargetMs = 0;

        SloTargetMiddleware.OnTargetRecorded = t =>
        {
            capturedTargetMs = t.TargetMs;
        };

        using var host = await CreateControllerHostAsync();

        var response = await host.GetTestClient().GetAsync("/api/invoices");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(400, capturedTargetMs);
    }
}
