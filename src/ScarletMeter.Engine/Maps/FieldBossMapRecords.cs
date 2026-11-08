using System.Buffers.Binary;
using ScarletMeter.Engine.Records;

namespace ScarletMeter.Engine.Maps;

/// <summary>One boss in the world map's boss list: its world-map id, whether it is up, and its time (Unix ms; 0 when none).</summary>
internal readonly record struct FieldBossMapEntry(int Id, bool Up, long TimeMs);

/// <summary>The world map's boss list: the two bytes before the map id, the map id and every boss on it.</summary>
internal sealed record FieldBossMapRecord(ushort Header, int MapId, IReadOnlyList<FieldBossMapEntry> Bosses);

/// <summary>
/// Reads the field bosses on a map, as the game sends them when the player opens the world map (bytes <c>01 91</c>), as
/// worked out by A2Tools (taengu/A2Tools-DPS-Meter, <c>src-tauri/src/capture/boss_map.rs</c>, added in 9300bc5 on
/// 2026-10-14, last changed in b8c63f6 on 2026-11-07, GPL-3.0; THIRD-PARTY-NOTICES.md):
/// <code>
/// &lt;length varint&gt; 01 91 &lt;u16, 0 so far&gt; &lt;map id u32&gt; &lt;count varint&gt;
///   count x: &lt;up u8: 0 or 1&gt; &lt;world-map boss id varint&gt; [&lt;x y z f32&gt; when up] [&lt;one byte some bosses carry&gt;] &lt;time u64, unix ms&gt;
/// </code>
/// The time is when an up boss appeared, or when a dead one returns; 0 when the game gives none (the Abyss's scheduled
/// bosses carry 0 or their next spawn, days away). Some bosses carry one more byte before the time and others never do;
/// its meaning is unknown, so the time is read where it makes sense: right after the id (or the position), or one byte
/// later, whichever is a plausible time (or 0) and leaves the next entry starting as an entry does. Only the player's home
/// map and the Abyss come this way: the enemy side's map, viewed from the map's menu, sends nothing; the two bytes before
/// the map id are 0 so far (A2Tools: perhaps the server, when an enemy map is viewed from a rift). Pure; every read is
/// bounds-checked, and a record that does not read cleanly from end to end gives nothing rather than part of a list.
/// </summary>
internal static class FieldBossMapRecords
{
    /// <summary>The world map's boss list (bytes <c>01 91</c> after the length).</summary>
    public const ushort Opcode = 0x9101;

    /// <summary>How far back a time may lie before it is taken for a misread (A2Tools' bound).</summary>
    public static TimeSpan MaxPast { get; } = TimeSpan.FromDays(14);

    /// <summary>
    /// How far ahead a time may lie: a week and a day, since the Abyss's weekly bosses give their next spawn, up to a week
    /// away. A2Tools' three days failed its whole Abyss map once those were five days out (2026-11-08).
    /// </summary>
    public static TimeSpan MaxAhead { get; } = TimeSpan.FromDays(8);

    // A count past this is not a list (A2Tools' bound); the maps list 24 bosses or 8.
    private const uint MaxBosses = 200;

    // A boss up carries its position, three f32, after its id.
    private const int PositionBytes = 12;

    /// <summary>
    /// The map in one record, or null when it is another kind, or does not read cleanly: a map id of 0 or past
    /// <see cref="int.MaxValue"/>, a count of 0 or past 200, an up byte other than 0 or 1, a boss id that is not a varint or
    /// past <see cref="int.MaxValue"/>, or an entry whose time is neither 0 nor within <see cref="MaxPast"/> and
    /// <see cref="MaxAhead"/> of <paramref name="now"/> at either place. The two id bounds are Scarlet's: A2Tools takes any
    /// map id and any boss id that fits a u32; no id it documents comes near either.
    /// </summary>
    public static FieldBossMapRecord? Read(ReadOnlySpan<byte> record, DateTimeOffset now)
    {
        if (!GameRecords.TryOpen(record, out var opcode, out var body) || opcode != Opcode) return null;
        if (body.Length < sizeof(ushort) + sizeof(uint)) return null;
        var header = BinaryPrimitives.ReadUInt16LittleEndian(body);
        var mapId = BinaryPrimitives.ReadUInt32LittleEndian(body[sizeof(ushort)..]);
        if (mapId is 0 or > int.MaxValue) return null;
        var at = sizeof(ushort) + sizeof(uint);
        if (!GameRecords.TryReadVarInt(body, at, out var count, out at) || count is 0 or > MaxBosses) return null;

        var nowMs = now.ToUnixTimeMilliseconds();
        var earliest = nowMs - (long)MaxPast.TotalMilliseconds;
        var latest = nowMs + (long)MaxAhead.TotalMilliseconds;
        var bosses = new List<FieldBossMapEntry>((int)count);
        for (uint n = 0; n < count; n++)
        {
            if (at >= body.Length || body[at] > 1) return null;
            var up = body[at] == 1;
            if (!GameRecords.TryReadVarInt(body, at + 1, out var id, out var afterId) || id > int.MaxValue) return null;
            var timeAt = afterId + (up ? PositionBytes : 0);
            var last = n + 1 == count;
            long? time = null;
            var end = 0;
            // The time right there, or one byte later: the first that is plausible and leaves the next entry where one starts.
            for (var extra = 0; extra <= 1 && time is null; extra++)
            {
                var candidate = timeAt + extra;
                if (candidate < 0 || candidate + sizeof(ulong) > body.Length) break;
                var raw = BinaryPrimitives.ReadUInt64LittleEndian(body.Slice(candidate, sizeof(ulong)));
                if (raw > long.MaxValue) continue;
                var ms = (long)raw;
                var after = candidate + sizeof(ulong);
                if ((ms == 0 || (ms >= earliest && ms <= latest)) && EntryStarts(body, after, last))
                {
                    time = ms;
                    end = after;
                }
            }
            if (time is not { } found) return null;
            bosses.Add(new FieldBossMapEntry((int)id, up, found));
            at = end;
        }
        return new FieldBossMapRecord(header, (int)mapId, bosses);
    }

    // Whether an entry could start at `at`: an up byte of 0 or 1, then an id from 1,000 to below 10,000,000 (A2Tools'
    // test); after the last entry, only that the record holds that much (more may follow).
    private static bool EntryStarts(ReadOnlySpan<byte> body, int at, bool last)
    {
        if (last) return at <= body.Length;
        return at < body.Length && body[at] <= 1
            && GameRecords.TryReadVarInt(body, at + 1, out var id, out _) && id is >= 1_000 and < 10_000_000;
    }
}
