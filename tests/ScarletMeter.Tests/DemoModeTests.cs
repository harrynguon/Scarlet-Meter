using System.Collections.Concurrent;
using System.IO;
using AionDpsMeter.Core.GameData.Services;
using AionDpsMeter.Services.PacketCapture;
using AionDpsMeter.Services.Services.Entity;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Session.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ScarletMeter.Engine;
using ScarletMeter.Engine.Upstream.Demo;

namespace ScarletMeter.Tests;

/// <summary>
/// CaptureMode.Demo: the roster resolves in upstream's game data, a whole synthetic fight (run on a simulated
/// clock) ends in a kill and a history entry with every feature the UI shows, and the feed's thread starts
/// within seconds and ends on Stop and on Dispose without letting an exception escape.
/// </summary>
public sealed class DemoModeTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);
    private const int UserId = 41_001;
    private const int ClericId = 41_002;
    private const int RangerId = 41_003;
    private const int ElementalistId = 41_004;

    [Fact]
    public void RosterResolvesInUpstreamsGameDataWithSyntheticNames()
    {
        Assert.Empty(DemoRosterCheck.Problems(GameDataProvider.Instance));
        Assert.All(DemoRoster.Party, member => Assert.StartsWith("Demo ", member.Name, StringComparison.Ordinal));
        Assert.Equal(DemoRoster.Party.Count, DemoRoster.Party.Select(member => member.ClassId).Distinct().Count());
        Assert.Equal(UserId, Assert.Single(DemoRoster.Party, member => member.IsUser).EntityId);
    }

    [Fact]
    public void DemoGraphRunsTheFeedDeviceOnItsOwnHistoryDatabase()
    {
        using var host = new TestEngineHost(CaptureMode.Demo);
        var engine = host.Services.GetRequiredService<ICombatEngine>();
        Assert.IsType<DemoCaptureDevice>(host.Services.GetRequiredService<IPacketCaptureDevice>());
        Assert.True(File.Exists(Path.Combine(host.DataDirectory, "demo-history.db")));
        Assert.False(File.Exists(Path.Combine(host.DataDirectory, "combat-history.db")));
        Assert.Equal(new CaptureState(false, null), engine.Capture); // nothing runs before Start
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(20_261_024)]
    public void SimulatedFightEndsInAKillAndOneHistoryEntry(int seed)
    {
        using var host = new TestEngineHost(CaptureMode.Demo);
        var engine = host.Services.GetRequiredService<ICombatEngine>();
        var pings = 0;
        engine.PingUpdated += (_, _) => pings++;
        var feed = new DemoCombatFeed(
            host.Services.GetRequiredService<EntityTracker>(), host.Services.GetRequiredService<CombatSessionManager>(),
            host.Services.GetRequiredService<ICombatHistoryStore>(), NullLogger.Instance, new Random(seed));

        var start = DateTime.Now;
        var clock = start;
        feed.Begin(clock);
        feed.Step(clock);
        var firstBoss = Assert.IsType<int>(feed.CurrentBossEntityId);
        CombatSnapshot? midFight = null;
        while (feed.CurrentBossEntityId is not null && clock - start < TimeSpan.FromMinutes(2))
        {
            clock = clock.AddMilliseconds(100);
            feed.Step(clock);
            if (midFight is null && clock - start >= TimeSpan.FromSeconds(30)) midFight = engine.GetSnapshot();
        }

        // Half a minute in: the party of four is on a boss whose HP is falling.
        Assert.NotNull(midFight);
        var target = Assert.IsType<TargetInfo>(midFight.Target);
        Assert.Equal(firstBoss, target.Id);
        Assert.True(target.IsBoss);
        Assert.InRange(target.HpCurrent, 1, target.HpTotal - 1);
        Assert.Equal(4, midFight.Players.Count);
        Assert.True(Assert.Single(midFight.Players, p => p.IsUser).Name == "Demo Gladiator");

        // The fight ended in a kill within the demo's bounds and was saved as one completed entry.
        Assert.Null(feed.CurrentBossEntityId);
        Assert.InRange((clock - start).TotalSeconds, 60, 90);
        var entry = Assert.Single(engine.GetHistory(new HistoryQuery()).Entries);
        Assert.True(entry.Completed);
        Assert.Equal("Aultross", entry.TargetName);
        Assert.Equal(4, entry.PlayerCount);
        Assert.Equal(entry.TargetHpTotal, entry.TotalDamage); // the last blow takes exactly the HP left
        Assert.InRange(entry.Duration.TotalSeconds, 59, 90);
        Assert.True(pings >= 60);

        var fight = engine.GetHistoryFight(entry.FightId)!;
        Assert.Equal(
            ["Demo Cleric", "Demo Elementalist", "Demo Gladiator", "Demo Ranger"],
            fight.Players.Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.All(fight.Details.Values, details =>
        {
            Assert.Contains(details.Buffs, buff => buff.Name == "Prayer of Amplification" && buff.UptimePercent > 0);
            Assert.NotEmpty(details.Graph.Points);
            Assert.NotEmpty(details.Graph.Buffs);
            Assert.All(details.Skills, skill => Assert.NotNull(skill.Icon)); // names and icons resolve from upstream's data
            Assert.DoesNotContain(details.Skills, skill => skill.Name.StartsWith("Unknown", StringComparison.Ordinal));
        });

        var user = fight.Details[UserId];
        Assert.InRange(user.Rates.Critical, 1, 99);
        Assert.InRange(user.Rates.BackAttack, 1, 99);
        Assert.Contains(user.Skills, skill => skill.SpecializationFlags.SequenceEqual([true, false, true, false, false]));

        var spirit = Assert.Single(fight.Details[ElementalistId].Skills, skill => skill.IsSummonGroup);
        Assert.Equal("Summon: Fire Spirit", spirit.Name);
        Assert.Equal(["Fire Spirit: Basic Attack", "Summon: Fire Spirit"], spirit.SummonChildren.Select(c => c.Name).Order(StringComparer.Ordinal));
        Assert.Contains(fight.Details[ElementalistId].CombatLog, hit => hit.SummonName is not null);

        var drillDart = Assert.Single(fight.Details[RangerId].Skills, skill => skill.Name == "Drill Dart");
        Assert.False(drillDart.IsDot); // a hit, then ticks
        Assert.Contains(fight.Details[RangerId].CombatLog, hit => hit.IsDot && hit.SkillName == "Drill Dart");
        Assert.True(Assert.Single(fight.Details[ClericId].Skills, skill => skill.Name == "Chain of Torment").IsDot); // ticks only

        // Ten seconds later the next boss is up.
        var ended = clock;
        while (feed.CurrentBossEntityId is null && clock - ended < TimeSpan.FromSeconds(20))
        {
            clock = clock.AddMilliseconds(100);
            feed.Step(clock);
        }
        Assert.Equal(DemoCombatFeed.BreakLength.TotalSeconds, (clock - ended).TotalSeconds, 0.15);
        Assert.Equal(firstBoss + 1, feed.CurrentBossEntityId);
        feed.Step(clock.AddSeconds(3));
        Assert.Equal("Fediv Wraith", engine.GetSnapshot().Target?.Name);
    }

    [Fact]
    public void FeedShowsAFightWithinSecondsAndItsThreadEndsOnStop()
    {
        using var host = new TestEngineHost(CaptureMode.Demo);
        var engine = host.Services.GetRequiredService<ICombatEngine>();
        var device = (DemoCaptureDevice)host.Services.GetRequiredService<IPacketCaptureDevice>();
        using var pinged = new ManualResetEventSlim();
        engine.PingUpdated += (_, _) => pinged.Set();

        engine.Start();
        Assert.Equal(new CaptureState(true, "Demo"), engine.Capture);
        var snapshot = WaitFor(engine.GetSnapshot, s => s.Players.Count > 0);
        Assert.True(snapshot.Target?.IsBoss);
        Assert.All(snapshot.Players, p => Assert.StartsWith("Demo ", p.Name, StringComparison.Ordinal));
        Assert.True(pinged.Wait(Patience, TestContext.Current.CancellationToken));

        var thread = Assert.IsType<Thread>(device.FeedThread);
        engine.Stop();
        Assert.False(thread.IsAlive);
        Assert.Null(device.FeedThread);
        Assert.Equal(new CaptureState(false, null), engine.Capture);
        Assert.NotEmpty(engine.GetSnapshot().Players); // the fight's numbers stay readable, as after a live stop

        // A second start resumes on a fresh boss with a new thread.
        engine.Start();
        var resumed = Assert.IsType<Thread>(device.FeedThread);
        Assert.NotSame(thread, resumed);
        WaitFor(engine.GetSnapshot, s => s.Target?.Id == DemoRoster.FirstBossEntityId + 1);
        engine.Stop();
        Assert.False(resumed.IsAlive);
    }

    [Fact]
    public void DisposeEndsTheThreadAndSavesTheInterruptedFight()
    {
        using var host = new TestEngineHost(CaptureMode.Demo);
        var engine = host.Services.GetRequiredService<ICombatEngine>();
        var device = (DemoCaptureDevice)host.Services.GetRequiredService<IPacketCaptureDevice>();
        engine.Start();
        WaitFor(engine.GetSnapshot, s => s.Players.Count > 0);
        var thread = device.FeedThread!;

        engine.Dispose();

        Assert.False(thread.IsAlive);
        Assert.False(engine.Capture.IsCapturing);
        var saved = Assert.Single(host.Services.GetRequiredService<ICombatHistoryStore>().GetSessionList());
        Assert.Equal("Aultross", saved.TargetName);
        Assert.Equal(SessionState.Completed, saved.State);
    }

    [Fact]
    public void AFailingEventIsLoggedOnceAndTheFeedCarriesOn()
    {
        var log = new CapturingLoggerProvider();
        using var host = new TestEngineHost(CaptureMode.Demo, services => services.AddLogging(b => b.AddProvider(log)));
        var engine = host.Services.GetRequiredService<ICombatEngine>();
        var device = (DemoCaptureDevice)host.Services.GetRequiredService<IPacketCaptureDevice>();
        // A subscriber that throws, raised on the feed's thread as a capture thread would raise it.
        var calls = 0;
        engine.PingUpdated += (_, _) =>
        {
            Interlocked.Increment(ref calls);
            throw new InvalidOperationException("Synthetic subscriber failure.");
        };

        engine.Start();
        WaitFor(() => Volatile.Read(ref calls), count => count >= 3); // pings keep coming once a second
        WaitFor(engine.GetSnapshot, s => s.Players.Count > 0);   // and so do hits
        var thread = device.FeedThread!;
        Assert.True(thread.IsAlive);
        Assert.True(engine.Capture.IsCapturing);
        engine.Stop();
        Assert.False(thread.IsAlive);

        // Logged with its exception, once for the whole run of failures.
        Assert.Single(log.Errors, e => e.Contains("Synthetic subscriber failure.", StringComparison.Ordinal));
    }

    // Polls until the condition holds, failing after a few seconds.
    private static T WaitFor<T>(Func<T> read, Func<T, bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (true)
        {
            var value = read();
            if (condition(value)) return value;
            Assert.True(DateTime.UtcNow < deadline, $"The demo did not reach the expected state within {Patience.TotalSeconds} s.");
            Thread.Sleep(50);
        }
    }

    // Collects error entries (message and exception) from every logger.
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Errors { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(Errors);

        public void Dispose()
        {
        }

        private sealed class Logger(ConcurrentQueue<string> errors) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Error) errors.Enqueue($"{formatter(state, exception)} {exception?.Message}");
            }
        }
    }
}
