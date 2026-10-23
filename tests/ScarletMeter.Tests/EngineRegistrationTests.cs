using System.IO;
using AionDpsMeter.Core.Models;
using AionDpsMeter.Services.Services.Entity;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using ScarletMeter.Engine;

namespace ScarletMeter.Tests;

/// <summary>
/// Builds the real upstream service graph (no Npcap) and checks the engine's mapping over it. A failure
/// here after an upstream update points at src/ScarletMeter.Engine/Upstream.
/// </summary>
public sealed class EngineRegistrationTests : IDisposable
{
    private readonly string dataDirectory = Path.Combine(Path.GetTempPath(), "scarlet-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider provider;

    public EngineRegistrationTests()
    {
        // ValidateOnBuild stays off: upstream registers every type in its processor namespace, including nested
        // records and compiler-generated iterators that nothing resolves and DI cannot construct. Resolving
        // the engine still constructs every real processor through the opcode registry.
        provider = new ServiceCollection()
            .AddScarletEngine(new EngineOptions { DataDirectory = dataDirectory, Capture = CaptureMode.None })
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
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
    public void HistoryDatabaseLivesInTheDataDirectory()
    {
        _ = provider.GetRequiredService<ICombatEngine>();
        Assert.True(File.Exists(Path.Combine(dataDirectory, "combat-history.db")));
    }

    [Fact]
    public void SnapshotMapsDamageRecordedByUpstream()
    {
        var engine = provider.GetRequiredService<ICombatEngine>();
        var entities = provider.GetRequiredService<EntityTracker>();
        var sessions = provider.GetRequiredService<CombatSessionManager>();

        var gladiator = new CharacterClass { Id = 11, Name = "Gladiator" };
        var alpha = entities.GetOrCreateSessionPlayer(7, gladiator);
        entities.SetSessionPlayerName(7, "Alpha", combatPower: 120_000, isUser: true);
        var beta = entities.GetOrCreateSessionPlayer(8, gladiator);
        entities.SetSessionPlayerName(8, "Beta", combatPower: 90_000);
        var mob = entities.CreateOrUpdateTargetEntity(900, mobCode: 0, hpTotal: 1_000_000);
        mob.HpCurrent = 1_000_000;

        var start = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Local);
        sessions.ProcessDamageEvent(Hit(alpha, mob, start, 600, critical: true));
        sessions.ProcessDamageEvent(Hit(beta, mob, start.AddSeconds(1), 200));
        sessions.ProcessDamageEvent(Hit(alpha, mob, start.AddSeconds(2), 200));

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

    public void Dispose()
    {
        provider.Dispose();
        // Microsoft.Data.Sqlite pools connections; release them so the temporary database can be deleted.
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(dataDirectory, recursive: true); }
        catch (IOException) { /* upstream's background cleanup may still hold the database briefly */ }
        catch (UnauthorizedAccessException) { }
    }
}
