using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SchemaArchitects.AspNetCore.Slo.Tests;

public class SloOptionsValidationTests
{
    private const string ApmId = "APM00000001";

    private static SloOptions Resolve(Action<SloOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddSloTargets(o =>
        {
            o.ApmId = ApmId; // valid baseline; individual tests override what they exercise
            configure(o);
        });
        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<SloOptions>>().Value;
    }

    private static OptionsValidationException ResolveExpectingFailure(Action<SloOptions> configure) =>
        Assert.Throws<OptionsValidationException>(() => Resolve(configure));

    [Fact]
    public void ValidConfiguration_Passes()
    {
        var options = Resolve(o =>
        {
            o.Endpoints.Add(new SloEndpointTarget { Route = "/api/orders/{id}", TargetMs = 150 });
            o.Endpoints.Add(new SloEndpointTarget { Route = "/api/orders", TargetMs = 200 });
        });

        Assert.Equal(2, options.Endpoints.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingApmId_Fails(string apmId)
    {
        var ex = ResolveExpectingFailure(o => o.ApmId = apmId);

        Assert.Contains(ex.Failures, f => f.Contains("ApmId is required"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public void NonPositiveDefaultLatency_Fails(double defaultLatencyMs)
    {
        var ex = ResolveExpectingFailure(o => o.DefaultLatencyMs = defaultLatencyMs);

        Assert.Contains(ex.Failures, f => f.Contains("DefaultLatencyMs"));
    }

    [Fact]
    public void NonPositiveEndpointTarget_Fails()
    {
        var ex = ResolveExpectingFailure(o =>
            o.Endpoints.Add(new SloEndpointTarget { Route = "/api/orders", TargetMs = 0 }));

        Assert.Contains(ex.Failures, f => f.Contains("Endpoints[0]:TargetMs") && f.Contains("/api/orders"));
    }

    [Fact]
    public void MissingRoute_Fails()
    {
        var ex = ResolveExpectingFailure(o =>
            o.Endpoints.Add(new SloEndpointTarget { Route = " ", TargetMs = 100 }));

        Assert.Contains(ex.Failures, f => f.Contains("Endpoints[0]:Route is required"));
    }

    [Fact]
    public void InvalidHttpMethod_Fails()
    {
        var ex = ResolveExpectingFailure(o =>
            o.Endpoints.Add(new SloEndpointTarget { Method = "POTS", Route = "/api/orders", TargetMs = 100 }));

        Assert.Contains(ex.Failures, f => f.Contains("Endpoints[0]:Method 'POTS' is not a valid HTTP method"));
    }

    [Fact]
    public void RoutesThatNormalizeToTheSameTemplate_FailAsDuplicates()
    {
        var ex = ResolveExpectingFailure(o =>
        {
            o.Endpoints.Add(new SloEndpointTarget { Route = "/api/orders/{id}", TargetMs = 150 });
            o.Endpoints.Add(new SloEndpointTarget { Route = "API/Orders/{id:int}", TargetMs = 999 });
        });

        Assert.Contains(ex.Failures, f => f.Contains("Endpoints[1]") && f.Contains("duplicates"));
    }

    [Fact]
    public void SameMethodAndRoute_FailAsDuplicates_RegardlessOfCasing()
    {
        var ex = ResolveExpectingFailure(o =>
        {
            o.Endpoints.Add(new SloEndpointTarget { Method = "POST", Route = "/catalogsvc/v1/shipProduct", TargetMs = 400 });
            o.Endpoints.Add(new SloEndpointTarget { Method = "post", Route = "catalogsvc/v1/shipproduct", TargetMs = 500 });
        });

        Assert.Contains(ex.Failures, f => f.Contains("Endpoints[1]") && f.Contains("duplicates"));
    }

    [Fact]
    public void SameRouteWithDifferentMethods_IsValid()
    {
        var options = Resolve(o =>
        {
            o.Endpoints.Add(new SloEndpointTarget { Method = "GET", Route = "/api/orders", TargetMs = 200 });
            o.Endpoints.Add(new SloEndpointTarget { Method = "POST", Route = "/api/orders", TargetMs = 500 });
            o.Endpoints.Add(new SloEndpointTarget { Route = "/api/orders", TargetMs = 300 }); // any other method
        });

        Assert.Equal(3, options.Endpoints.Count);
    }

    [Fact]
    public void AllFailuresAreReportedTogether()
    {
        var ex = ResolveExpectingFailure(o =>
        {
            o.ApmId = "";
            o.DefaultLatencyMs = 0;
            o.Endpoints.Add(new SloEndpointTarget { Route = "", TargetMs = 100 });
            o.Endpoints.Add(new SloEndpointTarget { Route = "/api/x", TargetMs = -1 });
        });

        Assert.Equal(4, ex.Failures.Count());
    }

    [Fact]
    public async Task InvalidConfiguration_FailsApplicationStartup()
    {
        var builder = new HostBuilder().ConfigureWebHost(webBuilder =>
            webBuilder.UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSloTargets(o => o.DefaultLatencyMs = 100); // no ApmId
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseSloTargets();
                }));

        var ex = await Assert.ThrowsAsync<OptionsValidationException>(() => builder.StartAsync());
        Assert.Contains(ex.Failures, f => f.Contains("ApmId is required"));
    }
}
