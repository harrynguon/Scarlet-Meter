using AionDpsMeter.Core.GameData.Services;
using AionDpsMeter.Core.Models;
using AionDpsMeter.Services.Models;
using AionDpsMeter.Services.Services.Session;

namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// Pure conversions from upstream's models to Scarlet's records. Nothing here reads shared state: callers pass
/// what they read under upstream's session lock (the live fight) or from a saved snapshot (history), so live
/// and saved details are built by the same code. A saved snapshot is cleaned by <see cref="SavedFightSanitizer"/>
/// first; the lists here are still read null-tolerantly, and figures that feed TimeSpan or sums are bounded, so
/// no saved value can make a conversion throw.
/// </summary>
internal static class UpstreamMapping
{
    /// <summary>
    /// Upstream's player totals as a row. Rates and DPS are upstream's own figures; the class name is Global's
    /// (<see cref="GlobalNames"/>).
    /// </summary>
    public static PlayerRow ToPlayerRow(PlayerStats s) => new(
        s.PlayerId, s.PlayerName ?? string.Empty, s.IsUser, s.IsIdentified, s.ClassId, GlobalNames.Of(s.ClassName),
        s.ServerName ?? string.Empty, s.CombatPower, s.TotalDamage, s.DamagePerSecond, s.DamagePercentage,
        s.CriticalRate, s.PlayerDeaths);

    /// <summary>
    /// A tracked player as the character the game named as the user's, or null when upstream has not marked it as the
    /// user or has no name for it. The class id is read as upstream's PlayerSession reads it, its name is Global's.
    /// </summary>
    public static CharacterIdentity? ToIdentity(Player? player) =>
        player is { IsIdentified: true, IsUser: true } && !string.IsNullOrWhiteSpace(player.Name)
            ? new CharacterIdentity(player.Name, player.ServerName ?? string.Empty, (int)(player.CharacterClass?.Id ?? 0),
                GlobalNames.Of(player.CharacterClass?.Name), Math.Max(0, player.CombatPower))
            : null;

    /// <summary>
    /// A class's AION 2 Global name by upstream's class id, from upstream's game data (classes.json, loaded once and only
    /// read), as <see cref="ToIdentity"/> names a class upstream knows; empty for an id the data does not have.
    /// </summary>
    public static string ClassNameOf(int classId) => GlobalNames.Of(GameDataProvider.Instance.Classes.GetById(classId)?.Name);

    /// <summary>
    /// One player's details from the parts upstream computes. <paramref name="hitsNewestFirst"/> is the player's
    /// whole hit list, newest first, as upstream returns it; only the newest <see cref="PlayerDetails.CombatLogLimit"/>
    /// become the log.
    /// </summary>
    public static PlayerDetails ToDetails(
        PlayerStats stats,
        TimeSpan fightDuration,
        DateTime fightStart,
        IEnumerable<SkillStats?> skills,
        IEnumerable<BuffStats?> buffs,
        (IReadOnlyList<DpsDataPoint> Points, IReadOnlyList<BuffTimelineEntry> BuffTimeline, double TotalSec) graph,
        IReadOnlyList<PlayerDamage?> hitsNewestFirst) => new(
            ToPlayerRow(stats),
            fightDuration,
            new HitRates(stats.HitCount, stats.CriticalRate, stats.BackAttackRate, stats.FrontAttackRate,
                stats.PerfectRate, stats.DoubleDamageRate, stats.ParryRate),
            ToSkillRows(skills),
            ToBuffRows(buffs, fightDuration),
            ToGraph(graph.Points, graph.BuffTimeline, graph.TotalSec),
            ToCombatLog(hitsNewestFirst, fightStart));

    /// <summary>Skill rows by damage, highest first (skill id breaks ties, so the order is stable between polls).</summary>
    public static IReadOnlyList<SkillRow> ToSkillRows(IEnumerable<SkillStats?>? skills) =>
        (skills ?? []).OfType<SkillStats>()
            .Select(ToSkillRow)
            .OrderByDescending(row => row.TotalDamage)
            .ThenBy(row => row.SkillId)
            .ToList();

    // Upstream fills SummonChildren for a single folded summon skill too, but marks a group, and its own UI
    // shows children, only when more than one was folded in; the children follow that flag here.
    private static SkillRow ToSkillRow(SkillStats s) => new(
        s.SkillId,
        s.SkillName ?? string.Empty,
        s.SkillIcon,
        s.IsDot,
        s.IsSummonGroup,
        (s.SpecializationFlags ?? []).ToArray(),
        s.TotalDamage,
        s.DamagePercentage,
        s.DamagePerSecond,
        s.HitCount,
        s.MaxHit,
        s.SafeMinHit,
        s.AverageDamage,
        new HitRates(s.HitCount, s.CriticalRate, s.BackAttackRate, s.FrontAttackRate, s.PerfectRate,
            s.DoubleDamageRate, s.ParryRate),
        s.IsSummonGroup ? ToSkillRows(s.SummonChildren) : []);

    /// <summary>
    /// Buff rows by uptime, longest first; uptime percent is of <paramref name="fightDuration"/>. Upstream measures
    /// uptime within the fight, so it is bounded by the fight's duration; a saved figure beyond that (or not a
    /// finite number) is damaged and is clamped, which also keeps TimeSpan from overflowing.
    /// </summary>
    public static IReadOnlyList<BuffRow> ToBuffRows(IEnumerable<BuffStats?>? buffs, TimeSpan fightDuration) =>
        (buffs ?? []).OfType<BuffStats>()
            .Select(b =>
            {
                var seconds = double.IsFinite(b.EffectiveDurationSeconds)
                    ? Math.Clamp(b.EffectiveDurationSeconds, 0, Math.Max(0, fightDuration.TotalSeconds))
                    : 0;
                var uptime = TimeSpan.FromSeconds(seconds);
                var percent = fightDuration > TimeSpan.Zero ? Math.Clamp(uptime / fightDuration * 100, 0, 100) : 0;
                return new BuffRow(b.BuffId, b.BuffName ?? string.Empty, b.BuffIcon, b.Description ?? string.Empty,
                    b.ApplicationCount, uptime, percent);
            })
            .OrderByDescending(row => row.Uptime)
            .ThenBy(row => row.BuffId)
            .ToList();

    /// <summary>Upstream's graph data, unchanged in meaning (see <see cref="DamageGraph"/>).</summary>
    public static DamageGraph ToGraph(
        IReadOnlyList<DpsDataPoint>? points, IReadOnlyList<BuffTimelineEntry>? buffTimeline, double totalSeconds)
    {
        if (points is not { Count: > 0 }) return DamageGraph.Empty;
        return new DamageGraph(
            totalSeconds,
            points.Select(p => new GraphPoint(p.SecondOffset, p.PerSecondDamage, p.CumulativeDps)).ToList(),
            (buffTimeline ?? []).Select(b => new BuffSpan(b.BuffId, b.BuffName ?? string.Empty, b.BuffIcon, b.StartSec, b.EndSec, b.Count)).ToList());
    }

    /// <summary>The newest <see cref="PlayerDetails.CombatLogLimit"/> hits, newest first, timed from <paramref name="fightStart"/>.</summary>
    public static IReadOnlyList<CombatLogEntry> ToCombatLog(IReadOnlyList<PlayerDamage?>? hitsNewestFirst, DateTime fightStart) =>
        (hitsNewestFirst ?? []).OfType<PlayerDamage>()
            .Take(PlayerDetails.CombatLogLimit)
            .Select(hit => new CombatLogEntry(
                AsLocal(hit.DateTime),
                Math.Max(0, (hit.DateTime - fightStart).TotalSeconds),
                hit.Skill?.Id ?? 0,
                hit.Skill?.Name ?? string.Empty,
                hit.Skill?.Icon,
                hit.Damage,
                FlagsOf(hit),
                hit.IsDot,
                hit.SourceSummon?.Name))
            .ToList();

    /// <summary>A history list item as an entry. Party DPS is total damage over the fight's duration.</summary>
    public static HistoryEntry ToHistoryEntry(HistorySessionListItem item) => new(
        item.SessionId, item.TargetName ?? string.Empty, item.TargetHpTotal, AsLocal(item.SessionStart),
        AsLocal(item.SessionEnd), item.Duration, item.TotalDamage, PerSecond(item.TotalDamage, item.Duration),
        item.PlayerCount, item.State == SessionState.Completed);

    /// <summary>
    /// A saved or running fight with its players' details. The entry's totals and player count are computed
    /// from the snapshot's players the way upstream computes them when it saves a fight (the total saturating
    /// rather than overflowing). Pass a snapshot cleaned by <see cref="SavedFightSanitizer"/>. With
    /// <paramref name="includeDetails"/> false (a saved fight whose start or end is damaged, so nothing in it can
    /// be timed) only the rows are returned. A player whose details still fail to build is reported through
    /// <paramref name="playerFailed"/> and left out of the details, so one damaged player does not cost the fight.
    /// </summary>
    public static HistoryFight ToHistoryFight(
        HistorySessionSnapshot snapshot, bool includeDetails = true, Action<long, Exception>? playerFailed = null)
    {
        var players = (snapshot.PlayerStats ?? []).OfType<PlayerStats>().DistinctBy(p => p.PlayerId).ToList();
        long totalDamage = SaturatingSum(players.Select(p => p.TotalDamage));
        var duration = snapshot.Duration;
        var entry = new HistoryEntry(
            snapshot.SessionId, snapshot.TargetName ?? string.Empty, snapshot.TargetHpTotal,
            AsLocal(snapshot.SessionStart), AsLocal(snapshot.SessionEnd), duration, totalDamage,
            PerSecond(totalDamage, duration), players.Count(p => p.IsIdentified || p.DamagePercentage > 1),
            snapshot.State == SessionState.Completed);

        var details = new Dictionary<long, PlayerDetails>(players.Count);
        foreach (var player in includeDetails ? players : [])
        {
            try
            {
                // Hits newest first, matching the live combat log, as GraphDataCalculator expects.
                var hits = (Lookup(snapshot.HitsByPlayer, player.PlayerId) ?? []).OfType<PlayerDamage>().ToList();
                var buffEvents = (Lookup(snapshot.BuffEventsByPlayer, player.PlayerId) ?? []).OfType<BuffEvent>().ToList();
                details[player.PlayerId] = ToDetails(
                    player, duration, snapshot.SessionStart,
                    Lookup(snapshot.SkillStatsByPlayer, player.PlayerId) ?? [],
                    Lookup(snapshot.BuffStatsByPlayer, player.PlayerId) ?? [],
                    GraphDataCalculator.Compute(hits, buffEvents),
                    hits);
            }
            catch (Exception ex)
            {
                // Defence in depth behind the sanitizer: no known damage reaches here once SavedFightSanitizer has
                // cleaned the snapshot, so no test drives it; saved data is not trusted, and one player's damage
                // must not cost the whole fight.
                playerFailed?.Invoke(player.PlayerId, ex);
            }
        }

        return new HistoryFight(entry, players.Select(ToPlayerRow).ToList(), details);
    }

    /// <summary>
    /// Upstream records local times (DateTime.Now); the history database returns them unspecified. Marking them
    /// local keeps a caller's ToLocalTime from shifting them by the UTC offset.
    /// </summary>
    public static DateTime AsLocal(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value.ToLocalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Local),
        _ => value,
    };

    private static HitFlags FlagsOf(PlayerDamage hit) =>
        (hit.IsCritical ? HitFlags.Critical : HitFlags.None)
        | (hit.IsBackAttack ? HitFlags.BackAttack : HitFlags.None)
        | (hit.IsFrontAttack ? HitFlags.FrontAttack : HitFlags.None)
        | (hit.IsPerfect ? HitFlags.Perfect : HitFlags.None)
        | (hit.IsDoubleDamage ? HitFlags.DoubleDamage : HitFlags.None)
        | (hit.IsParry ? HitFlags.Parry : HitFlags.None);

    private static double PerSecond(long damage, TimeSpan duration) =>
        duration > TimeSpan.Zero ? damage / duration.TotalSeconds : 0;

    // Enumerable.Sum is checked and would throw on saved totals that overflow together; this saturates instead.
    private static long SaturatingSum(IEnumerable<long> values)
    {
        long total = 0;
        foreach (var value in values)
        {
            if (value > 0 && total > long.MaxValue - value) total = long.MaxValue;
            else if (value < 0 && total < long.MinValue - value) total = long.MinValue;
            else total += value;
        }
        return total;
    }

    private static T? Lookup<T>(IReadOnlyDictionary<long, T>? map, long playerId) where T : class =>
        map is not null && map.TryGetValue(playerId, out var value) ? value : null;
}
