namespace ScarletMeter.App.Pets;

/// <summary>
/// The player's pet counts as saved in the data directory (pets.json, or demo-pets.json for the demo), one entry per server
/// whose pets the meter has seen: the player's own progress, kept on this PC only and never logged. Untrusted on load
/// (<see cref="PetNormalizer"/>).
/// </summary>
public sealed record PetFile
{
    /// <summary>This build's format. A file with a higher number is used but never overwritten.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public IReadOnlyList<PetServer> Servers { get; init; } = [];
}

/// <summary>
/// One server's pet collection, which every character on it shares: each pet the meter has a count for, when the game last
/// sent the whole collection there, and when a record last changed it.
/// </summary>
public sealed record PetServer
{
    /// <summary>The server's id (Elyos 1001 and up, Asmodian 2001 and up).</summary>
    public int ServerId { get; init; }

    /// <summary>Each pet's level and souls toward its next, by pet id.</summary>
    public IReadOnlyList<PetEntry> Pets { get; init; } = [];

    /// <summary>
    /// When the game last sent the whole collection on this server (at a login with the meter running), or null when it
    /// never has: the counts then come from pickups alone and may be short.
    /// </summary>
    public DateTimeOffset? SyncedAt { get; init; }

    /// <summary>When a record last changed the counts.</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Equal when every value is, the pets compared one by one (a record compares a list by reference).</summary>
    public bool Equals(PetServer? other) =>
        other is not null && ServerId == other.ServerId && Pets.SequenceEqual(other.Pets) && SyncedAt == other.SyncedAt && UpdatedAt == other.UpdatedAt;

    public override int GetHashCode() => HashCode.Combine(ServerId, Pets.Count, SyncedAt, UpdatedAt);
}

/// <summary>A pet's saved count: its id, its level (0 to 3) and its souls toward its next level.</summary>
public sealed record PetEntry(int Id, int Level, int Souls);
