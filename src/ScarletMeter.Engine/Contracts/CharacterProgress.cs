namespace ScarletMeter.Engine;

/// <summary>
/// A character the game named as the player's, as upstream read it from the character's own record at login
/// (<see cref="ICombatEngine.Character"/>). The name and server tell characters apart; the combat power is what upstream
/// read when the record came, and the class is upstream's player class when it knows one (from the character's skills in
/// a fight), else the one the record itself names (0, and its name empty, when neither is known).
/// </summary>
/// <param name="Name">The character's name, as upstream decodes it.</param>
/// <param name="Server">The server's short name from upstream's table, or empty when the id is not in it.</param>
/// <param name="ClassId">Upstream's class id (10 to 19), or 0 while not known.</param>
/// <param name="ClassName">The class's AION 2 Global name, or empty while not known.</param>
/// <param name="CombatPower">Combat power as the record gave it, or 0.</param>
/// <param name="ServerId">
/// The server's id as the game numbers servers (1001 and up Elyos, 2001 and up Asmodian; <see cref="AbyssArtifacts.SideOf"/>),
/// or 0 when not known. Upstream's short names repeat across the two sides (SIE is 1001 and 2002), the id does not.
/// </param>
/// <param name="Level">The character's level as the record gave it (1 to 99), or 0 when not known.</param>
/// <param name="GearScore">The gear score, the equipment's item level, as the record gave it, or 0 when not known.</param>
public sealed record CharacterIdentity(
    string Name, string Server, int ClassId, string ClassName, int CombatPower, int ServerId = 0, int Level = 0, int GearScore = 0)
{
    /// <summary>Whether two identities are the same character: the same name on the same server.</summary>
    public bool IsSameCharacter(CharacterIdentity? other) =>
        other is not null
        && string.Equals(Name, other.Name, StringComparison.Ordinal)
        && string.Equals(Server, other.Server, StringComparison.Ordinal);
}

/// <summary>
/// One entry of the game's list of entry counts as last read: its two values and when the game last sent it. What the
/// values mean depends on the entry (<see cref="EntryIds"/>): for Odyle energy basic and charged, for a content the
/// entries left and the extra from recharge items.
/// </summary>
public readonly record struct EntryCount(int First, int Second, DateTimeOffset SeenAt);

/// <summary>
/// The Kina a character holds, as its inventory last said: Kina and Kina (Bound), each null while not known (before the
/// inventory's listing at world entry, until a change of that item), and when it was read.
/// </summary>
public sealed record KinaBalance(long? Kina, long? Bound, DateTimeOffset SeenAt);

/// <summary>
/// The player's entry counts with whose they are (<see cref="ICombatEngine.ProgressObserved"/>): every count the game
/// has sent for this character since its login's list, each with when it was last read, keyed by the game's entry id;
/// and the Kina it holds, once its inventory has said.
/// </summary>
public sealed record CharacterProgress(CharacterIdentity Character, IReadOnlyDictionary<uint, EntryCount> Entries, KinaBalance? Kina = null)
{
    /// <summary>The Odyle energy among the counts, or null when the game has not sent it.</summary>
    public OdyleEnergy? Odyle =>
        Entries.TryGetValue(EntryIds.OdyleEnergy, out var energy) ? new OdyleEnergy(energy.First, energy.Second) : null;
}

/// <summary>
/// Ids in the game's list of entry counts whose meaning is known, from A2Tools (taengu/A2Tools-DPS-Meter and the
/// Roster plugin of taengu/A2Tools-Plugins; THIRD-PARTY-NOTICES.md), each checked there against what the game showed.
/// </summary>
public static class EntryIds
{
    /// <summary>Odyle energy: basic, then charged (A2Tools, 2026-10-14, against the game's tooltip).</summary>
    public const uint OdyleEnergy = 60_000_001;

    /// <summary>Nightmare: entries left, then the extra from recharge items (A2Tools' Roster, 2026-10-24: "Nightmare 1+1").</summary>
    public const uint Nightmare = 4;

    /// <summary>Ascension Trial: entries left, then the extra (A2Tools' Roster, 2026-10-24: "Ascension 3+1").</summary>
    public const uint AscensionTrial = 80_000_001;
}
