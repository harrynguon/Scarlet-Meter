using ScarletMeter.Engine;

namespace ScarletMeter.App.Roster;

/// <summary>
/// What the overlay's last known values are for (<see cref="RosterService.LastKnown"/>): a character, and the Shugo
/// Festival keys and the Dimensional Invasion keys saved for its server, if any.
/// </summary>
public sealed record RosterLastKnown(RosterCharacter Character, RosterServerKeys? Shugo, RosterServerKeys? Invasion = null);

/// <summary>
/// The overlay's Odyle energy, Shugo keys and Dimensional Invasion keys, each null when there is none, and whether each is
/// the roster's last known.
/// </summary>
public readonly record struct OverlayEnergy(
    OdyleEnergy? Odyle, bool OdyleLastKnown, ShugoKeys? Shugo, bool ShugoLastKnown, InvasionKeys? Invasion = null, bool InvasionLastKnown = false);

/// <summary>
/// The recharge calendar for the server offset set in Settings, made again only when that offset changes: the overlay
/// asks for it ten times a second while it shows last known values.
/// </summary>
public sealed class RechargeCalendarCache
{
    private RechargeCalendar? calendar;
    private double offsetHours;

    /// <summary>The calendar at <paramref name="serverOffsetHours"/>: the one kept, unless the offset changed since.</summary>
    public RechargeCalendar For(double serverOffsetHours)
    {
        if (calendar is null || offsetHours != serverOffsetHours)
        {
            calendar = RechargeCalendar.ForServerOffsetHours(serverOffsetHours);
            offsetHours = serverOffsetHours;
        }
        return calendar;
    }
}

/// <summary>
/// The overlay's Odyle energy and Shugo Festival keys while the game has not sent its own this run, as when the meter
/// starts while the character is already in the game (Harry, 2026-10-28: "can the shugo festival key and odyle energy
/// count be reread without them changing in value?"; the meter only listens, and the game sends them at login and when
/// they change). They are the roster's last readings with what has come back since estimated as the roster estimates it:
/// basic energy +15 every three hours up to the character's cap, the Shugo keys +3 at each daily reset up to 21, the
/// Dimensional Invasion keys +1 up to 7; charged energy and the keys' extras as they were. The overlay marks them ≈ until
/// the game's own replace them. Pure.
/// </summary>
public static class LastKnownValues
{
    /// <summary>
    /// What the overlay shows: the game's Odyle energy, Shugo keys and Dimensional Invasion keys where it has sent them this
    /// run, each on its own; for one it has not, the roster's last known, estimated, flagged as such.
    /// <paramref name="lastKnown"/> is asked only when one of them is missing (the roster's lock, at the overlay's ten reads
    /// a second).
    /// </summary>
    public static OverlayEnergy Shown(
        OdyleEnergy? gameOdyle, ShugoKeys? gameShugo, InvasionKeys? gameInvasion, Func<RosterLastKnown?> lastKnown, DateTimeOffset now, RechargeCalendar calendar)
    {
        if (gameOdyle is not null && gameShugo is not null && gameInvasion is not null) return new(gameOdyle, false, gameShugo, false, gameInvasion, false);
        var last = lastKnown();
        var odyle = gameOdyle ?? (last is null ? null : Odyle(last.Character, now, calendar));
        var shugo = gameShugo ?? (last is null ? null : Shugo(last.Shugo, now, calendar));
        var invasion = gameInvasion ?? (last is null ? null : Invasion(last.Invasion, now, calendar));
        return new(odyle, gameOdyle is null && odyle is not null, shugo, gameShugo is null && shugo is not null,
            invasion, gameInvasion is null && invasion is not null);
    }

    /// <summary>The character's Odyle energy now, estimated from its last reading; null when it has none.</summary>
    public static OdyleEnergy? Odyle(RosterCharacter character, DateTimeOffset now, RechargeCalendar calendar) =>
        character.Odyle is { } seen
            ? new OdyleEnergy(RosterRules.OdyleNow(seen, RosterRules.OdyleCap(character), now, calendar).Value, seen.Second)
            : null;

    /// <summary>The server's Shugo Festival keys now, estimated from their last reading; null when there is none.</summary>
    public static ShugoKeys? Shugo(RosterServerKeys? saved, DateTimeOffset now, RechargeCalendar calendar) =>
        saved is null ? null : new ShugoKeys(RosterRules.ContentNow(RosterRules.Shugo, saved.Count, now, calendar).Value, saved.Extra);

    /// <summary>The server's Dimensional Invasion keys now, estimated from their last reading; null when there is none.</summary>
    public static InvasionKeys? Invasion(RosterServerKeys? saved, DateTimeOffset now, RechargeCalendar calendar) =>
        saved is null ? null : new InvasionKeys(RosterRules.ContentNow(RosterRules.Invasion, saved.Count, now, calendar).Value, saved.Extra);
}
