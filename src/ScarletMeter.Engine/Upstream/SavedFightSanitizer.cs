using AionDpsMeter.Core.Models;
using AionDpsMeter.Services.Models;
using AionDpsMeter.Services.Services.Session;

namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// Makes a fight read back from the history database safe to map. The database is a file anything can change,
/// and upstream deserialises it leniently (a missing field becomes its default), so a saved fight can hold
/// entries no live fight produces: hits with no time or skill, hits days away from the fight, buffs longer than
/// upstream's decoder accepts, null entries. Unchecked, those overflow DateTime arithmetic or make
/// GraphDataCalculator's one-point-per-second graph millions of points long. Damaged entries are dropped, never
/// repaired, and counted for the log; a live fight's snapshot passes through unchanged.
/// </summary>
internal static class SavedFightSanitizer
{
    /// <summary>
    /// The longest span a saved fight's start and end may have; a longer one is taken as damaged. It bounds the
    /// graph (one point per second) at about 43,000 points.
    /// </summary>
    public static readonly TimeSpan MaxFightLength = TimeSpan.FromHours(12);

    /// <summary>How far outside the fight's start and end a saved hit may lie and still be kept.</summary>
    public static readonly TimeSpan HitSlack = TimeSpan.FromMinutes(1);

    /// <summary>Upstream's buff decoder rejects buffs over an hour (BuffPacketProcessor), so a longer saved one is damaged.</summary>
    public const uint MaxBuffDurationMs = 3_600_000;

    /// <summary>
    /// Whether the saved start and end can time the fight's details: both set, in order, at most
    /// <see cref="MaxFightLength"/> apart and far enough from DateTime's limits for the slack arithmetic.
    /// </summary>
    public static bool HasUsableWindow(HistorySessionSnapshot snapshot)
    {
        var margin = MaxFightLength + HitSlack + TimeSpan.FromMilliseconds(MaxBuffDurationMs);
        return snapshot.SessionStart - DateTime.MinValue > margin
            && DateTime.MaxValue - snapshot.SessionEnd > margin
            && snapshot.SessionEnd >= snapshot.SessionStart
            && snapshot.SessionEnd - snapshot.SessionStart <= MaxFightLength;
    }

    /// <summary>
    /// The snapshot without damaged entries: null entries, hits with no skill, negative damage or outside the fight's window (with
    /// <see cref="HitSlack"/>), and buff events with no time, longer than an hour, or outside the window a buff
    /// can overlap. Hits come back newest first, the order GraphDataCalculator relies on.
    /// <paramref name="removed"/> counts what was dropped. Call only when <see cref="HasUsableWindow"/> holds.
    /// </summary>
    public static HistorySessionSnapshot Clean(HistorySessionSnapshot snapshot, out int removed)
    {
        var dropped = 0;
        var firstHit = snapshot.SessionStart - HitSlack;
        var lastHit = snapshot.SessionEnd + HitSlack;
        var firstBuff = firstHit - TimeSpan.FromMilliseconds(MaxBuffDurationMs);

        var players = Keep(snapshot.PlayerStats, _ => true, ref dropped);
        var skills = KeepEach<SkillStats, IReadOnlyCollection<SkillStats>>(snapshot.SkillStatsByPlayer, _ => true, ref dropped);
        var buffs = KeepEach<BuffStats, IReadOnlyCollection<BuffStats>>(snapshot.BuffStatsByPlayer, _ => true, ref dropped);
        var hits = KeepEach<PlayerDamage, IReadOnlyList<PlayerDamage>>(snapshot.HitsByPlayer,
            hit => hit.Skill is not null && hit.Damage >= 0 && hit.DateTime >= firstHit && hit.DateTime <= lastHit, ref dropped,
            kept => kept.OrderByDescending(hit => hit.DateTime));
        var buffEvents = KeepEach<BuffEvent, IReadOnlyList<BuffEvent>>(snapshot.BuffEventsByPlayer,
            buff => buff.DurationMs <= MaxBuffDurationMs && buff.AppliedAt >= firstBuff && buff.AppliedAt <= lastHit, ref dropped);

        removed = dropped;
        return new HistorySessionSnapshot
        {
            SessionId = snapshot.SessionId,
            TargetId = snapshot.TargetId,
            TargetName = snapshot.TargetName ?? string.Empty,
            TargetHpTotal = snapshot.TargetHpTotal,
            SessionStart = snapshot.SessionStart,
            SessionEnd = snapshot.SessionEnd,
            State = snapshot.State,
            PlayerStats = players,
            SkillStatsByPlayer = skills.ToDictionary(pair => pair.Key, pair => (IReadOnlyCollection<SkillStats>)pair.Value),
            BuffStatsByPlayer = buffs.ToDictionary(pair => pair.Key, pair => (IReadOnlyCollection<BuffStats>)pair.Value),
            HitsByPlayer = hits.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<PlayerDamage>)pair.Value),
            BuffEventsByPlayer = buffEvents.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<BuffEvent>)pair.Value),
        };
    }

    // The non-null entries that pass the check, in order (or reordered); every other entry is counted as dropped.
    private static List<T> Keep<T>(IEnumerable<T?>? entries, Func<T, bool> usable, ref int dropped,
        Func<IEnumerable<T>, IEnumerable<T>>? order = null) where T : class
    {
        var all = (entries ?? []).ToList();
        var kept = all.OfType<T>().Where(usable);
        var result = (order is null ? kept : order(kept)).ToList();
        dropped += all.Count - result.Count;
        return result;
    }

    // Keep for every player's list; a player whose list is missing gets an empty one.
    private static Dictionary<long, List<T>> KeepEach<T, TList>(IReadOnlyDictionary<long, TList>? byPlayer,
        Func<T, bool> usable, ref int dropped, Func<IEnumerable<T>, IEnumerable<T>>? order = null)
        where T : class where TList : class, IEnumerable<T>
    {
        var result = new Dictionary<long, List<T>>();
        foreach (var (playerId, entries) in byPlayer ?? new Dictionary<long, TList>())
            result[playerId] = Keep(entries, usable, ref dropped, order);
        return result;
    }
}
