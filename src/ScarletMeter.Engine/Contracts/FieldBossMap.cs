namespace ScarletMeter.Engine;

/// <summary>
/// One field boss as the world map listed it: its world-map id (the game's WorldMapFieldNamed id: 101021 is Eternal Gartua
/// in Verteron), whether it was up, and its time: since when it is up, or when it comes back (null when the game gave none).
/// </summary>
public readonly record struct FieldBossOnMap(int Id, bool Up, DateTimeOffset? Time);

/// <summary>
/// The field bosses on a map, as the game told the player when they opened the world map in the game, and when it was read
/// (<see cref="ICombatEngine.FieldBossMapObserved"/>). Only the player's home map and the Abyss come this way. It is the
/// world's on the server it was read on, which the record does not say, so a subscriber puts it on
/// <see cref="ICombatEngine.Character"/>'s.
/// </summary>
public sealed record FieldBossMap(int MapId, IReadOnlyList<FieldBossOnMap> Bosses, DateTimeOffset SeenAt);
