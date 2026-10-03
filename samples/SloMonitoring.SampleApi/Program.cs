using SchemaArchitects.AspNetCore.Slo;

var builder = WebApplication.CreateBuilder(args);

// Phase 2.2: register SLO targets bound to the "SloTargets" section of appsettings.json
builder.Services.AddSloTargets(builder.Configuration);

var app = builder.Build();

app.UseRouting();

// Must run after UseRouting (to see the matched route template) and before the endpoints
app.UseSloTargets();

// Configured targets (see appsettings.json). Delays simulate work so latency can be compared to targets.

// POST-only targets (see appsettings.json)
var catalogsvc = app.MapGroup("/catalogsvc/v1");

catalogsvc.MapPost("/updateProductStatus", async () =>
{
    await Task.Delay(Random.Shared.Next(50, 200));
    return Results.Ok(new { Updated = true });
});

catalogsvc.MapPost("/infoProduct", async () =>
{
    await Task.Delay(Random.Shared.Next(100, 400));
    return Results.Ok(new { Product = "PRD-001", Status = "Active" });
});

catalogsvc.MapPost("/shipProduct", async () =>
{
    await Task.Delay(Random.Shared.Next(200, 500));
    return Results.Ok(new { Shipped = true });
});

// GET on the same path: the POST-only target does not apply, so DefaultLatencyMs is used
catalogsvc.MapGet("/shipProduct", () => Results.Ok(new { Shipments = Array.Empty<string>() }));

app.MapGet("/api/orders/{id}", async (string id) =>
{
    await Task.Delay(Random.Shared.Next(50, 200));
    return Results.Ok(new { OrderId = id, Status = "Shipped" });
});

app.MapGet("/api/orders", async () =>
{
    await Task.Delay(Random.Shared.Next(80, 250));
    return Results.Ok(new[] { new { OrderId = "ORD-1001" }, new { OrderId = "ORD-1002" } });
});

app.MapGet("/api/reports/monthly", async () =>
{
    await Task.Delay(Random.Shared.Next(600, 1500));
    return Results.Ok(new { Report = "Monthly", GeneratedAt = DateTime.UtcNow });
});

// Not configured: falls back to DefaultLatencyMs
app.MapGet("/api/customers/{id}", (string id) => Results.Ok(new { CustomerId = id }));

// Excluded: default Kubernetes probe, and a configured exclusion
app.MapGet("/healthz", () => Results.Ok("healthy"));
app.MapGet("/internal/ping", () => Results.Ok("pong"));

app.Run();
