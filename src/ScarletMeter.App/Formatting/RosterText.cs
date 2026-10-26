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
    public static string At(DateTimeOffset instant, DateTimeOffset now)
    {
        var local = instant.ToLocalTime();
        return local.Date == now.ToLocalTime().Date
            ? local.ToString("HH:mm", CultureInfo.InvariantCulture)
            : local.ToString("ddd d MMM HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>The line under a character's name: server and class, whichever are known.</summary>
    public static string ServerAndClass(RosterCharacter character) =>
        string.Join(" · ", new[] { character.Server, character.ClassName }.Where(part => part.Length > 0));
}
