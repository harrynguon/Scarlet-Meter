using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace ScarletMeter.App.Updates;

/// <summary>Registers the upstream release checker for the composition root.</summary>
public static class UpdatesServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="UpstreamReleaseChecker"/> as a singleton, also as <see cref="IUpstreamReleaseChecker"/>,
    /// with the production HTTP handler, <see cref="BuildInfo.Current"/> and <see cref="TimeProvider.System"/>
    /// unless the host registered its own. Registering sends nothing: the first request waits for a
    /// <see cref="IUpstreamReleaseChecker.CheckAsync"/> call. The container disposes the checker and its handler.
    /// </summary>
    public static IServiceCollection AddUpstreamReleaseChecker(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(BuildInfo.Current);
        services.TryAddSingleton(provider => new UpstreamReleaseChecker(
            UpstreamReleaseChecker.CreateDefaultHandler(),
            provider.GetRequiredService<BuildInfo>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<UpstreamReleaseChecker>>()));
        services.TryAddSingleton<IUpstreamReleaseChecker>(provider => provider.GetRequiredService<UpstreamReleaseChecker>());
        return services;
    }
}
