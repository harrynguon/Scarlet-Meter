using ScarletMeter.Engine;

namespace ScarletMeter.App.Roster;

/// <summary>
/// What the overlay's last known values are for (<see cref="RosterService.LastKnown"/>): a character, and the Shugo
/// Festival keys saved for its server, if any.
/// </summary>
public sealed record RosterLastKnown(RosterCharacter Character, RosterShugoKeys? Shugo);

/// <summary>
/// The overlay's Odyle energy and Shugo Festival keys while the game has not sent its own this run, as when the meter
/// starts while the character is already in the game (Harry, 2026-10-28: "can the shugo festival key and odyle energy
/// count be reread without them changing in value?"; the meter only listens, and the game sends them at login and when
/// they change). They are the roster's last readings with what has come back since estimated as the roster estimates it:
/// basic energy +15 every three hours up to the character's cap, the keys +3 at each daily reset up to 21; charged energy
/// and the keys' extra as they were. The overlay marks them ≈ until the game's own replace them. Pure.
/// </summary>
public static class LastKnownValues
{
    /// <summary>The character's Odyle energy now, estimated from its last reading; null when it has none.</summary>
    public static OdyleEnergy? Odyle(RosterCharacter character, DateTimeOffset now, RechargeCalendar calendar) =>
        character.Odyle is { } seen
            ? new OdyleEnergy(RosterRules.OdyleNow(seen, RosterRules.OdyleCap(character), now, calendar).Value, seen.Second)
            : null;

    /// <summary>The server's Shugo Festival keys now, estimated from their last reading; null when there is none.</summary>
    public static ShugoKeys? Shugo(RosterShugoKeys? saved, DateTimeOffset now, RechargeCalendar calendar) =>
        saved is null ? null : new ShugoKeys(RosterRules.ContentNow(RosterRules.Shugo, saved.Count, now, calendar).Value, saved.Extra);
}
