using System.Buffers.Binary;
using ScarletMeter.Engine.Records;

namespace ScarletMeter.Engine.Entries;

/// <summary>One count read from the game's list of entry counts: the entry's id and its two values.</summary>
internal readonly record struct EntryValue(uint Id, int First, int Second);

/// <summary>
/// The counts one record of the list carries, and whether it is the whole list (sent at login and after a server
/// transfer) or the entries that changed (a raid, a recharge item, a key used).
/// </summary>
internal sealed record EntryRecord(IReadOnlyList<EntryValue> Counts, bool IsWholeList);

/// <summary>
/// Reads the game's list of entry counts, as A2Tools reads it (taengu/A2Tools-DPS-Meter b79969a, 2149936 and caa7ed4,
/// unchanged up to b933531; GPL-3.0; THIRD-PARTY-NOTICES.md). An entry is <c>&lt;kind u8&gt; &lt;id u32 LE&gt;
/// &lt;values&gt;</c>: kind <c>00</c> no value (both 0), <c>01</c> a u64 (a time, such as an Abyss passage's usage
/// time left, not a count), <c>04</c> the first value, <c>08</c> the second, <c>0c</c> both, as varints. Record
/// <c>0b 61</c> is the whole list, <c>&lt;count varint&gt; &lt;entries&gt;</c> running to the end of the record after
/// other data; record <c>0c 61</c> is <c>&lt;count u8&gt; &lt;entries&gt; &lt;tail&gt;</c>, the entries that changed.
/// Odyle energy is id 60000001, so <c>0c 01 87 93 03 &lt;basic&gt; &lt;charged&gt;</c> (the "slot 1, key 87 93 03"
/// of the first port, A2Tools #63, was this id). Time entries are read past and not kept. Pure; every read is
/// bounds-checked, so malformed bytes give no value rather than an exception.
/// </summary>
internal static class EntryListRecords
{
    /// <summary>The whole list (bytes <c>0b 61</c> after the length).</summary>
    public const ushort ListOpcode = 0x610B;

    /// <summary>The entries that changed (bytes <c>0c 61</c> after the length).</summary>
    public const ushort UpdateOpcode = 0x610C;

    /// <summary>An entry with no value: both are 0.</summary>
    public const byte NoValueKind = 0x00;

    /// <summary>An entry holding a u64 time instead of a count.</summary>
    public const byte TimeKind = 0x01;

    /// <summary>An entry with only its first value (the second is 0).</summary>
    public const byte FirstValueKind = 0x04;

    /// <summary>An entry with only its second value (the first is 0).</summary>
    public const byte SecondValueKind = 0x08;

    /// <summary>An entry with both values.</summary>
    public const byte BothValuesKind = 0x0C;

    /// <summary>The largest id taken: ids seen run from 1 to 90000006, so anything past this is not an entry.</summary>
    public const uint MaxId = 99_999_999;

    /// <summary>The largest count taken: counts stay in the thousands, so anything past this is not an entry.</summary>
    public const int MaxValue = 1_000_000;

    /// <summary>The largest value taken for Odyle energy: generous (the game's are 840 and 2000).</summary>
    public const int MaxOdyleValue = 100_000;

    /// <summary>The most entries a whole list may hold (A2Tools' bound; the game's has about a hundred).</summary>
    public const int MaxListEntries = 1000;

    /// <summary>
    /// The most bytes an update may carry after its entries: one after a <c>00</c> count and five (<c>01 &lt;u32&gt;</c>)
    /// after <c>01</c> in what A2Tools saw, so entries that leave more were not an update.
    /// </summary>
    public const int MaxUpdateTail = 5;

    /// <summary>
    /// The counts in one record (its length varint, two opcode bytes, then its body), or null when the record is
    /// neither kind or an update that does not read whole. A whole list always gives a value, since it says a
    /// character logged in even when it does not read: its counts are then empty, or the Odyle energy alone when that
    /// entry is found by its id.
    /// </summary>
    public static EntryRecord? Read(ReadOnlySpan<byte> record)
    {
        if (!GameRecords.TryOpen(record, out var opcode, out var body)) return null;
        return opcode switch
        {
            ListOpcode => InList(body),
            UpdateOpcode => InUpdate(body),
            _ => null,
        };
    }

    /// <summary>The Odyle energy among the counts, or null when it is not there or out of bounds.</summary>
    public static OdyleEnergy? OdyleIn(IEnumerable<EntryValue> counts)
    {
        foreach (var count in counts)
        {
            if (count.Id != EntryIds.OdyleEnergy) continue;
            return count.First <= MaxOdyleValue && count.Second <= MaxOdyleValue ? new OdyleEnergy(count.First, count.Second) : null;
        }
        return null;
    }

    // The whole list: the place from which a count of entries ends right at the end of the record and holds the
    // Odyle energy (other data comes first, so it is found by where it ends, not at an offset). When no place reads,
    // the Odyle entry is looked for by its id alone; failing that, the list gives no counts.
    private static EntryRecord InList(ReadOnlySpan<byte> body)
    {
        for (var start = 0; start < body.Length; start++)
        {
            if (!GameRecords.TryReadVarInt(body, start, out var count, out var at)) continue;
            if (count is 0 or > MaxListEntries) continue;
            if (!TryReadEntries(body, at, (int)count, out var counts, out var end) || end != body.Length) continue;
            if (counts.Exists(c => c.Id == EntryIds.OdyleEnergy)) return new EntryRecord(counts, IsWholeList: true);
        }
        return new EntryRecord(OdyleById(body) is { } odyle ? [odyle] : [], IsWholeList: true);
    }

    // The Odyle entry in a list that does not read whole: a counting kind followed by the Odyle id, read as an entry.
    private static EntryValue? OdyleById(ReadOnlySpan<byte> body)
    {
        for (var i = 0; i + 5 <= body.Length; i++)
        {
            if (body[i] is not (FirstValueKind or SecondValueKind or BothValuesKind)) continue;
            if (BinaryPrimitives.ReadUInt32LittleEndian(body[(i + 1)..]) != EntryIds.OdyleEnergy) continue;
            if (TryReadEntry(body, i, out var entry, out _) && entry is { } count && OdyleIn([count]) is not null) return count;
        }
        return null;
    }

    // An update: a byte, the entries, a short tail. The byte is not a count to trust when it is 00 (updates with 00
    // there carry an entry too), so at least one entry is read; every entry must read, and no more than a short tail
    // may follow, or the record is not taken at all (a part read could be another table's bytes).
    private static EntryRecord? InUpdate(ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty) return null;
        var count = Math.Max((int)body[0], 1);
        if (!TryReadEntries(body, 1, count, out var counts, out var end) || body.Length - end > MaxUpdateTail) return null;
        return new EntryRecord(counts, IsWholeList: false);
    }

    // `count` entries from `at`, and where they end; false when any does not read.
    private static bool TryReadEntries(ReadOnlySpan<byte> body, int at, int count, out List<EntryValue> counts, out int end)
    {
        counts = [];
        end = at;
        for (var n = 0; n < count; n++)
        {
            if (!TryReadEntry(body, end, out var entry, out end)) return false;
            if (entry is { } value) counts.Add(value);
        }
        return true;
    }

    // One entry at an offset and where the next begins: a count (entry set), or a time (entry null, read past).
    private static bool TryReadEntry(ReadOnlySpan<byte> body, int at, out EntryValue? entry, out int next)
    {
        entry = null;
        next = at;
        if (at < 0 || at + 5 > body.Length) return false;
        var kind = body[at];
        var id = BinaryPrimitives.ReadUInt32LittleEndian(body[(at + 1)..]);
        if (id is 0 or > MaxId) return false;
        next = at + 5;
        uint first = 0, second = 0;
        switch (kind)
        {
            case NoValueKind:
                break;
            case TimeKind:
                if (next + 8 > body.Length) return false;
                next += 8;
                return true;
            case FirstValueKind:
                if (!TryReadValue(body, next, out first, out next)) return false;
                break;
            case SecondValueKind:
                if (!TryReadValue(body, next, out second, out next)) return false;
                break;
            case BothValuesKind:
                if (!TryReadValue(body, next, out first, out next) || !TryReadValue(body, next, out second, out next)) return false;
                break;
            default:
                return false;
        }
        entry = new EntryValue(id, (int)first, (int)second);
        return true;
    }

    // A value: a varint within MaxValue (the cast to int is then safe).
    private static bool TryReadValue(ReadOnlySpan<byte> body, int at, out uint value, out int next) =>
        GameRecords.TryReadVarInt(body, at, out value, out next) && value <= MaxValue;
}
