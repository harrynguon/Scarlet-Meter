namespace ScarletMeter.Engine;

/// <summary>
/// The Dimensional Invasion reward keys as the game last sent them (<see cref="EntryIds.DimensionalInvasionKeys"/>): the
/// keys left, which the server's characters share, and the extra from recharge items, if any.
/// </summary>
public readonly record struct InvasionKeys(int Keys, int Extra);