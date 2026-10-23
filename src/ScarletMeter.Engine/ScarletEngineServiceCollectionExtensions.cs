using AionDpsMeter.Services.Extensions;
using AionDpsMeter.Services.Models;
using AionDpsMeter.Services.PacketCapture;
using AionDpsMeter.Services.Services;
using AionDpsMeter.Services.Services.Entity;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Settings;
using AionDpsMeter.Services.Services.Timed;
using Microsoft.Extensions.DependencyInjection;
using ScarletMeter.Engine.Upstream;
using ScarletMeter.Engine.Upstream.Demo;

namespace ScarletMeter.Engine;

/// <summary>Registers the combat engine and the upstream services it is built on.</summary>
public static class ScarletEngineServiceCollectionExtensions
{
    /// <summary>
    /// Mirrors the service graph upstream's own App.xaml.cs builds, minus its UI, update checker and packet
    /// logger. The history database goes to <see cref="EngineOptions.HistoryDatabasePath"/>; logging must be
    /// configured by the host (any provider, or none).
    /// </summary>
    public static IServiceCollection AddScarletEngine(
        this IServiceCollection services, EngineOptions options, EngineSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddLogging();
        services.AddSingleton(options);
        services.AddSingleton(settings ?? new EngineSettings());
        services.AddSingleton<IAppSettingsService, UpstreamSettingsBridge>();

        // Upstream history store: SQLite through EF Core, at an absolute path under the data directory.
        services.AddCombatHistoryPersistence(options.HistoryDatabasePath);

        // Upstream capture and TCP reassembly. No FilePacketWriter is registered, so payloads are never logged.
        // Demo mode swaps the adapter for the synthetic feed, which upstream's pipeline starts and stops alike.
        services.AddSingleton<TcpStreamBuffer>();
        switch (options.Capture)
        {
            case CaptureMode.Live:
                services.AddSingleton<IPacketCaptureDevice, CaptureDevice>();
                break;
            case CaptureMode.Demo:
                services.AddSingleton<IPacketCaptureDevice, DemoCaptureDevice>();
                break;
            default:
                services.AddSingleton<IPacketCaptureDevice, NoCaptureDevice>();
                break;
        }

        // Upstream game state, the timed trackers its session manager requires, decoding and the pipeline.
        services.AddSingleton<EntityTracker>();
        services.AddKeyedSingleton<ITimedEventTracker, BuffTimedEventTracker>("Buffs");
        services.AddKeyedSingleton<ITimedEventTracker, SkillCdTimedEventTracker>("SkillCdUpdate");
        services.AddSingleton<CombatSessionManager>();
        services.AddPacketProcessingRouting();
        services.AddSingleton<IPacketService, PacketPipelineService>();

        services.AddSingleton<ICombatEngine, UpstreamCombatEngine>();
        return services;
    }
}
