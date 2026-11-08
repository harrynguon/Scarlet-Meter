namespace ScarletMeter.Engine;

/// <summary>
/// What a record about the player's pets said (<see cref="ICombatEngine.PetRecordObserved"/>). A pet collection is a
/// server's, shared by every character on it; the records do not say which server, so a subscriber puts each on the
/// server of <see cref="ICombatEngine.Character"/>. Pet ids are the game's (1001 and up); an id the meter has no name for
/// (a pet newer than its catalog) is passed on as it came.
/// </summary>
/// <param name="SeenAt">When the meter read the record.</param>
public abstract record PetRecord(DateTimeOffset SeenAt);

/// <summary>
/// The whole collection, which the game sends at login (<c>00 90</c>): each pet the player has, with its level, and each
/// pet's soul points toward its next level. A pet with no points may be missing from the second list.
/// </summary>
public sealed record PetCollectionRead(IReadOnlyList<PetLevel> Levels, IReadOnlyList<PetSouls> Points, DateTimeOffset SeenAt) : PetRecord(SeenAt);

/// <summary>
/// Souls picked up (<c>0D 90</c>), the moment they are: one kill can give souls to several pets, each with how many it
/// gave (a monster's drop 1, a Pet Soul Chest more).
/// </summary>
public sealed record PetSoulsGained(IReadOnlyList<PetSouls> Souls, DateTimeOffset SeenAt) : PetRecord(SeenAt);

/// <summary>
/// A pet reached a level (<c>14 90</c>), or became the player's at a level, from a chest (<c>09 90</c>,
/// <paramref name="Unlocked"/>). The game does not send a level-up for every step (A2Tools), so a subscriber counting souls
/// levels a pet itself too.
/// </summary>
public sealed record PetLevelReached(int PetId, int Level, bool Unlocked, DateTimeOffset SeenAt) : PetRecord(SeenAt);

/// <summary>A pet and its level (0 to 3).</summary>
public readonly record struct PetLevel(int PetId, int Level);

/// <summary>A pet and a number of souls: its points toward its next level (in a collection), or the souls just gained.</summary>
public readonly record struct PetSouls(int PetId, int Souls);
