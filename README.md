# SchemaArchitects.AspNetCore.Slo

[![CI](https://github.com/schema-architects/aspnetcore-slo-targets/actions/workflows/ci.yml/badge.svg)](https://github.com/schema-architects/aspnetcore-slo-targets/actions/workflows/ci.yml)
[![Target Framework](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Observability](https://img.shields.io/badge/Dynatrace-OneAgent%20IL%20Weaving-1496FF.svg)](https://www.dynatrace.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](https://github.com/schema-architects/aspnetcore-slo-targets/blob/main/LICENSE)

ASP.NET Core middleware library designed to standardize latency-based Service Level Objectives (SLOs) across microservices. Captures target thresholds with zero external metrics overhead using **Dynatrace OneAgent IL (Intermediate Language) Weaving**.

Teams install one package, declare per-endpoint latency targets in `appsettings.json`, and every request carries its APM ID, HTTP method, route template and target into Dynatrace, ready for centralized SLO dashboards.

## Design Decisions

**Zero-dependency telemetry through IL weaving.** Instead of shipping a metrics SDK, the middleware calls an intentionally empty, non-inlined method, `RecordSloTarget(apmId, method, route, targetMs)`. Dynatrace OneAgent instruments that method at runtime and captures its arguments as request attributes. Services take on no telemetry dependency and no exporter configuration; the method signature itself is the contract with the monitoring platform.

**Cardinality protection by design.** Every value sent to the monitoring backend is bounded:
- Routes are reported as **templates** (`/api/orders/{id}`), never raw URLs (`/api/orders/12345`).
- Requests that don't match any endpoint (404s, vulnerability scanners) are **not recorded at all**, so random URLs can't create unbounded attribute values.
- Health probes and configured paths are excluded **by segment** (`/swagger` covers `/swagger/index.html` but not `/swaggerish`).

**One canonical route form.** Controllers report `api/Orders/{id:int}` while minimal APIs report `/api/orders/{id}`. Both are normalized (leading slash, constraints and optional/default markers stripped, case-insensitive matching), so configuration is written once and dashboards see one consistent value per endpoint.

**Fail fast on bad configuration.** Options are validated at startup (`ValidateOnStart`): a missing `ApmId`, non-positive targets, invalid HTTP methods and duplicate targets (detected *after* normalization) stop the application with every error listed and its exact configuration path. A misconfigured service never runs while silently reporting wrong targets.

**Detect misuse, don't just document it.** If `UseSloTargets()` is registered before `UseRouting()`, no endpoint is visible and nothing could be recorded. The middleware detects this at runtime (routing selected an endpoint *after* it ran) and logs a single actionable warning instead of failing silently.

**Hot path stays cheap.** Target lookup is a dictionary keyed by method and normalized route, built once at startup; route normalization is cached per endpoint. Per-request cost is a few segment comparisons, two dictionary lookups and one empty method call.

**Tested as it runs.** 47 tests run the real ASP.NET Core pipeline in memory (`TestServer`), covering minimal APIs and MVC controllers, method-specific and method-less targets, exclusions, 404s, pipeline ordering and configuration validation.

## Contents

1. [Installation](#1-installation)
2. [Configuration (`appsettings.json`)](#2-configuration-appsettingsjson)
3. [Registration (`Program.cs`)](#3-registration-programcs)
4. [Optional: Programmatic Setup](#4-optional-programmatic-setup)
5. [Dynatrace OneAgent Global Setup](#5-dynatrace-oneagent-global-setup)

---

## 1. Installation

Install the package via the .NET CLI:

```bash
dotnet add package SchemaArchitects.AspNetCore.Slo
```

Or add the package reference directly to your `.csproj` file:

```xml
<PackageReference Include="SchemaArchitects.AspNetCore.Slo" Version="1.0.0" />
```

---

## 2. Configuration (`appsettings.json`)

Add the `SloTargets` section to your service's `appsettings.json`:

```json
{
  "SloTargets": {
    "ApmId": "APM00000001",
    "DefaultLatencyMs": 300,
    "AdditionalExcludedPaths": [
      "/swagger",
      "/metrics",
      "/internal/ping"
    ],
    "Endpoints": [
      {
        "Method": "POST",
        "Route": "/catalogsvc/v1/shipProduct",
        "TargetMs": 400
      },
      {
        "Method": "GET",
        "Route": "/api/orders/{id}",
        "TargetMs": 150
      },
      {
        "Route": "/api/reports/monthly",
        "TargetMs": 1200
      }
    ]
  }
}
```

### Configuration Options

| Setting | Type | Default | Description |
| --- | --- | --- | --- |
| `ApmId` | `string` | *Required* | APM application identifier (e.g., `APM00000001`). Reported with every target so SLOs can be grouped by application in Dynatrace. |
| `DefaultLatencyMs` | `double` | `300` | Baseline latency target threshold (in milliseconds) applied when an endpoint is not explicitly configured in `Endpoints`. |
| `AdditionalExcludedPaths` | `string[]` | `[]` | Extra paths to exclude from SLO tracking (case-insensitive). Each entry also excludes everything beneath it: `/swagger` covers `/swagger/v1/swagger.json`, but not `/swaggerish`. |
| `Endpoints` | `array` | `[]` | List of route patterns with customized latency target thresholds. |
| `Endpoints[].Method` | `string` | *(any)* | HTTP method (`GET`, `POST`, `PUT`, `PATCH`, `DELETE`, `HEAD`, `OPTIONS`; case-insensitive). When omitted, the target applies to every method on the route. A method-specific target wins over a method-less one for the same route. |
| `Endpoints[].Route` | `string` | *Required* | Route pattern matching the ASP.NET Core endpoint route template (e.g., `/api/orders/{id}`). Matching is case-insensitive and ignores leading/trailing slashes and parameter constraints, so `/api/orders/{id}` matches a controller route `api/[controller]/{id:int}` on `OrdersController`. |
| `Endpoints[].TargetMs` | `double` | *Required* | Target threshold in milliseconds for this specific route. |

> **Validation:** Configuration is validated at startup. The application fails to start if `ApmId` is missing, `DefaultLatencyMs` or any `TargetMs` is not greater than 0, a `Route` is empty, a `Method` is not a valid HTTP method, or two targets have the same method and resolve to the same template (e.g., `POST /api/orders/{id}` and `post api/orders/{id:int}`).

### Mapping from YAML-based SLO configuration

Teams that already keep SLO targets in a shared YAML format (for example, services on other platforms) can carry the same values over with .NET naming:

| YAML (`application.yml`) | .NET (`appsettings.json`) |
| --- | --- |
| `sre.slo.apmId` | `SloTargets:ApmId` |
| `sre.slo.targets[].method` | `SloTargets:Endpoints[].Method` |
| `sre.slo.targets[].path` | `SloTargets:Endpoints[].Route` |
| `sre.slo.targets[].target-ms` | `SloTargets:Endpoints[].TargetMs` |

> **Default Probes:** Standard Kubernetes probes (`/health`, `/healthz`, `/ready`, `/live`) are excluded automatically and do not need to be declared in `AdditionalExcludedPaths`.

> **Unmatched Requests:** Requests that don't match an ASP.NET Core endpoint (404s, scanners, static files) are not recorded. This keeps random URLs from creating unbounded `SLO_Target_Route` values in Dynatrace.

---

## 3. Registration (`Program.cs`)

Bind the configuration and add the middleware to the HTTP pipeline:

```csharp
using SchemaArchitects.AspNetCore.Slo;

var builder = WebApplication.CreateBuilder(args);

// 1. Register SLO services bound to appsettings.json
builder.Services.AddSloTargets(builder.Configuration);

builder.Services.AddControllers();

var app = builder.Build();

app.UseRouting();

// 2. Add SLO middleware (must run AFTER UseRouting and BEFORE MapControllers/endpoints)
app.UseSloTargets();

app.MapControllers();

app.Run();
```

> **Ordering:** If `UseSloTargets()` is registered before `UseRouting()`, no targets can be recorded. The middleware detects this on the first affected request and logs a warning once.

> **Reported route:** The route sent to Dynatrace is the normalized template: leading slash, constraints removed (`api/Orders/{id:int}` → `/api/Orders/{id}`).

---

## 4. Optional: Programmatic Setup

To configure in code instead of `appsettings.json`:

```csharp
builder.Services.AddSloTargets(options =>
{
    options.ApmId = "APM00000001";
    options.DefaultLatencyMs = 250;
    options.AdditionalExcludedPaths.Add("/swagger");
    options.Endpoints.Add(new SloEndpointTarget
    {
        Method = "POST",
        Route = "/api/checkout",
        TargetMs = 180
    });
});
```

---

### Verifying Locally

Enable `Debug` logging for the library to see each recorded target without Dynatrace:

```json
"Logging": { "LogLevel": { "SchemaArchitects.AspNetCore.Slo": "Debug" } }
```

```
SLO target recorded: [APM00000001] POST /catalogsvc/v1/shipProduct => 400 ms
```

A runnable reference service lives in [`samples/SloMonitoring.SampleApi`](https://github.com/schema-architects/aspnetcore-slo-targets/tree/main/samples/SloMonitoring.SampleApi). Start it with `dotnet run --project samples/SloMonitoring.SampleApi` and send the requests in `SampleApi.http`.

---

## 5. Dynatrace OneAgent Global Setup

The middleware executes a non-inlined hook method on every evaluated request:

```csharp
[MethodImpl(MethodImplOptions.NoInlining)]
public static void RecordSloTarget(string apmId, string method, string route, double targetMs)
```

Configure four global **Request Attributes** in Dynatrace under **Settings > Server-side service monitoring > Request attributes**:

| Request Attribute | Type | Source | Method / Parameter |
| --- | --- | --- | --- |
| `SLO_APM_ID` | `Text` | `.NET method argument` | `RecordSloTarget` → `Argument 1` (`apmId`) |
| `SLO_Target_Method` | `Text` | `.NET method argument` | `RecordSloTarget` → `Argument 2` (`method`) |
| `SLO_Target_Route` | `Text` | `.NET method argument` | `RecordSloTarget` → `Argument 3` (`route`) |
| `SLO_Target_Threshold` | `Floating point` | `.NET method argument` | `RecordSloTarget` → `Argument 4` (`targetMs`) |

> Use the same request attribute names across all services, regardless of language or framework, so every service feeds the same SLO dashboards.
