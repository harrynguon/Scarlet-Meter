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
/// history reads fail soft (logged) rather than throw. Training-dummy fights the player is not in (strangers' fights
/// nearby) are left out of the snapshot, the details and the history list, as <see cref="DummyFightFilter"/> decides;
/// <see cref="DummyFightHistoryStore"/> keeps them out of the saved history. Only the Upstream folder touches
/// upstream types, so an upstream API change is contained there.
/// </summary>
internal sealed class UpstreamCombatEngine : ICombatEngine
{
    // A details or snapshot read that straddles a change of fight is taken again, this many times in all, then given up.
    private const int DetailsReadAttempts = 3;
    private const int SnapshotReadAttempts = 3;

    private readonly IPacketService pipeline;
    private readonly IPacketCaptureDevice capture;
    private readonly CombatSessionManager sessions;
    private readonly ICombatHistoryStore history;
    private readonly SavedHistoryReader savedHistory;
    private readonly RecordTap records;
    private readonly EntryListTap entries;
    private readonly MapLoadTap maps;
    private readonly AbyssArtifactTap artifacts;
    private readonly AbyssPause pause;
    private readonly DummyFightFilter dummies;
    private readonly ILogger<UpstreamCombatEngine> logger;
    private readonly Lock shownLock = new(); // orders GetSnapshot and Reset around `shown`
    private CombatSnapshot shown = CombatSnapshot.Empty; // the snapshot GetSnapshot last returned
    private bool shownOnDummy; // whether `shown` is a training-dummy fight
    private volatile bool savedHistoryDamaged; // set while upstream's store cannot read the saved list
    private bool disposed;

    public UpstreamCombatEngine(
        IPacketService pipeline,
        IPacketCaptureDevice capture,
        CombatSessionManager sessions,
        ICombatHistoryStore history,
        IDbContextFactory<CombatHistoryDbContext> historyDatabase,
        RecordTap records,
        EntryListTap entries,
        MapLoadTap maps,
        AbyssArtifactTap artifacts,
        AbyssPause pause,
        DummyFightFilter dummies,
        ILogger<UpstreamCombatEngine> logger)
    {
        this.pipeline = pipeline;
        this.capture = capture;
        this.sessions = sessions;
        this.history = history;
        savedHistory = new SavedHistoryReader(historyDatabase);
        this.records = records;
        this.entries = entries;
        this.maps = maps;
        this.artifacts = artifacts;
        this.pause = pause;
        this.dummies = dummies;
        this.logger = logger;
    }

    /// <summary>Test seam: runs in every details read between its first and last check of the active fight. Null in production.</summary>
    internal Action? BetweenDetailsReads { get; set; }

    /// <summary>Test seam: runs in every snapshot read after the active target is read and before its players are. Null in production.</summary>
    internal Action? BetweenSnapshotReads { get; set; }

    public event EventHandler<int>? PingUpdated
    {
        add => sessions.PingUpdated += value;
        remove => sessions.PingUpdated -= value;
    }

    public OdyleEnergy? OdyleEnergy => entries.Odyle;

    public event EventHandler<OdyleEnergy>? OdyleEnergyUpdated
    {
        add => entries.OdyleUpdated += value;
        remove => entries.OdyleUpdated -= value;
    }

    public CharacterIdentity? Character => entries.Character;

    public event EventHandler<CharacterProgress>? ProgressObserved
    {
        add => entries.ProgressObserved += value;
        remove => entries.ProgressObserved -= value;
    }

    public int? MapId => maps.Current;

    public event EventHandler<int?>? MapChanged
    {
        add => maps.Changed += value;
        remove => maps.Changed -= value;
    }

    public event EventHandler<AbyssArtifacts>? ArtifactsObserved
    {
        add => artifacts.Observed += value;
        remove => artifacts.Observed -= value;
    }

    public bool PausedInAbyss => pause.Paused;

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
    // thread writes them. Upstream's own UI never calls it. The snapshot held for a left-out dummy fight goes too.
    public void Reset()
    {
        lock (shownLock)
        {
            sessions.Reset();
            shown = CombatSnapshot.Empty;
            shownOnDummy = false;
        }
    }

    public CombatSnapshot GetSnapshot()
    {
        lock (shownLock)
        {
            // Paused for the Abyss: nothing shows (and the rows of a crowd's boss fight are never built), and nothing
            // shown before the Abyss is held over it.
            if (pause.Paused)
            {
                shownOnDummy = false;
                return shown = CombatSnapshot.Empty;
            }

            // Each upstream getter takes the session manager's lock on its own, so a snapshot is a composition of
            // consistent reads taken a few microseconds apart, which is what upstream's own UI does. If upstream switched
            // the active fight between them, they would mix two fights (one's target, the other's players), and the
            // snapshot held for an ignored dummy fight would keep that mix up; so the active fight is compared before
            // and after the reads and the read taken again, as GetPlayerDetails does. A read that keeps straddling a
            // change shows nothing new: the next poll reads afresh.
            for (var attempt = 1; attempt <= SnapshotReadAttempts; attempt++)
            {
                var session = sessions.GetActiveTargetSession();
                var mob = sessions.GetActiveTargetInfo();
                BetweenSnapshotReads?.Invoke();
                var duration = sessions.GetCombatDuration();
                var stats = sessions.PlayerStats;
                if (!ReferenceEquals(sessions.GetActiveTargetSession(), session)) continue;
                if (session is not null && mob?.Id != session.TargetId) continue;

                // Upstream makes a stranger's training-dummy fight the active one once the player's own last hit is
                // five seconds old. Such a fight is ignored: the overlay keeps the fight it showed last, as it stood,
                // as if those hits had not happened.
                if (!dummies.Keeps(mob, stats)) return Held();
                shownOnDummy = mob?.IsDummy == true;
                if (mob is null && stats.Count == 0) return shown = CombatSnapshot.Empty;

                var players = stats.Select(UpstreamMapping.ToPlayerRow).ToList();
                long totalDamage = players.Sum(p => p.TotalDamage);
                double partyDps = duration.TotalSeconds > 0 ? totalDamage / duration.TotalSeconds : 0;
                var target = mob is null ? null : new TargetInfo(mob.Id, mob.Name, mob.HpCurrent, mob.HpTotal, mob.IsBoss);
                return shown = new CombatSnapshot(target, duration, partyDps, players);
            }
            return shown;
        }
    }

    // The snapshot to show while the active fight is ignored: the one shown last, unless that one is itself a dummy
    // fight the user is not in, shown before the meter knew the user (the only way one is shown), which goes too.
    private CombatSnapshot Held()
    {
        if (shownOnDummy && !shown.Players.Any(p => p.IsUser)) shown = CombatSnapshot.Empty;
        return shown;
    }

    public PlayerDetails? GetPlayerDetails(long playerId)
    {
        // Upstream computes every part on its own call, each under the session lock. If the active fight changed
        // between the first check and the last (it ended, or the party switched targets), the parts could mix two
        // fights, so the read is taken again, and given up (null) if it keeps straddling: the caller polls again.
        // The check is by identity, so a switch away and back between the two checks goes unnoticed (documented on
        // ICombatEngine). The session's start and target are set once, so reading them unlocked is safe. Paused for the
        // Abyss: none.
        if (pause.Paused) return null;
        for (var attempt = 1; attempt <= DetailsReadAttempts; attempt++)
        {
            var session = sessions.GetActiveTargetSession();
            if (session is null) return null;
            var players = sessions.PlayerStats;
            // A training-dummy fight the player is not in is not shown (GetSnapshot), so neither are its details.
            if (!dummies.Keeps(session.TargetInfo, players)) return null;
            var stats = players.FirstOrDefault(p => p.PlayerId == playerId);
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
            var upstreamQuery = new HistorySessionQuery
            {
                BossNameContains = valid.TargetNameContains,
                DateFrom = valid.From,
                DateTo = valid.To,
                PageNumber = valid.Page,
                PageSize = valid.PageSize,
            };
            var page = sessions.GetHistoryPage(upstreamQuery);
            // Unless a running fight is a training-dummy fight the player is not in, which the list leaves out as the
            // overlay and the saved history do, or the meter is paused for the Abyss, when it lists no running fight:
            // the page is then put together again without them.
            var running = RunningFights(upstreamQuery, page.TotalCount);
            if (running.Any(fight => fight.LeftOut)) return PageWithout(running, valid);
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

    // The running boss fights that match the query, newest first, each with whether the list leaves it out. The store
    // holds finished fights only, so upstream's count less the store's is how many are running, and upstream lists
    // them ahead of the saved ones: a first page that size holds them all and reads no saved row. A fight that starts
    // or ends between these reads can be missed, so a left-out fight can still show on that one read.
    private List<(HistorySessionListItem Item, bool LeftOut)> RunningFights(HistorySessionQuery query, int totalCount)
    {
        var count = totalCount - history.GetSessionCount(query.DateFrom, query.DateTo, query.BossNameContains);
        if (count <= 0) return [];
        var head = sessions.GetHistoryPage(new HistorySessionQuery
        {
            BossNameContains = query.BossNameContains,
            DateFrom = query.DateFrom,
            DateTo = query.DateTo,
            PageNumber = 1,
            PageSize = count,
        });
        return head.Items
            .TakeWhile(item => item.State != SessionState.Completed)
            .Select(item => (item, LeavesOut(item)))
            .ToList();
    }

    // Whether the list leaves out a running fight: every one while paused for the Abyss (deep audit on PR #42: a crowd's
    // world-boss fight showed in History until the next sweep), else a training-dummy fight the player is not in. Only a
    // dummy's fight is snapshotted to find its players, since a snapshot copies every hit under the session lock. The
    // session's target is set once, so reading it unlocked is safe.
    private bool LeavesOut(HistorySessionListItem item)
    {
        if (pause.Paused) return true;
        var live = sessions.GetTargetHistory(item.TargetId).FirstOrDefault(fight => fight.SessionId == item.SessionId);
        if (live is null || !live.TargetInfo.IsDummy) return false;
        var fight = sessions.GetHistorySession(item.SessionId);
        return fight is not null && !dummies.Keeps(live.TargetInfo, fight.PlayerStats);
    }

    // Upstream's merge (CombatSessionManager.GetHistoryPage) without the left-out fights: the other running fights
    // first, then the saved ones, newest first, paged across both, so the count and the pages agree. Unlike
    // upstream's, it is not one read under the session lock, so a fight that ends meanwhile can be missing from
    // this one page.
    private HistoryPage PageWithout(List<(HistorySessionListItem Item, bool LeftOut)> running, HistoryQuery valid)
    {
        var shownRunning = running.Where(fight => !fight.LeftOut).Select(fight => fight.Item).ToList();
        var runningIds = running.Select(fight => fight.Item.SessionId).ToHashSet();
        var savedCount = history.GetSessionCount(valid.From, valid.To, valid.TargetNameContains, runningIds);
        var skip = (valid.Page - 1) * valid.PageSize;
        var items = shownRunning.Skip(skip).Take(valid.PageSize).ToList();
        if (items.Count < valid.PageSize)
        {
            items.AddRange(history.GetSessionPage(valid.From, valid.To, valid.TargetNameContains,
                Math.Max(0, skip - shownRunning.Count), valid.PageSize - items.Count, runningIds));
        }
        return new HistoryPage(
            items.Select(UpstreamMapping.ToHistoryEntry).ToList(), shownRunning.Count + savedCount, valid.Page, valid.PageSize);
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
            // manager, which then snapshots it; one the history list leaves out (a training-dummy fight the player
            // is not in, or any running fight while paused for the Abyss, never snapshotted then) is not found either.
            var snapshot = history.GetSession(fightId);
            if (snapshot is null && !pause.Paused)
            {
                snapshot = sessions.GetHistorySession(fightId);
                if (snapshot is not null && !dummies.Keeps(snapshot)) snapshot = null;
            }
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
            // Paused for the Abyss, the open fight is the Abyss's, which is never saved (Codex and the deep audit on
            // PR #42: the boss hits since the last sweep were saved at exit): it is dropped first. Capture has stopped,
            // so no map change can end or start the pause meanwhile.
            if (pause.Paused) sessions.Reset();
            // Upstream persists completed boss fights; close the open ones so a normal exit loses nothing.
            sessions.CompleteAndPersistActiveSessions();
            history.FlushPendingSaves();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while shutting down the combat engine.");
        }
        (pipeline as IDisposable)?.Dispose();
        records.Dispose();
        pause.Dispose();
    }
}
