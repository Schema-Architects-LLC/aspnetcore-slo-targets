using System.Text.RegularExpressions;

namespace SchemaArchitects.AspNetCore.Slo;

/// <summary>
/// Canonical route form used both for matching configured targets and for reporting to Dynatrace.
/// </summary>
internal static class SloRouteTemplate
{
    // "{id:int}", "{id?}", "{page=1}", "{**slug}" => "{id}", "{id}", "{page}", "{slug}"
    private static readonly Regex Parameter = new(@"\{\**([^:=?}]+)[^}]*\}", RegexOptions.Compiled);

    /// <summary>
    /// Normalizes a route template: one leading slash, no trailing slash, and parameter
    /// constraints, defaults, optional markers and catch-all markers removed.
    /// Controller routes ("api/Orders/{id:int}") and minimal API routes ("/api/orders/{id}")
    /// then compare equal (case-insensitively).
    /// </summary>
    internal static string Normalize(string route)
    {
        var trimmed = route.Trim().Trim('/');
        return "/" + Parameter.Replace(trimmed, "{$1}");
    }

    /// <summary>
    /// Lookup key for a target: "POST /api/orders", or "* /api/orders" when no method is given.
    /// </summary>
    internal static string TargetKey(string? method, string normalizedRoute)
    {
        var verb = string.IsNullOrWhiteSpace(method) ? AnyMethod : method.Trim().ToUpperInvariant();
        return verb + " " + normalizedRoute;
    }

    internal const string AnyMethod = "*";
}
