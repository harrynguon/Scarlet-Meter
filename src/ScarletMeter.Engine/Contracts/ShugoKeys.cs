namespace ScarletMeter.Engine;

/// <summary>
/// The Shugo Festival keys as the game last sent them (<see cref="EntryIds.ShugoFestivalKeys"/>): the keys left, which
/// the server's characters share, and the extra from recharge items, as the game writes them ("12 (+3)").
/// </summary>
public readonly record struct ShugoKeys(int Keys, int Extra);
