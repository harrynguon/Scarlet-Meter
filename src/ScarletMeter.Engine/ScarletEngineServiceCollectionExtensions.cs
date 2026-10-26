using AionDpsMeter.Services.Extensions;
using AionDpsMeter.Services.Models;
using AionDpsMeter.Services.PacketCapture;
using AionDpsMeter.Services.Services;
using AionDpsMeter.Services.Services.Entity;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Session.Persistence;
using AionDpsMeter.Services.Services.Settings;
using AionDpsMeter.Services.Services.Timed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

        // Upstream history store: SQLite through EF Core, at an absolute path under the data directory. Behind
        // Scarlet's decorator, which does not save training-dummy fights the player did not hit.
        services.AddCombatHistoryPersistence(options.HistoryDatabasePath);
        services.AddSingleton<DummyFightFilter>();
        WrapHistoryStore(services);

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
        // Records upstream does not decode (Odyle energy, the map loaded): read beside the pipeline from the same
        // reassembled stream by one tap, which opens each LZ4 bundle once for all its readers.
        services.AddSingleton<OdyleEnergyTap>();
        services.AddSingleton<MapLoadTap>();
        services.AddSingleton<IRecordReader>(provider => provider.GetRequiredService<OdyleEnergyTap>());
        services.AddSingleton<IRecordReader>(provider => provider.GetRequiredService<MapLoadTap>());
        services.AddSingleton<RecordTap>();
        // The Abyss pause: the settings bridge reads its state on every hit, so the state is its own object.
        services.AddSingleton<PauseState>();
        services.AddSingleton<AbyssPause>();

        services.AddSingleton<ICombatEngine, UpstreamCombatEngine>();
        return services;
    }

    // Replaces upstream's one ICombatHistoryStore registration with DummyFightHistoryStore around the store that
    // registration builds, with that registration's lifetime, so upstream's session manager saves through the
    // decorator. If an upstream release registers no store or several, Single() makes AddScarletEngine throw, failing
    // every engine test.
    private static void WrapHistoryStore(IServiceCollection services)
    {
        var upstream = services.Single(d => d.ServiceType == typeof(ICombatHistoryStore) && !d.IsKeyedService);
        services.Remove(upstream);
        services.Add(ServiceDescriptor.Describe(typeof(ICombatHistoryStore), provider => new DummyFightHistoryStore(
            (ICombatHistoryStore)(upstream.ImplementationInstance
                ?? upstream.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, upstream.ImplementationType!)),
            provider.GetRequiredService<DummyFightFilter>(),
            provider.GetRequiredService<ILogger<DummyFightHistoryStore>>()), upstream.Lifetime));
    }
}
