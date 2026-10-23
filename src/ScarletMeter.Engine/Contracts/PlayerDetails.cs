namespace ScarletMeter.Engine;

/// <summary>
/// One player's breakdown of a fight: totals, hit rates, skills, buffs, the damage graph and the newest hits.
/// For the live fight it is a composition of consecutive reads (see <see cref="ICombatEngine.GetPlayerDetails"/>),
/// so a hit landing between two reads can be counted in one part and not yet in another.
/// </summary>
/// <param name="Player">The player's row, as in <see cref="CombatSnapshot.Players"/>.</param>
/// <param name="Duration">
/// The fight's duration, from its first hit by anyone to its last: the window over which buff uptime is measured.
/// <see cref="PlayerRow.Dps"/> is upstream's figure over the player's own first to last hit instead.
/// </param>
/// <param name="Rates">The player's hit rates over direct hits; damage-over-time ticks are not counted.</param>
/// <param name="Skills">Skills by damage, highest first.</param>
/// <param name="Buffs">Buffs the player received during the fight, by uptime, longest first.</param>
/// <param name="CombatLog">
/// The player's newest hits, newest first, at most <see cref="CombatLogLimit"/>. Skills and the graph cover
/// every hit; only the log is capped, to bound what a poll copies.
/// </param>
public sealed record PlayerDetails(
    PlayerRow Player,
    TimeSpan Duration,
    HitRates Rates,
    IReadOnlyList<SkillRow> Skills,
    IReadOnlyList<BuffRow> Buffs,
    DamageGraph Graph,
    IReadOnlyList<CombatLogEntry> CombatLog)
{
    /// <summary>The most hits <see cref="CombatLog"/> carries.</summary>
    public const int CombatLogLimit = 1_000;
}

/// <summary>
/// How often hits carried each flag, as percentages from 0 to 100 of <paramref name="Hits"/>. The flags are
/// counted over direct hits only, as upstream does: a skill's <paramref name="Hits"/> also counts its
/// damage-over-time ticks, so a skill that only ticks shows 0 for every rate.
/// </summary>
public sealed record HitRates(
    int Hits,
    double Critical,
    double BackAttack,
    double FrontAttack,
    double Perfect,
    double DoubleDamage,
    double Parry);

/// <summary>
/// One skill's damage in a fight. With summon grouping on (<see cref="EngineSettings.GroupSummonDamage"/>), a
/// summon's attacks are folded into the owner skill that commands it: <paramref name="IsSummonGroup"/> is
/// true when more than one summon skill was folded in, and <paramref name="SummonChildren"/> lists them.
/// </summary>
/// <param name="SkillId">Upstream's normalised skill id, shared by every specialisation of the skill.</param>
/// <param name="Icon">Upstream's icon string, unchanged (see docs/ARCHITECTURE.md, "Icons"); null when upstream has none.</param>
/// <param name="IsDot">Every hit of the skill was a damage-over-time tick.</param>
/// <param name="SpecializationFlags">
/// The specialisation slots (five) the first hit's raw skill code carried, or empty when it carried none.
/// </param>
/// <param name="DamageShare">Share of the player's damage, 0 to 100.</param>
/// <param name="Dps">The skill's damage over the player's first to last hit, as upstream computes it.</param>
/// <param name="Hits">Hits including damage-over-time ticks.</param>
/// <param name="MinHit">The smallest hit, or 0 when there is none.</param>
public sealed record SkillRow(
    long SkillId,
    string Name,
    string? Icon,
    bool IsDot,
    bool IsSummonGroup,
    IReadOnlyList<bool> SpecializationFlags,
    long TotalDamage,
    double DamageShare,
    double Dps,
    int Hits,
    long MaxHit,
    long MinHit,
    double AverageHit,
    HitRates Rates,
    IReadOnlyList<SkillRow> SummonChildren);

/// <summary>A buff the player received during a fight.</summary>
/// <param name="Icon">Upstream's icon string, unchanged (see docs/ARCHITECTURE.md, "Icons").</param>
/// <param name="Description">Upstream's description; its buff decoder currently always leaves it empty.</param>
/// <param name="Applications">Applications overlapping the fight.</param>
/// <param name="Uptime">Time the buff was active within the fight, overlapping applications counted once.</param>
/// <param name="UptimePercent">Uptime as 0 to 100 of <see cref="PlayerDetails.Duration"/>.</param>
public sealed record BuffRow(
    int BuffId,
    string Name,
    string? Icon,
    string Description,
    int Applications,
    TimeSpan Uptime,
    double UptimePercent);

/// <summary>
/// The player's damage over time, as upstream's GraphDataCalculator computes it. Seconds count from the
/// player's own first hit (not the fight's start, unlike <see cref="CombatLogEntry.Second"/>), and
/// <paramref name="TotalSeconds"/> is the player's first to last hit, at least 0.1 once there is a hit.
/// </summary>
/// <param name="Points">One point per whole second, from 0 to the last second, in order.</param>
/// <param name="Buffs">Buff lanes: overlapping or touching applications of a buff merged into one span, by start.</param>
public sealed record DamageGraph(double TotalSeconds, IReadOnlyList<GraphPoint> Points, IReadOnlyList<BuffSpan> Buffs)
{
    /// <summary>No hits, nothing to draw.</summary>
    public static DamageGraph Empty { get; } = new(0, [], []);
}

/// <summary>
/// One second of the graph. <paramref name="Damage"/> is the damage per second around it, smoothed by upstream
/// over a five-second rolling window (two seconds either side); <paramref name="CumulativeDps"/> is all damage
/// up to the end of this second divided by the time elapsed.
/// </summary>
public sealed record GraphPoint(double Second, long Damage, double CumulativeDps);

/// <summary>A buff active from <paramref name="StartSecond"/> to <paramref name="EndSecond"/>, merged from <paramref name="Count"/> applications.</summary>
public sealed record BuffSpan(int BuffId, string Name, string? Icon, double StartSecond, double EndSecond, int Count);

/// <summary>One hit in a player's combat log.</summary>
/// <param name="Time">When upstream recorded the hit, local time.</param>
/// <param name="Second">Seconds since the fight's first hit.</param>
/// <param name="SkillId">The skill's normalised id, as in <see cref="SkillRow.SkillId"/>, to filter the log by skill.</param>
/// <param name="Icon">Upstream's icon string, unchanged (see docs/ARCHITECTURE.md, "Icons").</param>
/// <param name="SummonName">
/// Set when the player's summon dealt the hit: the summon's name as upstream knows it, which is usually its
/// placeholder ("Player_" and its entity id) since the game does not name summons.
/// </param>
public sealed record CombatLogEntry(
    DateTime Time,
    double Second,
    long SkillId,
    string SkillName,
    string? Icon,
    long Damage,
    HitFlags Flags,
    bool IsDot,
    string? SummonName);

/// <summary>What the game reported about a hit.</summary>
[Flags]
public enum HitFlags
{
    None = 0,
    Critical = 1,
    BackAttack = 2,
    FrontAttack = 4,
    Perfect = 8,
    DoubleDamage = 16,
    Parry = 32,
}
