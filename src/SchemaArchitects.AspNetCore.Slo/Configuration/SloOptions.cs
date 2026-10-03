namespace SchemaArchitects.AspNetCore.Slo;

/// <summary>
/// Latency SLO targets for a service, bound from the "SloTargets" section of appsettings.json.
/// </summary>
public class SloOptions
{
    /// <summary>
    /// Configuration section name ("SloTargets").
    /// </summary>
    public const string SectionName = "SloTargets";

    /// <summary>
    /// APM application identifier for this service (e.g., "APM00000001"). Required.
    /// Reported with every target so Dynatrace can group SLOs by application.
    /// </summary>
    public string ApmId { get; set; } = string.Empty;

    /// <summary>
    /// Global default latency threshold in milliseconds (Default: 300ms).
    /// </summary>
    public double DefaultLatencyMs { get; set; } = 300;

    /// <summary>
    /// Target thresholds defined per route pattern.
    /// </summary>
    public List<SloEndpointTarget> Endpoints { get; set; } = new();

    /// <summary>
    /// Core infrastructure probes excluded by default across all services.
    /// </summary>
    private static readonly HashSet<string> DefaultProbes = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health",
        "/healthz",
        "/ready",
        "/live"
    };

    /// <summary>
    /// Service-specific paths to exclude, populated via appsettings.json.
    /// </summary>
    public List<string> AdditionalExcludedPaths { get; set; } = new();

    /// <summary>
    /// Resolves the unified set of excluded paths (defaults + custom additions).
    /// </summary>
    internal HashSet<string> GetEffectiveExcludedPaths()
    {
        var effective = new HashSet<string>(DefaultProbes, StringComparer.OrdinalIgnoreCase);

        foreach (var path in AdditionalExcludedPaths)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                effective.Add(path.Trim());
            }
        }

        return effective;
    }
}

/// <summary>
/// Latency target for a single route, optionally restricted to one HTTP method.
/// </summary>
public class SloEndpointTarget
{
    /// <summary>
    /// HTTP method (e.g., "POST"). Optional: when empty, the target applies to every method
    /// on the route. A method-specific target takes precedence over a method-less one.
    /// </summary>
    public string? Method { get; set; }

    /// <summary>
    /// Endpoint route pattern (e.g., "/api/orders/{id}").
    /// </summary>
    public string Route { get; set; } = string.Empty;

    /// <summary>
    /// Target latency threshold in milliseconds for this specific route.
    /// </summary>
    public double TargetMs { get; set; }
}