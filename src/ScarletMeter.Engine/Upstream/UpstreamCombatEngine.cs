using AionDpsMeter.Services.Models;
using AionDpsMeter.Services.PacketCapture;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Session.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// <see cref="ICombatEngine"/> over upstream RATmeter's pipeline. Upstream's IPacketService drives capture,
/// TCP reassembly, frame decoding and the opcode processors that feed CombatSessionManager; snapshots, details
/// and history are read back from that manager (and saved fights from its history store), then converted by
/// <see cref="UpstreamMapping"/>. Saved data is untrusted: it is cleaned by <see cref="SavedFightSanitizer"/>, and
/// history reads fail soft (logged) rather than throw. Only the Upstream folder touches upstream types, so an
/// upstream API change is contained there.
/// </summary>
internal sealed class UpstreamCombatEngine : ICombatEngine
{
    // A details read that straddles a change of fight is taken again, this many times in all, then given up.
    private const int DetailsReadAttempts = 3;

    private readonly IPacketService pipeline;
    private readonly IPacketCaptureDevice capture;
    private readonly CombatSessionManager sessions;
    private readonly ICombatHistoryStore history;
    private readonly SavedHistoryReader savedHistory;
    private readonly OdyleEnergyTap odyle;
    private readonly ILogger<UpstreamCombatEngine> logger;
    private volatile bool savedHistoryDamaged; // set while upstream's store cannot read the saved list
    private bool disposed;

    public UpstreamCombatEngine(
        IPacketService pipeline,
        IPacketCaptureDevice capture,
        CombatSessionManager sessions,
        ICombatHistoryStore history,
        IDbContextFactory<CombatHistoryDbContext> historyDatabase,
        OdyleEnergyTap odyle,
        ILogger<UpstreamCombatEngine> logger)
    {
        this.pipeline = pipeline;
        this.capture = capture;
        this.sessions = sessions;
        this.history = history;
        savedHistory = new SavedHistoryReader(historyDatabase);
        this.odyle = odyle;
        this.logger = logger;
    }

    /// <summary>Test seam: runs in every details read between its first and last check of the active fight. Null in production.</summary>
    internal Action? BetweenDetailsReads { get; set; }

    public event EventHandler<int>? PingUpdated
    {
        add => sessions.PingUpdated += value;
        remove => sessions.PingUpdated -= value;
    }

    public OdyleEnergy? OdyleEnergy => odyle.Current;

    public event EventHandler<OdyleEnergy>? OdyleEnergyUpdated
    {
        add => odyle.Updated += value;
        remove => odyle.Updated -= value;
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
        // between the first check and the last (it ended, or the party switched targets), the parts could mix two
        // fights, so the read is taken again, and given up (null) if it keeps straddling: the caller polls again.
        // The check is by identity, so a switch away and back between the two checks goes unnoticed (documented on
        // ICombatEngine). The session's start is set once, so reading it unlocked is safe.
        for (var attempt = 1; attempt <= DetailsReadAttempts; attempt++)
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
            BetweenDetailsReads?.Invoke();
            if (ReferenceEquals(sessions.GetActiveTargetSession(), session))
                return UpstreamMapping.ToDetails(stats, duration, session.SessionStart, skills, buffs, graph, hits);
        }
        return null;
    }

    public HistoryPage GetHistory(HistoryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var valid = query.Validate();
        // "Damaged" means exactly that upstream cannot read the saved list: it ends as soon as upstream's own
        // read of every row succeeds again, whatever the row-by-row read would have made of it.
        if (savedHistoryDamaged && !UpstreamReadsEverySavedFight()) return ReadDamagedHistory(valid);
        try
        {
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
        catch (Exception ex)
        {
            // Most often a damaged row: upstream's store reads a page in one go, so such a row fails every page it
            // falls on (and only those: other pages would still count it). Whatever the cause, the list is read
            // row by row from now on, so every page agrees, until upstream can read the whole list again.
            logger.LogError(ex, "The fight history could not be read as saved; reading it row by row until upstream can read it again.");
            savedHistoryDamaged = true;
            return ReadDamagedHistory(valid);
        }
    }

    // Whether upstream's store reads every saved row's list columns again; once it does, damaged mode ends. It
    // reads outside the session lock.
    private bool UpstreamReadsEverySavedFight()
    {
        try
        {
            history.GetSessionList();
        }
        catch (Exception)
        {
            return false;
        }
        savedHistoryDamaged = false;
        logger.LogInformation("The fight history reads as saved again.");
        return true;
    }

    public HistoryFight? GetHistoryFight(Guid fightId)
    {
        try
        {
            // Saved fights are read from the store directly, not through CombatSessionManager.GetHistorySession,
            // which holds the session lock while it decompresses and deserialises every hit of the fight: a
            // performance choice, so that loading a large fight cannot stall the capture thread. A fight is saved
            // only once it has left upstream's live sessions, so only a running fight is left for the session
            // manager, which then snapshots it.
            var snapshot = history.GetSession(fightId) ?? sessions.GetHistorySession(fightId);
            return snapshot is null ? null : MapFight(snapshot);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fight {FightId} could not be read from the history.", fightId);
            return null;
        }
    }

    // Maps a saved or running fight once its damaged entries are dropped; what was left out is logged.
    private HistoryFight MapFight(HistorySessionSnapshot snapshot)
    {
        if (!SavedFightSanitizer.HasUsableWindow(snapshot))
        {
            logger.LogWarning(
                "Fight {FightId} was saved with an unusable start ({Start}) or end ({End}); only its players are shown.",
                snapshot.SessionId, snapshot.SessionStart, snapshot.SessionEnd);
            return UpstreamMapping.ToHistoryFight(snapshot, includeDetails: false);
        }

        var clean = SavedFightSanitizer.Clean(snapshot, out var removed);
        if (removed > 0)
            logger.LogWarning("Fight {FightId}: {Removed} damaged saved entries were left out.", snapshot.SessionId, removed);
        return UpstreamMapping.ToHistoryFight(clean, includeDetails: true, (playerId, ex) =>
            logger.LogWarning(ex, "Fight {FightId}: player {PlayerId}'s details could not be built and are left out.",
                snapshot.SessionId, playerId));
    }

    // The saved fights that can still be read, marked incomplete (running fights are left out: only upstream's own
    // query lists them); an empty page if even that fails.
    private HistoryPage ReadDamagedHistory(HistoryQuery valid)
    {
        try
        {
            var (items, total, skipped) = savedHistory.ReadPage(valid);
            logger.LogWarning("Saved fights left out of the history because they could not be read: {Skipped}.", skipped);
            return new HistoryPage(
                items.Select(UpstreamMapping.ToHistoryEntry).ToList(), total, valid.Page, valid.PageSize, IsComplete: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The fight history could not be read at all.");
            return new HistoryPage([], 0, valid.Page, valid.PageSize, IsComplete: false);
        }
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
        odyle.Dispose();
    }
}
