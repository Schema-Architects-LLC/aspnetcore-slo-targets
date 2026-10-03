using Microsoft.Extensions.Options;

namespace SchemaArchitects.AspNetCore.Slo;

/// <summary>
/// Rejects invalid SLO configuration at startup instead of silently mis-reporting targets.
/// </summary>
internal sealed class SloOptionsValidator : IValidateOptions<SloOptions>
{
    private static readonly HashSet<string> HttpMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"
    };

    public ValidateOptionsResult Validate(string? name, SloOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApmId))
        {
            failures.Add($"{SloOptions.SectionName}:ApmId is required (e.g., \"APM00000001\").");
        }

        if (options.DefaultLatencyMs <= 0)
        {
            failures.Add($"{SloOptions.SectionName}:DefaultLatencyMs must be greater than 0 (was {options.DefaultLatencyMs}).");
        }

        var seenTargets = new Dictionary<string, SloEndpointTarget>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < options.Endpoints.Count; i++)
        {
            var endpoint = options.Endpoints[i];
            var prefix = $"{SloOptions.SectionName}:Endpoints[{i}]";

            if (string.IsNullOrWhiteSpace(endpoint.Route))
            {
                failures.Add($"{prefix}:Route is required.");
                continue;
            }

            if (endpoint.TargetMs <= 0)
            {
                failures.Add($"{prefix}:TargetMs must be greater than 0 for route '{endpoint.Route}' (was {endpoint.TargetMs}).");
            }

            if (!string.IsNullOrWhiteSpace(endpoint.Method) && !HttpMethods.Contains(endpoint.Method.Trim()))
            {
                failures.Add($"{prefix}:Method '{endpoint.Method}' is not a valid HTTP method ({string.Join(", ", HttpMethods)}).");
                continue;
            }

            // Duplicates are detected after normalization, so "POST /api/orders/{id}" and "post api/orders/{id:int}"
            // collide, while "GET /api/orders" and "POST /api/orders" are distinct targets
            var key = SloRouteTemplate.TargetKey(endpoint.Method, SloRouteTemplate.Normalize(endpoint.Route));
            if (seenTargets.TryGetValue(key, out var first))
            {
                failures.Add($"{prefix} '{Describe(endpoint)}' duplicates '{Describe(first)}' (both resolve to '{key}').");
            }
            else
            {
                seenTargets[key] = endpoint;
            }
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }

    private static string Describe(SloEndpointTarget target) =>
        string.IsNullOrWhiteSpace(target.Method) ? target.Route : $"{target.Method} {target.Route}";
}
