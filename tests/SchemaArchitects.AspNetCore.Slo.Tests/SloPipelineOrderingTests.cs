using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SchemaArchitects.AspNetCore.Slo.Tests;

[Collection(SloTargetHookCollection.Name)]
public class SloPipelineOrderingTests
{
    private static async Task<IHost> CreateHostAsync(CapturingLoggerProvider logs, bool sloBeforeRouting)
    {
        return await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer()
                    .ConfigureLogging(logging => logging.AddProvider(logs))
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddSloTargets(o => o.ApmId = "APM00000001");
                    })
                    .Configure(app =>
                    {
                        if (sloBeforeRouting)
                        {
                            app.UseSloTargets();
                            app.UseRouting();
                        }
                        else
                        {
                            app.UseRouting();
                            app.UseSloTargets();
                        }

                        app.UseEndpoints(endpoints => endpoints.MapGet("/api/orders", () => Results.Ok()));
                    });
            })
            .StartAsync();
    }

    [Fact]
    public async Task UseSloTargets_BeforeUseRouting_LogsWarningOnce()
    {
        bool hookTriggered = false;
        SloTargetMiddleware.OnTargetRecorded = _ => hookTriggered = true;

        var logs = new CapturingLoggerProvider();
        using var host = await CreateHostAsync(logs, sloBeforeRouting: true);
        var client = host.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/orders")).StatusCode);

        Assert.False(hookTriggered);
        var warning = Assert.Single(logs.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("UseSloTargets() runs before UseRouting()", warning.Message);
    }

    [Fact]
    public async Task UseSloTargets_AfterUseRouting_DoesNotWarn_EvenForUnmatchedRequests()
    {
        var logs = new CapturingLoggerProvider();
        using var host = await CreateHostAsync(logs, sloBeforeRouting: false);
        var client = host.GetTestClient();

        await client.GetAsync("/api/orders");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/does-not-exist")).StatusCode);

        Assert.DoesNotContain(logs.Entries, e => e.Level == LogLevel.Warning);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<(LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) =>
            categoryName == typeof(SloTargetMiddleware).FullName ? new CapturingLogger(Entries) : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentBag<(LogLevel, string)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
