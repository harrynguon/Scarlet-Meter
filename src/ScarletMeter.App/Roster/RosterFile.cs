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

    /// <summary>
    /// The Shugo Festival keys on each server the game said them on, one entry per server: the overlay's last known keys
    /// when the meter starts. A file from before them has none; an earlier build passes them over when it reads the file
    /// and leaves them out when it saves it, so they come back with each server's next reading.
    /// </summary>
    public IReadOnlyList<RosterShugoKeys> ShugoKeys { get; init; } = [];
}

/// <summary>How a character's basic Odyle energy cap is chosen.</summary>
public enum OdyleCapChoice
{
    /// <summary>
    /// The default: 840, a membership's (Harry, 2026-10-27). Until then it was 560, or 840 once the character had been seen
    /// with more than 560; the name stays, as saved files have it.
    /// </summary>
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

    /// <summary>The character's level as the game last said at login, or 0 while not known.</summary>
    public int Level { get; init; }

    /// <summary>The gear score (the equipment's item level) as the game last said at login, or 0 while not known.</summary>
    public int GearScore { get; init; }

    /// <summary>When the game last named this character as the player's.</summary>
    public DateTimeOffset LastSeen { get; init; }

    /// <summary>Odyle energy: <see cref="RosterCount.First"/> basic, <see cref="RosterCount.Second"/> charged.</summary>
    public RosterCount? Odyle { get; init; }

    /// <summary>Nightmare: entries left, then the extra from recharge items.</summary>
    public RosterCount? Nightmare { get; init; }

    /// <summary>Ascension Trial: entries left, then the extra from recharge items.</summary>
    public RosterCount? Ascension { get; init; }

    /// <summary>
    /// How the basic energy cap is chosen; the player can change it. (A file from before 840 was the default also has
    /// <c>membershipSeen</c>, which the automatic cap no longer needs: it is passed over when read and not written again.)
    /// </summary>
    public OdyleCapChoice OdyleCap { get; init; }

    /// <summary>
    /// When the character entered the Artifact Corridor, as the meter saw it, oldest first: the most recent
    /// <see cref="RosterRules.CorridorRunsKept"/>. The roster counts those since the last Abyss Siege.
    /// </summary>
    public IReadOnlyList<DateTimeOffset> CorridorRuns { get; init; } = [];

    /// <summary>
    /// The character's Abyss passages as the game last said all three, or null while it has not (the roster counts the
    /// corridors used from them as well, <see cref="RosterRules.CorridorsDone"/>).
    /// </summary>
    public RosterPassages? Passages { get; init; }

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
        && CombatPower == other.CombatPower && Level == other.Level && GearScore == other.GearScore && LastSeen == other.LastSeen
        && Equals(Odyle, other.Odyle) && Equals(Nightmare, other.Nightmare) && Equals(Ascension, other.Ascension)
        && OdyleCap == other.OdyleCap
        && CorridorRuns.SequenceEqual(other.CorridorRuns) && Equals(Passages, other.Passages) && Equals(Kina, other.Kina) && Note == other.Note;

    public override int GetHashCode() => HashCode.Combine(Name, Server, LastSeen, CorridorRuns.Count);
}

/// <summary>A count as the game last sent it: its two values and when.</summary>
public sealed record RosterCount(int First, int Second, DateTimeOffset SeenAt);

/// <summary>
/// A character's Abyss passages, its entries to the Artifact Corridors, as the game last said all three
/// (<see cref="EntryIds.AbyssPassages"/>): which were used, in the artifacts' order (1001 to 1003), when the oldest and
/// the latest of the three were read (the game sends each as it changes, so a reading can mix a passage from before a
/// siege with one from after: deep audit and Codex on #66), and when one last had time left, which says the passages had
/// been handed out for the holding then (before they are, all three read as used). Untrusted on load
/// (<see cref="RosterNormalizer"/>).
/// </summary>
/// <param name="Used">One flag per passage, <see cref="RosterRules.CorridorsPerSiege"/> of them: true for used.</param>
/// <param name="ReadFrom">When the oldest of the three was read: the reading says nothing of the time before it.</param>
/// <param name="SeenAt">When the latest of the three was read: which of two readings is newer.</param>
/// <param name="LeftSeenAt">When one last had time left, or null if none has been seen so.</param>
public sealed record RosterPassages(IReadOnlyList<bool> Used, DateTimeOffset ReadFrom, DateTimeOffset SeenAt, DateTimeOffset? LeftSeenAt)
{
    /// <summary>Equal when every value is, the flags compared one by one (a record compares a list by reference).</summary>
    public bool Equals(RosterPassages? other) =>
        other is not null && Used.SequenceEqual(other.Used) && ReadFrom == other.ReadFrom && SeenAt == other.SeenAt && LeftSeenAt == other.LeftSeenAt;

    public override int GetHashCode() => HashCode.Combine(Used.Count, ReadFrom, SeenAt, LeftSeenAt);
}

/// <summary>The Kina a character holds: Kina and Kina (Bound), each null while not known, and when the inventory said it.</summary>
public sealed record RosterKina(long? Kina, long? Bound, DateTimeOffset SeenAt);

/// <summary>
/// The Shugo Festival keys on one server, as the game last said there (they are the server's, shared by its
/// characters): the keys left, the extra from recharge items, and when. Untrusted on load (<see cref="RosterNormalizer"/>).
/// </summary>
/// <param name="ServerId">The server's id (Elyos 1001 and up, Asmodian 2001 and up, Global's among them).</param>
public sealed record RosterShugoKeys(int ServerId, int Keys, int Extra, DateTimeOffset SeenAt)
{
    /// <summary>The keys as a count, for the roster's estimates (<see cref="RosterRules.ContentNow"/>).</summary>
    [JsonIgnore]
    public RosterCount Count => new(Keys, Extra, SeenAt);
}

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
