using System.Text;
using SchemaArchitects.AspNetCore.Slo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace SchemaArchitects.AspNetCore.Slo.Tests;

public class SloConfigurationBindingTests
{
    private const string SampleAppSettingsJson = """
    {
      "SloTargets": {
        "ApmId": "APM00000001",
        "DefaultLatencyMs": 250.0,
        "AdditionalExcludedPaths": [
          "/swagger",
          "/internal/ping",
          "/metrics"
        ],
        "Endpoints": [
          {
            "Route": "/api/orders/{id}",
            "TargetMs": 120.0
          },
          {
            "Route": "/api/reports",
            "TargetMs": 750.0
          },
          {
            "Method": "POST",
            "Route": "/catalogsvc/v1/shipProduct",
            "TargetMs": 400.0
          }
        ]
      }
    }
    """;

    private static IConfiguration BuildConfigurationFromJson(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        
        return new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();
    }

    [Fact]
    public void AddSloTargets_BindsConfigurationViaDependencyInjection()
    {
        // Arrange
        var configuration = BuildConfigurationFromJson(SampleAppSettingsJson);
        var services = new ServiceCollection();

        // Act: Register using the extension method microservices will call
        services.AddSloTargets(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<SloOptions>>().Value;

        // Assert: Scalar and list values bound correctly
        Assert.Equal("APM00000001", options.ApmId);
        Assert.Equal(250.0, options.DefaultLatencyMs);
        Assert.Equal(3, options.Endpoints.Count);
        Assert.Contains(options.Endpoints, e => e.Route == "/api/orders/{id}" && e.TargetMs == 120.0 && e.Method == null);
        Assert.Contains(options.Endpoints, e => e.Route == "/api/reports" && e.TargetMs == 750.0);
        Assert.Contains(options.Endpoints, e => e.Method == "POST" && e.Route == "/catalogsvc/v1/shipProduct" && e.TargetMs == 400.0);

        // Assert: Custom exclusion paths bound from JSON
        Assert.Equal(3, options.AdditionalExcludedPaths.Count);
        Assert.Contains("/swagger", options.AdditionalExcludedPaths);
        Assert.Contains("/internal/ping", options.AdditionalExcludedPaths);
    }

    [Fact]
    public void GetEffectiveExcludedPaths_MergesDefaultProbesWithConfiguredPaths_CaseInsensitive()
    {
        // Arrange
        var configuration = BuildConfigurationFromJson(SampleAppSettingsJson);
        var options = configuration.GetSection(SloOptions.SectionName).Get<SloOptions>() 
                      ?? new SloOptions();

        // Act
        var effectivePaths = options.GetEffectiveExcludedPaths();

        // Assert: Baseline infrastructure probes are preserved
        Assert.Contains("/health", effectivePaths);
        Assert.Contains("/healthz", effectivePaths);
        Assert.Contains("/ready", effectivePaths);
        Assert.Contains("/live", effectivePaths);

        // Assert: Dynamic paths from JSON are incorporated
        Assert.Contains("/swagger", effectivePaths);
        Assert.Contains("/internal/ping", effectivePaths);
        Assert.Contains("/metrics", effectivePaths);

        // Assert: Case-insensitivity verification
        Assert.Contains("/SWAGGER", effectivePaths);
        Assert.Contains("/HEALTHZ", effectivePaths);
    }

    [Fact]
    public void AddSloTargets_MissingSection_FailsBecauseApmIdIsRequired()
    {
        // Arrange: Configuration with no "SloTargets" section
        var configuration = BuildConfigurationFromJson("{}");
        var services = new ServiceCollection();

        // Act
        services.AddSloTargets(configuration);

        using var serviceProvider = services.BuildServiceProvider();

        // Assert: a service that installs the package but forgets its config fails fast
        var ex = Assert.Throws<OptionsValidationException>(
            () => serviceProvider.GetRequiredService<IOptions<SloOptions>>().Value);
        Assert.Contains(ex.Failures, f => f.Contains("ApmId is required"));
    }

    [Fact]
    public void AddSloTargets_OnlyApmId_FallsBackToDefaults()
    {
        // Arrange: Minimal section with only the required ApmId
        var configuration = BuildConfigurationFromJson("""{ "SloTargets": { "ApmId": "APM00000001" } }""");
        var services = new ServiceCollection();

        // Act
        services.AddSloTargets(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<SloOptions>>().Value;

        // Assert: Defaults remain intact
        Assert.Equal(300.0, options.DefaultLatencyMs);
        Assert.Empty(options.Endpoints);
        Assert.Empty(options.AdditionalExcludedPaths);

        // Core probes still exist in the effective lookup set
        var effectivePaths = options.GetEffectiveExcludedPaths();
        Assert.Contains("/healthz", effectivePaths);
    }
}