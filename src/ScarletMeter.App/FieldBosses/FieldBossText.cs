using ScarletMeter.App.Alerts;
using ScarletMeter.WorldEvents.FieldBosses;

namespace ScarletMeter.App.FieldBosses;

/// <summary>Wording for field-boss timers: a boss's short name, its state, where a timer came from, and a server.</summary>
public static class FieldBossText
{
    /// <summary>
    /// A boss's name as the overlay's notice writes it, to fit beside its time: the name before a title ("Kusan the Mad
    /// Gladiator", "Neikel of the East" and "Argo, the Spirit King" give "Kusan", "Neikel" and "Argo"), else its last word
    /// ("Eternal Gartua" gives "Gartua").
    /// </summary>
    public static string Short(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2) return name;
        if (words.Skip(1).Any(word => word is "the" or "of")) return words[0].TrimEnd(',');
        return words[^1];
    }

    /// <summary>
    /// A boss's state for the window at <paramref name="now"/>, with the time left to a respawn as the overlay's notice
    /// counts it: "Respawns in 2h 59m", "Respawns in 14:43", "Up", "Killed, no time given", or "No timer yet". The clock
    /// time is <see cref="Time(FieldBossTimer?, DateTimeOffset)"/>'s, in the column beside it (Harry, 2026-11-09: "say
    /// 'Respawns in ...' and then the timer/countdown, followed by the exact time on the other column").
    /// </summary>
    public static string State(FieldBossTimer? timer, DateTimeOffset now)
    {
        if (timer is null) return "No timer yet";
        var state = timer.StateAt(now);
        return state.Status switch
        {
            FieldBossStatus.Up => "Up",
            FieldBossStatus.Respawning when state.At is { } back => $"Respawns in {EventText.IconTime(back - now)}",
            _ => "Killed, no time given",
        };
    }

    /// <summary>
    /// The clock time beside a boss's state, in the PC's local time: the respawn ("6:01pm"), since when it is up ("since
    /// 2:58pm"), or when it was seen up when the source did not say since when ("seen 2:58pm"); empty with no time.
    /// </summary>
    public static string Time(FieldBossTimer? timer, DateTimeOffset now) => Time(timer, now, TimeZoneInfo.Local);

    /// <summary>The clock time beside a boss's state, as <see cref="Time(FieldBossTimer?, DateTimeOffset)"/>, in <paramref name="zone"/>.</summary>
    public static string Time(FieldBossTimer? timer, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (timer is null) return string.Empty;
        var state = timer.StateAt(now);
        return state.Status switch
        {
            FieldBossStatus.Up when state.At is { } since => "since " + EventText.LocalTime(since, zone),
            FieldBossStatus.Up => "seen " + EventText.LocalTime(timer.ObservedAt, zone),
            FieldBossStatus.Respawning when state.At is { } back => EventText.LocalTime(back, zone),
            _ => string.Empty,
        };
    }

    /// <summary>Where a timer came from and how long ago: "Your map · 3 min ago", "NotMeter · just now".</summary>
    public static string Source(FieldBossTimer timer, DateTimeOffset now)
    {
        var from = timer.Source == FieldBossSource.Map ? "Your map" : "NotMeter";
        return $"{from} · {Ago(now - timer.ObservedAt)}";
    }

    /// <summary>
    /// The window's "Your map" line: that a reading waits for the game to name the character (a meter started mid-session:
    /// Harry, 2026-11-08, read "not read this run" after opening the map), else when the map was last read on the server
    /// shown, or how to read it. A held reading is always the newest: the naming that keeps a reading ends the hold.
    /// </summary>
    public static string MapLine(FieldBossView view, DateTimeOffset now)
    {
        if (view.MapHeldAt is { } held)
            return $"read {Ago(now - held)}, waiting for the game to name your character: change zone or teleport once";
        return view.MapReadAt is { } read ? $"read {Ago(now - read)}" : "not read this run: open the world map (M) in the game to read it";
    }

    /// <summary>How long ago, roughly: "just now", "4 min ago", "2 h ago", "3 d ago".</summary>
    public static string Ago(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.FromMinutes(1)) return "just now";
        if (elapsed < TimeSpan.FromHours(1)) return $"{(int)elapsed.TotalMinutes} min ago";
        if (elapsed < TimeSpan.FromDays(1)) return $"{(int)elapsed.TotalHours} h ago";
        return $"{(int)elapsed.TotalDays} d ago";
    }

    /// <summary>A server for display: its Global region and number ("Asia 1503"), or the number alone ("1003") elsewhere.</summary>
    public static string Server(int serverId) => (NotMeterFieldBossFeed.Region(serverId)) switch
    {
        "nae" => $"NA East {serverId}",
        "naw" => $"NA West {serverId}",
        "eu" => $"Europe {serverId}",
        "la" => $"South America {serverId}",
        "as" => $"Asia {serverId}",
        _ => serverId.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>What a field-boss notice says for screen readers and its tip: "Eternal Gartua respawns at 9:05pm", "Eternal Gartua is up".</summary>
    public static string Notice(FieldBossAlert alert) => alert.Kind == FieldBossAlertKind.Soon
        ? $"{alert.Boss.Name} respawns at {EventText.LocalTime(alert.At)}"
        : $"{alert.Boss.Name} is up";
}
