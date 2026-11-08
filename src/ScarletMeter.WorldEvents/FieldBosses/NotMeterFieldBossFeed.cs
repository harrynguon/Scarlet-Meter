using System.Text.Json;

namespace ScarletMeter.WorldEvents.FieldBosses;

/// <summary>
/// NotMeter's shared field-boss timers: what NotMeter users' meters read off their world maps, which NotMeter's service
/// merges and publishes as one public file per Global region on GitHub (Not4You-Dev/NotMeter-Cache,
/// <c>presence/notmeter-field-boss-public-ww-&lt;region&gt;.json</c>; Asia, NA East and Europe as an "-expanded" file, which
/// lists every open server of the region). The format, as published on 2026-11-08 (no other documentation exists):
/// <code>
/// { "schema": "notmeter-global-field-boss-public-cache-v1", "version": 1, "serviceRegion": "WW", "globalRegion": "as",
///   "generatedAt": &lt;unix s&gt;, "servers": [ { "serverId": 1501, "maps": [ { "mapId": 1010, "observedAt": &lt;unix s&gt;,
///   "entries": [ { "observedAtMs": &lt;unix ms&gt;, "spawned": true, "bossCode": &lt;NPC code&gt;, "targetAt": &lt;unix ms&gt; } ] } ] } ] }
/// </code>
/// A boss down has <c>targetAt</c>, its respawn; one up has <c>spawned</c>, and its <c>targetAt</c> is when it was seen,
/// not when it appeared, so it is not read. Nothing in the file is about a player. Pure: the file is read here, fetched by
/// the host.
/// </summary>
public static class NotMeterFieldBossFeed
{
    /// <summary>The <c>schema</c> the reader takes; a file with any other is not read.</summary>
    public const string Schema = "notmeter-global-field-boss-public-cache-v1";

    /// <summary>How far back an observation or a time may lie, as A2Tools bounds the map's times (a misread otherwise).</summary>
    public static TimeSpan MaxPast { get; } = TimeSpan.FromDays(14);

    /// <summary>
    /// How far ahead a respawn may lie: a week and a day, as A2Tools bounds the map's (the Abyss's weekly bosses give their
    /// next spawn, days away).
    /// </summary>
    public static TimeSpan MaxAhead { get; } = TimeSpan.FromDays(8);

    /// <summary>How far ahead of this PC's clock an observation may be dated (another PC's clock) before it is refused.</summary>
    public static TimeSpan MaxObservedAhead { get; } = TimeSpan.FromDays(1);

    // Bounds on what is read, so a hostile or broken file costs little: the published files list at most 46 servers,
    // 3 maps a server and 24 bosses a map.
    private const int MaxServers = 500;
    private const int MaxMaps = 16;
    private const int MaxEntries = 256;

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 16 };

    /// <summary>
    /// The feed's region code for a Global server id (faction digit 1 or 2, then the region: 1 NA East "nae", 2 NA West
    /// "naw", 3 Europe "eu", 4 South America "la", 5 Asia "as", then a slot from 1), or null for any other id (Korea's and
    /// Taiwan's servers among them: NotMeter publishes no shared timers for them).
    /// </summary>
    public static string? Region(int serverId)
    {
        if (serverId / 1000 is not (1 or 2) || serverId % 100 == 0) return null;
        return (serverId / 100 % 10) switch
        {
            1 => "nae",
            2 => "naw",
            3 => "eu",
            4 => "la",
            5 => "as",
            _ => null,
        };
    }

    /// <summary>The address of the file with a Global server's timers, or null for a server with none (<see cref="Region"/>).</summary>
    public static Uri? Address(int serverId)
    {
        if (Region(serverId) is not { } region) return null;
        var file = region is "as" or "nae" or "eu" ? $"{region}-expanded" : region;
        return new Uri($"https://raw.githubusercontent.com/Not4You-Dev/NotMeter-Cache/main/presence/notmeter-field-boss-public-ww-{file}.json");
    }

    /// <summary>
    /// Reads one server's field bosses from a published file. True with the observations (none when the file does not
    /// list the server yet), or false with a one-sentence reason when the file is not NotMeter's global field-boss file or
    /// does not read. Entries for a boss the catalog does not know, on another map than its own, or with an observation or a
    /// time outside the bounds (<see cref="MaxPast"/>, <see cref="MaxAhead"/>, <see cref="MaxObservedAhead"/>) are left
    /// out; of two for the same boss, the newer stands.
    /// </summary>
    public static bool TryRead(ReadOnlyMemory<byte> utf8Json, int serverId, DateTimeOffset now, out IReadOnlyList<FieldBossTimer> timers, out string? problem)
    {
        timers = [];
        problem = null;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8Json, Options);
        }
        catch (JsonException)
        {
            problem = "The shared timers file is not valid JSON.";
            return false;
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || String(root, "schema") != Schema
                || String(root, "serviceRegion") != "WW"
                || !root.TryGetProperty("servers", out var servers) || servers.ValueKind != JsonValueKind.Array)
            {
                problem = "The shared timers file is not NotMeter's global field boss file.";
                return false;
            }
            if (servers.GetArrayLength() > MaxServers)
            {
                problem = "The shared timers file lists more servers than expected.";
                return false;
            }
            var found = new Dictionary<int, FieldBossTimer>();
            foreach (var server in servers.EnumerateArray())
            {
                if (Int(server, "serverId") != serverId) continue;
                if (!server.TryGetProperty("maps", out var maps) || maps.ValueKind != JsonValueKind.Array || maps.GetArrayLength() > MaxMaps) continue;
                foreach (var map in maps.EnumerateArray())
                {
                    if (Int(map, "mapId") is not { } mapId || FieldBossMaps.NameOf(mapId) is null) continue;
                    if (!map.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > MaxEntries) continue;
                    foreach (var entry in entries.EnumerateArray())
                    {
                        if (Entry(entry, mapId, now) is { } timer
                            && (!found.TryGetValue(timer.BossId, out var held) || timer.ObservedAt > held.ObservedAt))
                            found[timer.BossId] = timer;
                    }
                }
            }
            timers = [.. found.Values.OrderBy(timer => timer.BossId)];
            return true;
        }
    }

    // One entry as an observation, or null when it names no boss of this map or a time is out of bounds.
    private static FieldBossTimer? Entry(JsonElement entry, int mapId, DateTimeOffset now)
    {
        if (entry.ValueKind != JsonValueKind.Object) return null;
        if (Int(entry, "bossCode") is not { } code || FieldBossCatalog.FindByNpcCode(code) is not { } boss || boss.MapId != mapId) return null;
        if (Instant(entry, "observedAtMs") is not { } observed || observed < now - MaxPast || observed > now + MaxObservedAhead) return null;
        var up = entry.TryGetProperty("spawned", out var spawned) && spawned.ValueKind == JsonValueKind.True;
        if (up) return new FieldBossTimer(boss.Id, true, null, observed, FieldBossSource.NotMeter);
        DateTimeOffset? back = Instant(entry, "targetAt") is { } target && target >= now - MaxPast && target <= now + MaxAhead ? target : null;
        return new FieldBossTimer(boss.Id, false, back, observed, FieldBossSource.NotMeter);
    }

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Int(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;

    // A Unix time in milliseconds, or null when absent, not a whole number, 0 or out of DateTimeOffset's range.
    private static DateTimeOffset? Instant(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var ms) || ms <= 0) return null;
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(ms);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
