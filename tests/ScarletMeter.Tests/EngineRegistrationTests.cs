using System.IO;
using AionDpsMeter.Core.Models;
using AionDpsMeter.Services.PacketCapture;
using AionDpsMeter.Services.Services.Entity;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using ScarletMeter.Engine;

namespace ScarletMeter.Tests;

/// <summary>
/// Builds the real upstream service graph (no Npcap) and checks the engine's mapping over it. A failure
/// here after an upstream update points at src/ScarletMeter.Engine/Upstream.
/// </summary>
public sealed class EngineRegistrationTests : IDisposable
{
    private static CharacterClass Gladiator => new() { Id = 11, Name = "Gladiator" }; // upstream models are mutable
    private static readonly DateTime FightStart = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Local);

    private readonly TestEngineHost host = new(CaptureMode.None);
    private readonly ServiceProvider provider;

    public EngineRegistrationTests()
    {
        provider = host.Services;
    }

    // Upstream's timed trackers take an item only while their overlay is enabled with its id listed, and Scarlet never
    // enables them, so they hold none; they are built without their 100 ms tick, which would only drop expired items.
    // Should the bridge ever enable them, this fails: give them their tick back.
    [Fact]
    public void TheTimedTrackersHoldNothingAndAreBuiltWithoutATick()
    {
        foreach (var key in new[] { "Buffs", "SkillCdUpdate" })
        {
            var tracker = provider.GetRequiredKeyedService<AionDpsMeter.Services.Services.Timed.ITimedEventTracker>(key);
            var changed = 0;
            tracker.StateChanged += (_, _) => changed++;
            tracker.Register(new AionDpsMeter.Services.Services.Timed.TimedEvent { Id = 1, Name = "Synthetic", IconUrl = string.Empty, Duration = TimeSpan.FromMinutes(1) });
            Assert.Empty(tracker.Items);
            Assert.Equal(0, changed);
        }
        var settings = provider.GetRequiredService<IAppSettingsService>();
        Assert.False(settings.BufOverlaySettings.Enabled);
        Assert.False(settings.SkillCdOverlaySettings.Enabled);
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "ScarletMeter.Engine", "ScarletEngineServiceCollectionExtensions.cs"));
        Assert.Contains("new BuffTimedEventTracker(sp.GetRequiredService<IAppSettingsService>(), Timeout.InfiniteTimeSpan)", source);
        Assert.Contains("new SkillCdTimedEventTracker(sp.GetRequiredService<IAppSettingsService>(), Timeout.InfiniteTimeSpan)", source);
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ScarletMeter.slnx"))) return dir.FullName;
        throw new InvalidOperationException("ScarletMeter.slnx was not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void EngineResolvesAndStartsWithoutCapturing()
    {
        var engine = provider.GetRequiredService<ICombatEngine>();
        engine.Start();
        Assert.False(engine.Capture.IsCapturing); // the null device never captures, so the UI can say so
        Assert.Null(engine.Capture.AdapterName);
        Assert.Same(CombatSnapshot.Empty, engine.GetSnapshot());
        engine.Stop();
        Assert.False(engine.Capture.IsCapturing);
    }

    [Fact]
    public void LiveCaptureGraphResolvesWithoutStarting()
    {
        // The production graph. Upstream's CaptureDevice constructor touches no Npcap; only Start enumerates
        // adapters, so this resolves on machines without Npcap, CI included.
        using var live = new TestEngineHost(CaptureMode.Live);
        var engine = live.Services.GetRequiredService<ICombatEngine>();
        Assert.IsType<CaptureDevice>(live.Services.GetRequiredService<IPacketCaptureDevice>());
        Assert.False(engine.Capture.IsCapturing);
        Assert.Same(CombatSnapshot.Empty, engine.GetSnapshot());
    }

    [Fact]
    public void HistoryDatabaseLivesInTheDataDirectory()
    {
        _ = provider.GetRequiredService<ICombatEngine>();
        Assert.True(File.Exists(Path.Combine(host.DataDirectory, "combat-history.db")));
    }

    [Fact]
    public void SnapshotMapsDamageRecordedByUpstream()
    {
        var engine = provider.GetRequiredService<ICombatEngine>();
        var entities = provider.GetRequiredService<EntityTracker>();
        var sessions = provider.GetRequiredService<CombatSessionManager>();

        var alpha = entities.GetOrCreateSessionPlayer(7, Gladiator);
        entities.SetSessionPlayerName(7, "Alpha", combatPower: 120_000, isUser: true);
        var beta = entities.GetOrCreateSessionPlayer(8, Gladiator);
        entities.SetSessionPlayerName(8, "Beta", combatPower: 90_000);
        var mob = Target(entities);

        sessions.ProcessDamageEvent(Hit(alpha, mob, FightStart, 600, critical: true));
        sessions.ProcessDamageEvent(Hit(beta, mob, FightStart.AddSeconds(1), 200));
        sessions.ProcessDamageEvent(Hit(alpha, mob, FightStart.AddSeconds(2), 200));

        var snapshot = engine.GetSnapshot();
        var target = Assert.IsType<TargetInfo>(snapshot.Target);
        Assert.Equal(900, target.Id);
        Assert.Equal(1_000_000, target.HpTotal);
        Assert.Equal(TimeSpan.FromSeconds(2), snapshot.Duration);
        Assert.Equal(500, snapshot.PartyDps);

        var alphaRow = Assert.Single(snapshot.Players, p => p.Name == "Alpha");
        Assert.True(alphaRow.IsUser);
        Assert.True(alphaRow.IsIdentified);
        Assert.Equal(800, alphaRow.TotalDamage);
        Assert.Equal(80, alphaRow.DamageShare);
        Assert.Equal(50, alphaRow.CriticalRate);
        Assert.Equal(11, alphaRow.ClassId);
        Assert.Equal("Gladiator", alphaRow.ClassName);
        Assert.Equal(120_000, alphaRow.CombatPower);
        var betaRow = Assert.Single(snapshot.Players, p => p.Name == "Beta");
        Assert.False(betaRow.IsUser);
        Assert.Equal(200, betaRow.TotalDamage);

        engine.Reset();
        Assert.Same(CombatSnapshot.Empty, engine.GetSnapshot());
    }

    [Fact]
    public void BossHpBeyondTheIntRangeIsMappedWhole()
    {
        // Upstream v1.11.5.341 reads boss HP as a 64-bit varint; the mapping must not narrow it.
        var engine = provider.GetRequiredService<ICombatEngine>();
        var entities = provider.GetRequiredService<EntityTracker>();
        var sessions = provider.GetRequiredService<CombatSessionManager>();
        var player = entities.GetOrCreateSessionPlayer(7, Gladiator);
        entities.SetSessionPlayerName(7, "Alpha", combatPower: 120_000, isUser: true);
        var boss = entities.CreateOrUpdateTargetEntity(901, mobCode: 0, hpTotal: 5_000_000_000);
        boss.HpCurrent = 4_000_000_000;

        sessions.ProcessDamageEvent(Hit(player, boss, FightStart, 600));

        var target = Assert.IsType<TargetInfo>(engine.GetSnapshot().Target);
        Assert.Equal(5_000_000_000, target.HpTotal);
        Assert.InRange(target.HpCurrent, 3_000_000_000, 4_000_000_000);
        Assert.InRange(target.HpPercent, 60, 80);
    }

    [Fact]
    public void ResetKeepsSummonOwnership()
    {
        var engine = provider.GetRequiredService<ICombatEngine>();
        var entities = provider.GetRequiredService<EntityTracker>();
        var sessions = provider.GetRequiredService<CombatSessionManager>();

        entities.GetOrCreateSessionPlayer(7, Gladiator);
        entities.SetSessionPlayerName(7, "Alpha", combatPower: 120_000, isUser: true);
        entities.RegisterSummon(55, 7);
        var pet = new Player { Id = 55, Name = "Spirit", CharacterClass = Gladiator };
        var mob = Target(entities);

        sessions.ProcessDamageEvent(Hit(pet, mob, FightStart, 300));
        Assert.Equal(7, Assert.Single(engine.GetSnapshot().Players).Id); // upstream credits the owner

        engine.Reset();
        Assert.Same(CombatSnapshot.Empty, engine.GetSnapshot());

        // A pet that is still out keeps hitting after a reset; its damage must still reach its owner.
        sessions.ProcessDamageEvent(Hit(pet, mob, FightStart.AddSeconds(5), 100));
        var row = Assert.Single(engine.GetSnapshot().Players);
        Assert.Equal(7, row.Id);
        Assert.Equal(100, row.TotalDamage);
    }

    [Fact]
    public void StartFailuresNameTheirCause()
    {
        // Upstream's CaptureDevice throws this when Npcap works but every adapter is of a skipped kind.
        var noAdapter = StartFailure(new InvalidOperationException("No eligible network adapters found."));
        Assert.Contains("no eligible network adapter", noAdapter.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Install Npcap", noAdapter.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(noAdapter.InnerException);

        // SharpPcap's failure when wpcap.dll is absent.
        var noNpcap = StartFailure(new DllNotFoundException("wpcap"));
        Assert.Contains("Install Npcap", noNpcap.Message, StringComparison.Ordinal);
        Assert.IsType<DllNotFoundException>(noNpcap.InnerException);
    }

    [Fact]
    public void PacketLoggingCanNeverBeEnabled()
    {
        var upstreamSettings = provider.GetRequiredService<IAppSettingsService>();
        upstreamSettings.IsPacketLoggingEnabled = true;
        Assert.False(upstreamSettings.IsPacketLoggingEnabled);
    }

    [Fact]
    public void EngineSettingsReachUpstreamAndRaiseItsChangeEvent()
    {
        var settings = provider.GetRequiredService<EngineSettings>();
        var upstreamSettings = provider.GetRequiredService<IAppSettingsService>();
        var raised = 0;
        upstreamSettings.SettingsChanged += (_, _) => raised++;

        settings.BossOnlyCapture = true;
        settings.HistoryRetentionDays = 0;
        settings.HistoryRetentionDays = 0; // unchanged after clamping: no event

        Assert.True(upstreamSettings.BossOnlyCapture);
        Assert.Equal(1, upstreamSettings.HistoryRetantionPeriod);
        Assert.Equal(2, raised);
    }

    // Starts an engine whose capture device fails with the given error and returns what the engine throws.
    private static CaptureStartException StartFailure(Exception failure)
    {
        using var failing = new TestEngineHost(CaptureMode.None,
            services => services.AddSingleton<IPacketCaptureDevice>(new FailingCaptureDevice(failure)));
        var engine = failing.Services.GetRequiredService<ICombatEngine>();
        return Assert.Throws<CaptureStartException>(engine.Start);
    }

    private static Mob Target(EntityTracker entities)
    {
        var mob = entities.CreateOrUpdateTargetEntity(900, mobCode: 0, hpTotal: 1_000_000);
        mob.HpCurrent = 1_000_000;
        return mob;
    }

    private static PlayerDamage Hit(Player source, Mob target, DateTime at, long damage, bool critical = false) => new()
    {
        DateTime = at,
        SourceEntity = source,
        TargetEntity = target,
        Damage = damage,
        IsCritical = critical,
        Skill = new Skill { Id = 1, Name = "Synthetic hit" },
        CharacterClass = source.CharacterClass!,
    };

    // A capture device that fails to start the way upstream's does, without touching Npcap.
    private sealed class FailingCaptureDevice(Exception failure) : IPacketCaptureDevice
    {
        public bool IsCapturing => false;

        public string? DeviceName => null;

        public void DiscoverAdapters()
        {
        }

        public void StartCapture() => throw failure;

        public void StopCapture()
        {
        }

        public void Dispose()
        {
        }
    }

    public void Dispose() => host.Dispose();
}
