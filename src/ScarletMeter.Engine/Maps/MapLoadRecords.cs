using System.Buffers.Binary;
using ScarletMeter.Engine.Records;

namespace ScarletMeter.Engine.Maps;

/// <summary>
/// Reads the map id from the game's map-loaded record, as worked out by A2Tools (taengu/A2Tools-DPS-Meter b1fcd53,
/// unchanged up to 6abf5e7, GPL-3.0; see THIRD-PARTY-NOTICES.md): <c>&lt;length varint&gt; 21 36 &lt;u32&gt; &lt;map id
/// u32&gt; …</c>, both little-endian. The game sends it on every zone load, naming the map from its Map table: a teleport
/// inside an instance names the instance again, and leaving names an open-world map. A2Tools calls the first u32 a
/// count (1 on entering, 2 after a teleport inside, in its capture) and does not use it; nor does this. What follows the
/// id is not read either (the whole record was 49 bytes, its length 52, on every load A2Tools captured). Pure and
/// allocation-free; every read is bounds-checked, so malformed bytes give no id rather than an exception.
/// </summary>
internal static class MapLoadRecords
{
    /// <summary>The map-loaded record (bytes <c>21 36</c> after the length).</summary>
    public const ushort Opcode = 0x3621;

    // Where the id starts in the body: after the u32 A2Tools calls a count.
    private const int MapIdOffset = 4;

    /// <summary>
    /// The map id in one record (its length varint, two opcode bytes, then its body), or null when the record is
    /// another kind, too short to hold the id, or names 0 or an id past <see cref="int.MaxValue"/>. A2Tools takes any
    /// value (as an i32); Scarlet refuses those two, which no map id A2Tools documents is (all lie between 1 and
    /// 999,999), so neither can pass for a map.
    /// </summary>
    public static int? Read(ReadOnlySpan<byte> record)
    {
        if (!GameRecords.TryOpen(record, out var opcode, out var body) || opcode != Opcode) return null;
        if (body.Length < MapIdOffset + sizeof(uint)) return null;
        var id = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(MapIdOffset, sizeof(uint)));
        return id is > 0 and <= int.MaxValue ? (int)id : null;
    }
}
