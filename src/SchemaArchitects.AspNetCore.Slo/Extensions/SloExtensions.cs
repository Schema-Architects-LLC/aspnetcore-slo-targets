using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace SchemaArchitects.AspNetCore.Slo;

/// <summary>
/// Registration helpers for SLO target tracking.
/// </summary>
public static class SloExtensions
{
    /// <summary>
    /// Registers SLO configuration bound to the "SloTargets" section in appsettings.json.
    /// Invalid configuration fails application startup.
    /// </summary>
    public static IServiceCollection AddSloTargets(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SloOptions>()
            .Bind(configuration.GetSection(SloOptions.SectionName))
            .ValidateOnStart();

        AddValidation(services);
        return services;
    }

    /// <summary>
    /// Registers SLO configuration programmatically via delegate.
    /// Invalid configuration fails application startup.
    /// </summary>
    public static IServiceCollection AddSloTargets(this IServiceCollection services, Action<SloOptions> configureOptions)
    {
        services.AddOptions<SloOptions>()
            .Configure(configureOptions)
            .ValidateOnStart();

        AddValidation(services);
        return services;
    }

    /// <summary>
    /// Adds the SLO target extraction middleware to the HTTP request pipeline.
    /// Must be called after <c>UseRouting()</c>; a warning is logged at runtime if it is not.
    /// </summary>
    public static IApplicationBuilder UseSloTargets(this IApplicationBuilder app)
    {
        return app.UseMiddleware<SloTargetMiddleware>();
    }

    private static void AddValidation(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SloOptions>, SloOptionsValidator>());
    }
}
