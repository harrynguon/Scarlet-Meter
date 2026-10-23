using AionDpsMeter.Core.Models;
using AionDpsMeter.Services.Models;
using AionDpsMeter.Services.Services.Session;

namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// Pure conversions from upstream's models to Scarlet's records. Nothing here reads shared state: callers pass
/// what they read under upstream's session lock (the live fight) or from a saved snapshot (history), so live
/// and saved details are built by the same code. Saved fights are deserialised from the history database, so
/// every list is read null-tolerantly: a damaged entry is skipped rather than failing the whole fight.
/// </summary>
internal static class UpstreamMapping
{
    /// <summary>Upstream's player totals as a row. Rates and DPS are upstream's own figures.</summary>
    public static PlayerRow ToPlayerRow(PlayerStats s) => new(
        s.PlayerId, s.PlayerName ?? string.Empty, s.IsUser, s.IsIdentified, s.ClassId, s.ClassName ?? string.Empty,
        s.ServerName ?? string.Empty, s.CombatPower, s.TotalDamage, s.DamagePerSecond, s.DamagePercentage,
        s.CriticalRate, s.PlayerDeaths, s.ClassIcon);

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

    /// <summary>Buff rows by uptime, longest first; uptime percent is of <paramref name="fightDuration"/>.</summary>
    public static IReadOnlyList<BuffRow> ToBuffRows(IEnumerable<BuffStats?>? buffs, TimeSpan fightDuration) =>
        (buffs ?? []).OfType<BuffStats>()
            .Select(b =>
            {
                var uptime = TimeSpan.FromSeconds(Math.Max(0, b.EffectiveDurationSeconds));
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
    /// A saved or running fight with every player's details. The entry's totals and player count are computed
    /// from the snapshot's players the way upstream computes them when it saves a fight.
    /// </summary>
    public static HistoryFight ToHistoryFight(HistorySessionSnapshot snapshot)
    {
        var players = (snapshot.PlayerStats ?? []).OfType<PlayerStats>().DistinctBy(p => p.PlayerId).ToList();
        long totalDamage = players.Sum(p => p.TotalDamage);
        var duration = snapshot.Duration;
        var entry = new HistoryEntry(
            snapshot.SessionId, snapshot.TargetName ?? string.Empty, snapshot.TargetHpTotal,
            AsLocal(snapshot.SessionStart), AsLocal(snapshot.SessionEnd), duration, totalDamage,
            PerSecond(totalDamage, duration), players.Count(p => p.IsIdentified || p.DamagePercentage > 1),
            snapshot.State == SessionState.Completed);

        var details = new Dictionary<long, PlayerDetails>(players.Count);
        foreach (var player in players)
        {
            // Upstream keeps the raw hits newest first, matching the live combat log, which GraphDataCalculator expects.
            var hits = (Lookup(snapshot.HitsByPlayer, player.PlayerId) ?? []).OfType<PlayerDamage>().ToList();
            var buffEvents = (Lookup(snapshot.BuffEventsByPlayer, player.PlayerId) ?? []).OfType<BuffEvent>().ToList();
            details[player.PlayerId] = ToDetails(
                player, duration, snapshot.SessionStart,
                Lookup(snapshot.SkillStatsByPlayer, player.PlayerId) ?? [],
                Lookup(snapshot.BuffStatsByPlayer, player.PlayerId) ?? [],
                GraphDataCalculator.Compute(hits, buffEvents),
                hits);
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

    private static T? Lookup<T>(IReadOnlyDictionary<long, T>? map, long playerId) where T : class =>
        map is not null && map.TryGetValue(playerId, out var value) ? value : null;
}
