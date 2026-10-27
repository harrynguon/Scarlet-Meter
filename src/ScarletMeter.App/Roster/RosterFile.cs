using System.Text.Json.Serialization;
using ScarletMeter.Engine;

namespace ScarletMeter.App.Roster;

/// <summary>
/// The characters the meter has seen the player log in on, as saved in the data directory (roster.json, or
/// demo-roster.json for the demo): the player's own progress, kept on this PC only and never logged. Untrusted on load
/// (<see cref="RosterNormalizer"/>).
/// </summary>
public sealed record RosterFile
{
    /// <summary>This build's format. A file with a higher number is used but never overwritten.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public IReadOnlyList<RosterCharacter> Characters { get; init; } = [];

    /// <summary>Who held the Abyss's lower-layer artifacts on each server the game said it on: one entry per server.</summary>
    public IReadOnlyList<RosterArtifacts> Artifacts { get; init; } = [];
}

/// <summary>How a character's basic Odyle energy cap is chosen.</summary>
public enum OdyleCapChoice
{
    /// <summary>560, or 840 once the character has been seen with more than 560 (a membership).</summary>
    Automatic,
    /// <summary>560: the player says there is no membership.</summary>
    Standard,
    /// <summary>840: the player says there is a membership.</summary>
    Membership,
}

/// <summary>
/// One character and what the game last said of it. A count is as the game last sent it, with when: the roster's
/// estimates start from there (<see cref="RosterRules"/>).
/// </summary>
public sealed record RosterCharacter
{
    /// <summary>The character's name, which with <see cref="Server"/> tells characters apart.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The server's short name as upstream gives it, or empty when not known.</summary>
    public string Server { get; init; } = string.Empty;

    /// <summary>
    /// The server's id (1001 and up Elyos, 2001 and up Asmodian), or 0 while not known: the character's side, and where
    /// the artifact holders that open its corridors are kept (<see cref="RosterFile.Artifacts"/>).
    /// </summary>
    public int ServerId { get; init; }

    /// <summary>The class (10 to 19), or 0 while not known.</summary>
    public int ClassId { get; init; }

    /// <summary>The class's Global name, or empty while not known.</summary>
    public string ClassName { get; init; } = string.Empty;

    /// <summary>Combat power as the game last said, or 0.</summary>
    public int CombatPower { get; init; }

    /// <summary>When the game last named this character as the player's.</summary>
    public DateTimeOffset LastSeen { get; init; }

    /// <summary>Odyle energy: <see cref="RosterCount.First"/> basic, <see cref="RosterCount.Second"/> charged.</summary>
    public RosterCount? Odyle { get; init; }

    /// <summary>Nightmare: entries left, then the extra from recharge items.</summary>
    public RosterCount? Nightmare { get; init; }

    /// <summary>Ascension Trial: entries left, then the extra from recharge items.</summary>
    public RosterCount? Ascension { get; init; }

    /// <summary>How the basic energy cap is chosen; the player can change it.</summary>
    public OdyleCapChoice OdyleCap { get; init; }

    /// <summary>Whether more basic energy than the standard cap has ever been seen, which only a membership allows.</summary>
    public bool MembershipSeen { get; init; }

    /// <summary>
    /// When the character entered the Artifact Corridor, as the meter saw it, oldest first: the most recent
    /// <see cref="RosterRules.CorridorRunsKept"/>. The roster counts those since the last Abyss Siege.
    /// </summary>
    public IReadOnlyList<DateTimeOffset> CorridorRuns { get; init; } = [];

    /// <summary>The Kina the character holds, as its inventory last said, and when.</summary>
    public RosterKina? Kina { get; init; }

    /// <summary>
    /// The player's own note on the character (a role, a plan), empty for none: kept as
    /// <see cref="RosterNormalizer.CleanNote"/> leaves it, shown in its row, never logged.
    /// </summary>
    public string Note { get; init; } = string.Empty;

    /// <summary>The character's key in the roster (not saved: it is the name and server).</summary>
    [JsonIgnore]
    public RosterKey Key => new(Server, Name);

    /// <summary>Equal when every value is, the corridor runs compared one by one (a record compares a list by reference).</summary>
    public bool Equals(RosterCharacter? other) =>
        other is not null
        && Name == other.Name && Server == other.Server && ServerId == other.ServerId && ClassId == other.ClassId && ClassName == other.ClassName
        && CombatPower == other.CombatPower && LastSeen == other.LastSeen
        && Equals(Odyle, other.Odyle) && Equals(Nightmare, other.Nightmare) && Equals(Ascension, other.Ascension)
        && OdyleCap == other.OdyleCap && MembershipSeen == other.MembershipSeen
        && CorridorRuns.SequenceEqual(other.CorridorRuns) && Equals(Kina, other.Kina) && Note == other.Note;

    public override int GetHashCode() => HashCode.Combine(Name, Server, LastSeen, CorridorRuns.Count);
}

/// <summary>A count as the game last sent it: its two values and when.</summary>
public sealed record RosterCount(int First, int Second, DateTimeOffset SeenAt);

/// <summary>The Kina a character holds: Kina and Kina (Bound), each null while not known, and when the inventory said it.</summary>
public sealed record RosterKina(long? Kina, long? Bound, DateTimeOffset SeenAt);

/// <summary>
/// Who held the Abyss's lower-layer artifacts, the Artifact Corridors', on one server, as the game last said there
/// (<see cref="AbyssArtifacts"/>): the holders of artifacts 1001 to 1003 in that order (<see cref="ArtifactHolder.Unknown"/>
/// for one it did not say), the holding's start and reset when the game sent them, and when it said so. Untrusted on
/// load (<see cref="RosterNormalizer"/>).
/// </summary>
public sealed record RosterArtifacts
{
    /// <summary>The server's id (1001 and up Elyos, 2001 and up Asmodian).</summary>
    public int ServerId { get; init; }

    /// <summary>The holders of artifacts 1001, 1002 and 1003, in that order.</summary>
    public IReadOnlyList<ArtifactHolder> Holders { get; init; } = [];

    /// <summary>When the holding began, or null when the game did not say.</summary>
    public DateTimeOffset? Since { get; init; }

    /// <summary>When the holding resets (the next siege), or null when the game did not say.</summary>
    public DateTimeOffset? Until { get; init; }

    /// <summary>When the game said it.</summary>
    public DateTimeOffset SeenAt { get; init; }

    /// <summary>Equal when every value is, the holders compared one by one (a record compares a list by reference).</summary>
    public bool Equals(RosterArtifacts? other) =>
        other is not null && ServerId == other.ServerId && Holders.SequenceEqual(other.Holders)
        && Since == other.Since && Until == other.Until && SeenAt == other.SeenAt;

    public override int GetHashCode() => HashCode.Combine(ServerId, SeenAt, Holders.Count);
}

/// <summary>A character's key: the same name on another server is another character.</summary>
public readonly record struct RosterKey(string Server, string Name);
