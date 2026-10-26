using AionDpsMeter.Services.PacketCapture;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ScarletMeter.Engine;
using ScarletMeter.Engine.Maps;
using ScarletMeter.Engine.Upstream;
using static ScarletMeter.Tests.SyntheticRecords;

namespace ScarletMeter.Tests;

/// <summary>
/// The Abyss pause (AbyssPause): in the Abyss's maps, with the setting on, upstream's boss-only filter is forced on,
/// the fight is cleared on entering, leaving and every sweep, nothing is saved, and the engine shows nothing; leaving
/// (a server change) or the setting ends it. Records are synthetic; 20 and 22 are the Abyss's ids, other map ids made
/// up or from the names table.
/// </summary>
public sealed class AbyssPauseTests
{
    private const string AbyssConnection = "Client:50001";
    private const string NextConnection = "Client:50002";
    private static readonly DateTime FightStart = new(2026, 10, 26, 17, 0, 0, DateTimeKind.Local);

    // 20 (seen in the game) and 22 (from three sources) are the Abyss; 21 (AetherDPS's alone), the open world, a
    // dungeon and impossible ids are not.
    [Theory]
    [InlineData(20, true)]
    [InlineData(22, true)]
    [InlineData(21, false)]
    [InlineData(1010, false)]
    [InlineData(600021, false)]
    [InlineData(0, false)]
    [InlineData(-20, false)]
    public void TheAbyssIsMaps20And22(int mapId, bool abyss) => Assert.Equal(abyss, AbyssMaps.Contains(mapId));

    // Through upstream's real reassembly: the map record for 20 pauses the meter, forces upstream's boss-only filter on
    // (the player's own setting untouched), and saves the fight from before, then clears it; a boss hit then shows
    // nowhere, History included. The next connection (leaving) ends it, clears again without saving, and hits count as
    // before.
    [Fact]
    public void TheMeterPausesInTheAbyssAndTracksAgainOnLeaving()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var fight = new SyntheticCombat(host);
        var stream = host.Services.GetRequiredService<TcpStreamBuffer>();
        var upstreamSettings = host.Services.GetRequiredService<IAppSettingsService>();
        var settings = host.Services.GetRequiredService<EngineSettings>();
        var (player, boss, skill) = Fighters(fight);

        fight.Hit(player, boss, FightStart, 500, skill);
        Assert.NotEmpty(fight.Engine.GetSnapshot().Players);
        Assert.False(fight.Engine.PausedInAbyss);

        Synchronise(stream, AbyssConnection);
        stream.AddData(AbyssConnection, MapLoad(20), 0);
        Assert.True(fight.Engine.PausedInAbyss);
        Assert.True(upstreamSettings.BossOnlyCapture);
        Assert.False(settings.BossOnlyCapture);
        Assert.Empty(fight.Sessions.PlayerStats);
        // The fight from before, still open (upstream saves one only at a later hit, a new try or exit), is saved on
        // entering (deep audit on PR #42: going to the Abyss straight after a kill lost it).
        fight.History.FlushPendingSaves();
        Assert.Single(fight.History.GetSessionList());

        fight.Hit(player, boss, FightStart.AddSeconds(1), 500, skill);
        Assert.Same(CombatSnapshot.Empty, fight.Engine.GetSnapshot());
        Assert.Null(fight.Engine.GetPlayerDetails(player.Id));
        // History lists no running fight while paused and opens none (deep audit on PR #42): only the saved one.
        var running = fight.Sessions.GetHistoryPage(new HistorySessionQuery { PageNumber = 1, PageSize = 10 }).Items[0];
        Assert.NotEqual(SessionState.Completed, running.State);
        Assert.Null(fight.Engine.GetHistoryFight(running.SessionId));
        Assert.Single(fight.Engine.GetHistory(new HistoryQuery()).Entries);

        Synchronise(stream, NextConnection);
        stream.AddData(NextConnection, Frame(0x3603, "00 00 00 00 00 00 00 00 00"), 0);
        Assert.False(fight.Engine.PausedInAbyss);
        Assert.False(upstreamSettings.BossOnlyCapture);
        Assert.Empty(fight.Sessions.PlayerStats);
        fight.History.FlushPendingSaves();
        Assert.Single(fight.History.GetSessionList()); // the Abyss's boss hits were dropped, not saved
        fight.Hit(player, boss, FightStart.AddSeconds(5), 500, skill);
        Assert.NotEmpty(fight.Engine.GetSnapshot().Players);
        Assert.Equal(2, fight.Engine.GetHistory(new HistoryQuery()).Entries.Count); // a running fight is listed again
    }

    // Closing the meter while paused saves nothing of the Abyss: the boss hits since the last sweep are dropped before
    // the exit's save (Codex and the deep audit on PR #42). The same hits outside the Abyss are saved at exit.
    [Fact]
    public void ClosingTheMeterWhilePausedSavesNothingFromTheAbyss()
    {
        foreach (var inAbyss in new[] { true, false })
        {
            using var host = new TestEngineHost(CaptureMode.None);
            var fight = new SyntheticCombat(host);
            var stream = host.Services.GetRequiredService<TcpStreamBuffer>();
            var (player, boss, skill) = Fighters(fight);
            if (inAbyss)
            {
                Synchronise(stream, AbyssConnection);
                stream.AddData(AbyssConnection, MapLoad(20), 0);
                Assert.True(fight.Engine.PausedInAbyss);
            }

            fight.Hit(player, boss, FightStart, 500, skill);
            ((IDisposable)fight.Engine).Dispose();
            Assert.Equal(inAbyss ? 0 : 1, fight.History.GetSessionList().Count());
        }
    }

    // While paused the sweep clears what upstream kept (a boss's hits get through boss-only) and nothing of it reaches
    // the history (the fight from before the Abyss is saved on entering); not paused, the sweep leaves the fight alone.
    // Its timer is set to an hour here, so only the sweeps the test makes run.
    [Fact]
    public void TheSweepClearsTheFightWhilePausedAndSavesNothing()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var fight = new SyntheticCombat(host);
        var maps = new MapLoadTap(NullLogger<MapLoadTap>.Instance);
        using var pause = new AbyssPause(maps, new EngineSettings(), fight.Sessions, new PauseState(), NullLogger<AbyssPause>.Instance, TimeSpan.FromHours(1));
        var (player, boss, skill) = Fighters(fight);

        fight.Hit(player, boss, FightStart, 500, skill);
        pause.Sweep();
        Assert.NotEmpty(fight.Sessions.PlayerStats);

        maps.Observe(MapLoad(20));
        fight.History.FlushPendingSaves();
        Assert.Single(fight.History.GetSessionList()); // the fight from before, saved on entering
        fight.Hit(player, boss, FightStart.AddSeconds(1), 500, skill);
        Assert.NotEmpty(fight.Sessions.PlayerStats);
        pause.Sweep();
        Assert.Empty(fight.Sessions.PlayerStats);

        fight.History.FlushPendingSaves();
        Assert.Single(fight.History.GetSessionList()); // nothing of the Abyss
    }

    // The timer sweeps while paused and stops once the pause ends: here every 20 ms, so a boss hit in the Abyss is
    // gone within moments, and one after leaving stays well past several intervals.
    [Fact]
    public void TheTimerSweepsWhilePausedAndStopsAfter()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var fight = new SyntheticCombat(host);
        var maps = new MapLoadTap(NullLogger<MapLoadTap>.Instance);
        using var pause = new AbyssPause(maps, new EngineSettings(), fight.Sessions, new PauseState(), NullLogger<AbyssPause>.Instance, TimeSpan.FromMilliseconds(20));
        var (player, boss, skill) = Fighters(fight);

        maps.Observe(MapLoad(20));
        fight.Hit(player, boss, FightStart, 500, skill);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (fight.Sessions.PlayerStats.Count > 0 && DateTime.UtcNow < deadline)
            Thread.Sleep(10);
        Assert.Empty(fight.Sessions.PlayerStats);

        // A sweep clears under the pause's lock, so once the pause has ended none can clear the next place's hits
        // (deep audit on PR #42: one already under way could).
        maps.ConnectionStarted();
        fight.Hit(player, boss, FightStart.AddSeconds(1), 500, skill);
        Thread.Sleep(200);
        Assert.NotEmpty(fight.Sessions.PlayerStats);
    }

    // The setting rules it: off, the Abyss pauses nothing; turned on there, the meter pauses; turned off while paused,
    // it tracks again. Moving from one Abyss map to the other stays paused.
    [Fact]
    public void TheSettingTurnsThePauseOnAndOff()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var engine = host.Services.GetRequiredService<ICombatEngine>();
        var stream = host.Services.GetRequiredService<TcpStreamBuffer>();
        var settings = host.Services.GetRequiredService<EngineSettings>();
        Assert.True(settings.PauseInAbyss);

        settings.PauseInAbyss = false;
        Synchronise(stream, AbyssConnection);
        stream.AddData(AbyssConnection, MapLoad(20), 0);
        Assert.False(engine.PausedInAbyss);

        settings.PauseInAbyss = true;
        Assert.True(engine.PausedInAbyss);
        stream.AddData(AbyssConnection, MapLoad(22), 0);
        Assert.True(engine.PausedInAbyss);

        settings.PauseInAbyss = false;
        Assert.False(engine.PausedInAbyss);
    }

    // Pausing and tracking again are each logged once, with the map's number alone; another Abyss map in between logs
    // nothing more.
    [Fact]
    public void ThePauseIsLoggedOnceEachWay()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var maps = new MapLoadTap(NullLogger<MapLoadTap>.Instance);
        var log = new RecordingLogger<AbyssPause>();
        using var pause = new AbyssPause(maps, new EngineSettings(), host.Services.GetRequiredService<CombatSessionManager>(), new PauseState(), log);

        maps.Observe(MapLoad(20));
        maps.Observe(MapLoad(22));
        maps.ConnectionStarted();
        maps.Observe(MapLoad(1010));
        Assert.Equal(["Meter paused: in the Abyss (map 20).", "Meter tracking again."], log.Entries.Select(e => e.Message));
    }

    // A disposed pause follows neither the map nor the setting, and its sweep does nothing.
    [Fact]
    public void ADisposedPauseFollowsNothing()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var fight = new SyntheticCombat(host);
        var maps = new MapLoadTap(NullLogger<MapLoadTap>.Instance);
        var state = new PauseState();
        var pause = new AbyssPause(maps, new EngineSettings(), fight.Sessions, state, NullLogger<AbyssPause>.Instance);
        pause.Dispose();
        pause.Dispose(); // twice is fine

        maps.Observe(MapLoad(20));
        Assert.False(state.Paused);
        pause.Update(); // a map change racing the disposal: nothing, and the disposed timer is not touched
        Assert.False(state.Paused);
        var (player, boss, skill) = Fighters(fight);
        fight.Hit(player, boss, FightStart, 500, skill);
        pause.Sweep();
        Assert.NotEmpty(fight.Sessions.PlayerStats);
    }

    // The user hitting a boss (Aultross, a boss in upstream's game data, so boss-only lets the hit through).
    private static (AionDpsMeter.Core.Models.Player Player, AionDpsMeter.Core.Models.Mob Boss, AionDpsMeter.Core.Models.Skill Skill) Fighters(SyntheticCombat fight) =>
        (fight.Player(7, "Alpha", SyntheticCombat.Class(1, "Gladiator"), isUser: true),
         fight.Target(900, SyntheticCombat.AultrossCode, 1_000_000),
         SyntheticCombat.Skill(1_100_000, "Synthetic Slash"));
}
