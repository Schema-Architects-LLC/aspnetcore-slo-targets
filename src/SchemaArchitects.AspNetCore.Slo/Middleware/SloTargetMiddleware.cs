using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SchemaArchitects.AspNetCore.Slo;

/// <summary>
/// Resolves the latency SLO target for each routed request and passes it to
/// <see cref="RecordSloTarget"/>, where Dynatrace OneAgent captures it.
/// </summary>
public class SloTargetMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SloOptions _options;
    private readonly ILogger<SloTargetMiddleware> _logger;
    private readonly PathString[] _excludedPaths;

    private readonly string _apmId;

    // Configured targets keyed by "METHOD /normalized/route" or "* /normalized/route" (case-insensitive)
    private readonly Dictionary<string, double> _targetsByKey;

    // Raw route template => normalized route. Bounded by the number of endpoints in the app.
    private readonly ConcurrentDictionary<string, string> _normalizedRoutes = new();

    private int _orderingWarningLogged;

    // Internal hook exclusively used for in-memory unit tests
    internal static Action<SloRecordedTarget>? OnTargetRecorded;

    /// <summary>
    /// Creates the middleware. Instantiated by <see cref="SloExtensions.UseSloTargets"/>.
    /// </summary>
    public SloTargetMiddleware(RequestDelegate next, IOptions<SloOptions> options, ILogger<SloTargetMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
        _apmId = _options.ApmId.Trim();

        // Evaluated once at application startup
        _excludedPaths = _options.GetEffectiveExcludedPaths()
            .Select(p => new PathString(p.StartsWith('/') ? p.TrimEnd('/') : "/" + p.TrimEnd('/')))
            .ToArray();

        // Duplicates are rejected by SloOptionsValidator at startup
        _targetsByKey = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in _options.Endpoints)
        {
            _targetsByKey[SloRouteTemplate.TargetKey(target.Method, SloRouteTemplate.Normalize(target.Route))] = target.TargetMs;
        }
    }

    /// <summary>
    /// Records the SLO target for the current request, then invokes the next middleware.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // Skip excluded paths and everything beneath them, e.g. "/swagger" also covers
        // "/swagger/index.html" but not "/swaggerish" (segment-aware, case-insensitive)
        if (IsExcluded(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // Only matched routes are tracked. Unmatched requests (404s, scanners) would otherwise
        // report their raw path and create unbounded cardinality in Dynatrace.
        if (context.GetEndpoint() is not RouteEndpoint endpoint || endpoint.RoutePattern.RawText is null)
        {
            await _next(context);
            WarnIfRegisteredBeforeRouting(context);
            return;
        }

        // Route template avoids high cardinality, normalized so controller routes ("api/Orders/{id:int}")
        // and minimal API routes ("/api/orders/{id}") match configuration and report consistently
        var routePattern = _normalizedRoutes.GetOrAdd(endpoint.RoutePattern.RawText, SloRouteTemplate.Normalize);

        var method = context.Request.Method;

        // Resolve target: method-specific ("POST /api/orders"), then any-method ("* /api/orders"), then default
        var targetMs =
            _targetsByKey.TryGetValue(SloRouteTemplate.TargetKey(method, routePattern), out var byMethod) ? byMethod :
            _targetsByKey.TryGetValue(SloRouteTemplate.TargetKey(null, routePattern), out var anyMethod) ? anyMethod :
            _options.DefaultLatencyMs;

        // Hook method for Dynatrace OneAgent IL Weaving
        RecordSloTarget(_apmId, method, routePattern, targetMs);

        // Notify in-memory listeners during unit testing
        OnTargetRecorded?.Invoke(new SloRecordedTarget(_apmId, method, routePattern, targetMs));

        // Local visibility without Dynatrace (enable Debug for this category)
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("SLO target recorded: [{ApmId}] {Method} {Route} => {TargetMs} ms", _apmId, method, routePattern, targetMs);
        }

        await _next(context);
    }

    // If routing selected an endpoint *after* this middleware ran, UseSloTargets() was registered
    // before UseRouting() and no request will ever be recorded. Logged once per application.
    private void WarnIfRegisteredBeforeRouting(HttpContext context)
    {
        if (context.GetEndpoint() is RouteEndpoint &&
            Interlocked.Exchange(ref _orderingWarningLogged, 1) == 0)
        {
            _logger.LogWarning(
                "SLO targets are not being recorded: UseSloTargets() runs before UseRouting(). " +
                "Call app.UseSloTargets() after app.UseRouting() in Program.cs.");
        }
    }

    private bool IsExcluded(PathString path)
    {
        foreach (var excluded in _excludedPaths)
        {
            if (path.StartsWithSegments(excluded, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Dynatrace OneAgent capture point. Configure request attributes on argument 1
    /// (<paramref name="apmId"/>), argument 2 (<paramref name="method"/>), argument 3
    /// (<paramref name="route"/>) and argument 4 (<paramref name="targetMs"/>).
    /// Must stay non-inlined and keep its signature, or the Dynatrace configuration breaks.
    /// </summary>
    /// <param name="apmId">APM application identifier, e.g. "APM00000001".</param>
    /// <param name="method">HTTP method of the request, e.g. "POST".</param>
    /// <param name="route">Normalized route template, e.g. "/api/orders/{id}".</param>
    /// <param name="targetMs">Latency target in milliseconds.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void RecordSloTarget(string apmId, string method, string route, double targetMs)
    {
        // Intentionally empty. Dynatrace OneAgent captures arguments from the call stack.
    }
}
