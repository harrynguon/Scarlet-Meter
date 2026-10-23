namespace ScarletMeter.Engine;

/// <summary>Immutable view of the active fight. <see cref="Empty"/> is returned while nothing is being fought.</summary>
public sealed record CombatSnapshot(
    TargetInfo? Target,
    TimeSpan Duration,
    double PartyDps,
    IReadOnlyList<PlayerRow> Players)
{
    public static CombatSnapshot Empty { get; } = new(null, TimeSpan.Zero, 0, []);
}

/// <summary>The mob the player's party is currently fighting.</summary>
public sealed record TargetInfo(int Id, string Name, long HpCurrent, long HpTotal, bool IsBoss)
{
    /// <summary>Remaining HP as 0 to 100, or 0 while the total HP is unknown.</summary>
    public double HpPercent => HpTotal > 0 ? Math.Clamp(HpCurrent * 100.0 / HpTotal, 0, 100) : 0;
}

/// <summary>
/// One player's totals against a target. Players the game has not named yet carry a placeholder name and
/// <see cref="IsIdentified"/> false; <see cref="DamageShare"/> is their share of all damage in percent;
/// <see cref="Dps"/> and <see cref="CriticalRate"/> are upstream's, over the player's own first to last hit
/// and direct hits. <see cref="ClassIcon"/> is upstream's class icon string, unchanged (docs/ARCHITECTURE.md, "Icons").
/// </summary>
public sealed record PlayerRow(
    long Id,
    string Name,
    bool IsUser,
    bool IsIdentified,
    int ClassId,
    string ClassName,
    string ServerName,
    int CombatPower,
    long TotalDamage,
    double Dps,
    double DamageShare,
    double CriticalRate,
    int Deaths,
    string? ClassIcon = null);

/// <summary>
/// Capture status. <paramref name="AdapterName"/> is set once the game's connection has been locked; in
/// <see cref="CaptureMode.Demo"/> it reads "Demo" while the synthetic feed runs.
/// </summary>
public sealed record CaptureState(bool IsCapturing, string? AdapterName);
