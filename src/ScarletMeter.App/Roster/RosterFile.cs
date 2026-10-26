using System.Text.Json.Serialization;

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

    /// <summary>The character's key in the roster (not saved: it is the name and server).</summary>
    [JsonIgnore]
    public RosterKey Key => new(Server, Name);
}

/// <summary>A count as the game last sent it: its two values and when.</summary>
public sealed record RosterCount(int First, int Second, DateTimeOffset SeenAt);

/// <summary>A character's key: the same name on another server is another character.</summary>
public readonly record struct RosterKey(string Server, string Name);
