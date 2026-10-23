using AionDpsMeter.Services.Models;
using AionDpsMeter.Services.PacketCapture;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Session.Persistence;
using Microsoft.Extensions.Logging;

namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// <see cref="ICombatEngine"/> over upstream RATmeter's pipeline. Upstream's IPacketService drives capture,
/// TCP reassembly, frame decoding and the opcode processors that feed CombatSessionManager; snapshots, details
/// and history are read back from that manager (and saved fights from its history store), then converted by
/// <see cref="UpstreamMapping"/>. Only the Upstream folder touches upstream types, so an upstream API change is
/// contained there.
/// </summary>
internal sealed class UpstreamCombatEngine : ICombatEngine
{
    // A details read that straddles a change of fight is retried this many times in all, then returned as is.
    private const int DetailsReadAttempts = 3;

    private readonly IPacketService pipeline;
    private readonly IPacketCaptureDevice capture;
    private readonly CombatSessionManager sessions;
    private readonly ICombatHistoryStore history;
    private readonly ILogger<UpstreamCombatEngine> logger;
    private bool disposed;

    public UpstreamCombatEngine(
        IPacketService pipeline,
        IPacketCaptureDevice capture,
        CombatSessionManager sessions,
        ICombatHistoryStore history,
        ILogger<UpstreamCombatEngine> logger)
    {
        this.pipeline = pipeline;
        this.capture = capture;
        this.sessions = sessions;
        this.history = history;
        this.logger = logger;
    }

    public event EventHandler<int>? PingUpdated
    {
        add => sessions.PingUpdated += value;
        remove => sessions.PingUpdated -= value;
    }

    public CaptureState Capture => new(capture.IsCapturing, capture.DeviceName);

    public void Start()
    {
        try
        {
            pipeline.Start();
        }
        catch (CaptureStartException)
        {
            // The demo device's own failure, already worded for the user.
            throw;
        }
        catch (InvalidOperationException ex)
        {
            // Upstream's CaptureDevice.StartCapture: Npcap loaded, but listed no adapter, or only kinds upstream
            // skips (Hyper-V, VMware, VirtualBox, TAP, WAN Miniport). Upstream logs nothing about which.
            throw new CaptureStartException(
                "Packet capture could not start: no eligible network adapter was found. Virtual, TAP and WAN " +
                "Miniport adapters are skipped. If Npcap was installed with access restricted to administrators, " +
                "start the meter as administrator.", ex);
        }
        catch (Exception ex)
        {
            // SharpPcap fails (DllNotFoundException, often inside a TypeInitializationException) when wpcap.dll,
            // Npcap's WinPcap-compatible library, is absent.
            throw new CaptureStartException(
                "Packet capture could not start. Install Npcap (https://npcap.com) with \"Install Npcap in " +
                "WinPcap API-compatible Mode\" ticked, then start the meter again.", ex);
        }
    }

    public void Stop() => pipeline.Stop();

    // Fight state only, under the session manager's lock. Upstream's IPacketService.Reset would also clear the
    // summon-to-owner map (a pet already out would show as an unidentified player) and the TCP reassembly
    // buffer (frames are dropped until the stream re-synchronises), both without a lock while the capture
    // thread writes them. Upstream's own UI never calls it.
    public void Reset() => sessions.Reset();

    public CombatSnapshot GetSnapshot()
    {
        // Each upstream getter takes the session manager's lock on its own, so a snapshot is a composition of
        // three consistent reads taken a few microseconds apart, which is what upstream's own UI does.
        var mob = sessions.GetActiveTargetInfo();
        var duration = sessions.GetCombatDuration();
        var stats = sessions.PlayerStats;
        if (mob is null && stats.Count == 0) return CombatSnapshot.Empty;

        var players = stats.Select(UpstreamMapping.ToPlayerRow).ToList();
        long totalDamage = players.Sum(p => p.TotalDamage);
        double partyDps = duration.TotalSeconds > 0 ? totalDamage / duration.TotalSeconds : 0;
        var target = mob is null ? null : new TargetInfo(mob.Id, mob.Name, mob.HpCurrent, mob.HpTotal, mob.IsBoss);
        return new CombatSnapshot(target, duration, partyDps, players);
    }

    public PlayerDetails? GetPlayerDetails(long playerId)
    {
        // Upstream computes every part on its own call, each under the session lock. If the active fight changed
        // between the first read and the last (it ended, or the party switched targets), the parts would mix two
        // fights, so the read is taken again. The session's start is set once, so reading it unlocked is safe.
        for (var attempt = 1; ; attempt++)
        {
            var session = sessions.GetActiveTargetSession();
            if (session is null) return null;
            var stats = sessions.PlayerStats.FirstOrDefault(p => p.PlayerId == playerId);
            if (stats is null) return null;
            var duration = sessions.GetCombatDuration();
            var skills = sessions.GetPlayerSkillStats(playerId);
            var buffs = sessions.GetPlayerBuffStats(playerId);
            var graph = sessions.GetPlayerGraphData(playerId);
            var hits = sessions.GetPlayerCombatLog(playerId);
            if (ReferenceEquals(sessions.GetActiveTargetSession(), session) || attempt == DetailsReadAttempts)
                return UpstreamMapping.ToDetails(stats, duration, session.SessionStart, skills, buffs, graph, hits);
        }
    }

    public HistoryPage GetHistory(HistoryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var valid = query.Validate();
        // Upstream merges the running boss fights with the saved ones and pages across both, under its lock.
        var page = sessions.GetHistoryPage(new HistorySessionQuery
        {
            BossNameContains = valid.TargetNameContains,
            DateFrom = valid.From,
            DateTo = valid.To,
            PageNumber = valid.Page,
            PageSize = valid.PageSize,
        });
        return new HistoryPage(
            page.Items.Select(UpstreamMapping.ToHistoryEntry).ToList(), page.TotalCount, page.PageNumber, page.PageSize);
    }

    public HistoryFight? GetHistoryFight(Guid fightId)
    {
        // Saved fights are read from the store directly, not through CombatSessionManager.GetHistorySession, which
        // holds the session lock while it decompresses and deserialises every hit of the fight: that would stall
        // the capture thread for a large fight. A fight is saved only once it has left upstream's live sessions, so
        // it is never in both; only a running fight is left for the session manager, which then snapshots it.
        var snapshot = history.GetSession(fightId) ?? sessions.GetHistorySession(fightId);
        return snapshot is null ? null : UpstreamMapping.ToHistoryFight(snapshot);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            pipeline.Stop();
            // Upstream persists completed boss fights; close the open ones so a normal exit loses nothing.
            sessions.CompleteAndPersistActiveSessions();
            history.FlushPendingSaves();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while shutting down the combat engine.");
        }
        (pipeline as IDisposable)?.Dispose();
    }
}
