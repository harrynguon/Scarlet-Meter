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
    /// A boss's state for the window, at <paramref name="now"/> in the PC's local time: "Up since 21:05", "Up (seen 21:05)"
    /// when the source did not say since when, "Respawns 21:05", "Killed, no time given", or "No timer yet".
    /// </summary>
    public static string State(FieldBossTimer? timer, DateTimeOffset now)
    {
        if (timer is null) return "No timer yet";
        var state = timer.StateAt(now);
        return state.Status switch
        {
            FieldBossStatus.Up when state.At is { } since => $"Up since {EventText.LocalTime(since)}",
            FieldBossStatus.Up => $"Up (seen {EventText.LocalTime(timer.ObservedAt)})",
            FieldBossStatus.Respawning when state.At is { } back => $"Respawns {EventText.LocalTime(back)}",
            _ => "Killed, no time given",
        };
    }

    /// <summary>The time left to a respawn for the window's countdown ("4:59", "2h 05m"), or null when the boss is not respawning.</summary>
    public static string? Countdown(FieldBossTimer? timer, DateTimeOffset now) =>
        timer?.StateAt(now) is { Status: FieldBossStatus.Respawning, At: { } back } ? EventText.IconTime(back - now) : null;

    /// <summary>Where a timer came from and how long ago: "Your map · 3 min ago", "NotMeter · just now".</summary>
    public static string Source(FieldBossTimer timer, DateTimeOffset now)
    {
        var from = timer.Source == FieldBossSource.Map ? "Your map" : "NotMeter";
        return $"{from} · {Ago(now - timer.ObservedAt)}";
    }

    /// <summary>
    /// The window's "Your map" line: when the map was last read on the server shown, or that a newer reading waits for the game
    /// to name the character (a meter started mid-session: Harry, 2026-11-08, read "not read this run" after opening the
    /// map), or how to read it.
    /// </summary>
    public static string MapLine(FieldBossView view, DateTimeOffset now)
    {
        if (view.MapHeldAt is { } held && (view.MapReadAt is not { } kept || held > kept))
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

    /// <summary>What a field-boss notice says for screen readers and its tip: "Eternal Gartua respawns at 21:05", "Eternal Gartua is up".</summary>
    public static string Notice(FieldBossAlert alert) => alert.Kind == FieldBossAlertKind.Soon
        ? $"{alert.Boss.Name} respawns at {EventText.LocalTime(alert.At)}"
        : $"{alert.Boss.Name} is up";
}
