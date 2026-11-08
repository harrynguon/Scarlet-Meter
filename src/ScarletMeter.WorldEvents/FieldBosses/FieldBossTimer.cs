namespace ScarletMeter.WorldEvents.FieldBosses;

/// <summary>Where an observation of a field boss came from.</summary>
public enum FieldBossSource
{
    /// <summary>The player's own world map, read by the meter when they opened it in the game.</summary>
    Map,

    /// <summary>NotMeter's shared timers: what other players' meters read off their maps, published per region.</summary>
    NotMeter,
}

/// <summary>What is known of a field boss at an instant (<see cref="FieldBossTimer.StateAt"/>).</summary>
public enum FieldBossStatus
{
    /// <summary>Down, with no respawn time given.</summary>
    Dead,

    /// <summary>Down, coming back at <see cref="FieldBossState.At"/>.</summary>
    Respawning,

    /// <summary>Up: seen up, or its respawn time has passed (since <see cref="FieldBossState.At"/> when known).</summary>
    Up,
}

/// <summary>A field boss's status at an instant, with the instant it is about: the respawn, or since when it is up (null when not known).</summary>
public readonly record struct FieldBossState(FieldBossStatus Status, DateTimeOffset? At);

/// <summary>
/// One observation of a field boss, as a map or a feed gave it: whether it was up and, for a boss that was up, since when
/// (null when not given), or for one that was down, when it comes back (null when not given); when it was observed; and
/// where it came from. Instants are UTC.
/// </summary>
public sealed record FieldBossTimer(int BossId, bool Up, DateTimeOffset? Time, DateTimeOffset ObservedAt, FieldBossSource Source)
{
    /// <summary>
    /// What the observation says at <paramref name="now"/>. A boss down with a respawn time is up once that time has passed,
    /// up since it, as A2Tools' Event Timers reads it: the time is the game's own, and nobody may have opened the map since.
    /// </summary>
    public FieldBossState StateAt(DateTimeOffset now)
    {
        if (Up) return new(FieldBossStatus.Up, Time);
        if (Time is not { } back) return new(FieldBossStatus.Dead, null);
        return back > now ? new(FieldBossStatus.Respawning, back) : new(FieldBossStatus.Up, back);
    }
}

/// <summary>Combines observations of field bosses from every source: for each boss, the newest observation stands.</summary>
public static class FieldBossBoard
{
    /// <summary>
    /// The newest observation of each boss in <paramref name="observations"/>, by world-map id. An observation dated after
    /// <paramref name="now"/> (another PC's clock ahead of this one) counts as made now, so it cannot outrank the player's
    /// own map read a moment ago; between observations of the same instant, the player's map wins.
    /// </summary>
    public static IReadOnlyDictionary<int, FieldBossTimer> Merge(IEnumerable<FieldBossTimer> observations, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(observations);
        var board = new Dictionary<int, FieldBossTimer>();
        foreach (var observation in observations)
        {
            if (observation is null) continue;
            if (!board.TryGetValue(observation.BossId, out var held) || Outranks(observation, held, now))
                board[observation.BossId] = observation;
        }
        return board;
    }

    private static bool Outranks(FieldBossTimer candidate, FieldBossTimer held, DateTimeOffset now)
    {
        var candidateAt = Min(candidate.ObservedAt, now);
        var heldAt = Min(held.ObservedAt, now);
        if (candidateAt != heldAt) return candidateAt > heldAt;
        return candidate.Source == FieldBossSource.Map && held.Source != FieldBossSource.Map;
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a <= b ? a : b;
}
