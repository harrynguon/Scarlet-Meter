using System.Globalization;
using ScarletMeter.App.Roster;

namespace ScarletMeter.App.Formatting;

/// <summary>The character roster's wording: counts with their estimate mark, time left and when, in the PC's local time.</summary>
public static class RosterText
{
    /// <summary>The mark before a value the game has not sent but recharges likely added to.</summary>
    public const string EstimateMark = "≈";

    /// <summary>A count, marked as an estimate when recharges added to it ("≈ 735", "11").</summary>
    public static string Count(Estimate estimate) =>
        (estimate.Gained > 0 ? EstimateMark + " " : string.Empty) + estimate.Value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>The extra beside a count, as the game writes it ("(+1,200)"), or empty when there is none and <paramref name="always"/> is false.</summary>
    public static string Extra(int extra, bool always) =>
        extra > 0 || always ? "(+" + Math.Max(0, extra).ToString("N0", CultureInfo.InvariantCulture) + ")" : string.Empty;

    /// <summary>
    /// A count's cap after it, straight after the count or its extra: "2/3", "6 (+1)/14" (Harry, 2026-10-28, from the
    /// game's own; 2026-10-30: "remove the space before the / count").
    /// </summary>
    public static string OutOf(int cap) => "/" + cap.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>
    /// The corridors a character's side can enter, after the runs: "/2" once the game has said, or the most there can be
    /// with an asterisk, "/3*", while it has not since the last siege (Harry, 2026-10-30: an asterisk, not italics).
    /// </summary>
    public static string CorridorsOutOf(int? open) =>
        open is { } known ? OutOf(known) : OutOf(RosterRules.CorridorsPerSiege) + "*";

    /// <summary>Time left, to the minute, rounded up so it never reads 0 before the moment: "4d 15h", "12h 16m", "15m"; "now" once it has passed.</summary>
    public static string Until(TimeSpan left)
    {
        if (left <= TimeSpan.Zero) return "now";
        var minutes = (long)Math.Ceiling(left.TotalMinutes);
        var days = minutes / (24 * 60);
        var hours = minutes / 60 % 24;
        var rest = minutes % 60;
        return days > 0 ? $"{days}d {hours}h" : hours > 0 ? $"{hours}h {rest}m" : $"{rest}m";
    }

    /// <summary>An instant in the PC's local time, with the day when it is not today ("01:42", "Thu 30 Oct 07:00").</summary>
    public static string At(DateTimeOffset instant, DateTimeOffset now) =>
        Day(instant, now) is { Length: > 0 } day ? day + " " + Time(instant) : Time(instant);

    /// <summary>
    /// The day of an instant in the PC's local time ("Thu 30 Oct"), or empty when it is today: the roster's Last played
    /// puts it on a line above the time, so a date does not widen the column (Harry, 2026-10-28).
    /// </summary>
    public static string Day(DateTimeOffset instant, DateTimeOffset now)
    {
        var local = instant.ToLocalTime();
        return local.Date == now.ToLocalTime().Date ? string.Empty : local.ToString("ddd d MMM", CultureInfo.InvariantCulture);
    }

    /// <summary>The time of an instant in the PC's local time ("07:00").</summary>
    public static string Time(DateTimeOffset instant) => instant.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>The line under a character's name: server, class and level ("SIE · Sorcerer · Lv 45"), whichever are known.</summary>
    public static string ServerClassAndLevel(RosterCharacter character) =>
        string.Join(" · ", new[] { character.Server, character.ClassName, Level(character.Level) }.Where(part => part.Length > 0));

    /// <summary>A level as the game writes it ("Lv 45"), or empty while not known.</summary>
    public static string Level(int level) =>
        level > 0 ? "Lv " + level.ToString(CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>A whole number with thousands separators ("41,000"), or "?" while not known (0).</summary>
    public static string KnownNumber(int value) =>
        value > 0 ? value.ToString("N0", CultureInfo.InvariantCulture) : "?";
}
