using ScarletMeter.Engine.Records;

namespace ScarletMeter.Engine.Odyle;

/// <summary>The energy read from a record and its entry's key, which a list defines for the updates after it.</summary>
internal readonly record struct OdyleEnergyReading(OdyleEnergy Energy, uint Key);

/// <summary>
/// Reads Odyle energy from the game's records, as worked out by A2Tools (taengu/A2Tools-DPS-Meter#63 and its fixes
/// 2723bff and 29e7193, GPL-3.0; see THIRD-PARTY-NOTICES.md): it is the first entry of the game's list of entry
/// counts. An entry is <c>&lt;kind&gt; &lt;slot&gt; &lt;key varint&gt; &lt;value varint&gt;…</c>, where the kind says
/// which of two values follow, a zero being left out: <c>0c</c> both, <c>04</c> the first only, <c>08</c> the second
/// only. Odyle energy is the entry in slot 1, its values basic and charged: <c>0c 01 &lt;key&gt; &lt;basic&gt;
/// &lt;charged&gt;</c>, or <c>04 01 &lt;key&gt; &lt;basic&gt;</c> with no charged energy, or <c>08 01 &lt;key&gt;
/// &lt;charged&gt;</c> with no basic energy. Record <c>0b 61</c> carries the whole list (at login and after a server
/// transfer), with other data before it; record <c>0c 61 &lt;byte&gt; &lt;entries&gt;</c> carries the entries that
/// changed (a recharge item, a raid). Pure and allocation-free; every read is bounds-checked, so malformed bytes give
/// no value rather than an exception.
/// </summary>
internal static class OdyleEnergyRecords
{
    /// <summary>The whole list of entry counts (bytes <c>0b 61</c> after the length).</summary>
    public const ushort ListOpcode = 0x610B;

    /// <summary>The entries that changed (bytes <c>0c 61</c> after the length).</summary>
    public const ushort UpdateOpcode = 0x610C;

    /// <summary>
    /// The largest value taken for either part. Generous (the game's are 840 and 2000): it only turns away bytes in
    /// the list that merely look like the entry.
    /// </summary>
    public const int MaxValue = 100_000;

    /// <summary>An entry's kind with only its first value (for the energy, basic; charged is 0).</summary>
    public const byte FirstValueKind = 0x04;

    /// <summary>An entry's kind with only its second value (for the energy, charged; basic is 0).</summary>
    public const byte SecondValueKind = 0x08;

    /// <summary>An entry's kind with both values.</summary>
    public const byte BothValuesKind = 0x0C;

    /// <summary>The energy's slot.</summary>
    public const byte EnergySlot = 0x01;

    /// <summary>
    /// The Odyle energy in one record (its length varint, two opcode bytes, then its body), or null when the record
    /// is neither kind or does not carry it; no energy key known (see <see cref="ReadEntry"/>).
    /// </summary>
    public static OdyleEnergy? Read(ReadOnlySpan<byte> record) => ReadEntry(record, energyKey: null)?.Energy;

    /// <summary>
    /// The Odyle energy in one record and its entry's key, or null. The list defines the key (its energy entry is
    /// found by shape). An update can carry another key's slot-1 entry (A2Tools' list holds one, <c>04 01 b4 c4 04
    /// 03</c>, so its update would read as the energy), so with <paramref name="energyKey"/> known, from an earlier list,
    /// an update's entry counts only under that key; without it (a meter started mid-session) the first slot-1 entry
    /// is taken, as A2Tools does. Audit on PR #36.
    /// </summary>
    public static OdyleEnergyReading? ReadEntry(ReadOnlySpan<byte> record, uint? energyKey)
    {
        if (!GameRecords.TryOpen(record, out var opcode, out var body)) return null;
        return opcode switch
        {
            ListOpcode => InList(body),
            UpdateOpcode => InUpdate(body, energyKey),
            _ => null,
        };
    }

    // The list: the slot-1 entry that the list's second entry follows with slot 2 and the same key. Other data comes
    // before the list, so the entry is found by its shape, not by an offset. The data before it can hold a look-alike
    // ("0c 01 00 00 00" opens A2Tools' sample), which EnergyOf turns away by its zeros. Reading only the two-value
    // kind passed over the energy of a player with either value at 0, and the search went on to whatever looked like
    // it next.
    private static OdyleEnergyReading? InList(ReadOnlySpan<byte> body)
    {
        for (var i = 0; i + 1 < body.Length; i++)
        {
            if (!IsKind(body[i]) || body[i + 1] != EnergySlot) continue;
            if (!TryReadEntry(body, i, out var entry, out var next)) continue;
            if (!TryReadEntry(body, next, out var following, out _)) continue;
            if (following.Slot != 2 || following.Key != entry.Key) continue;
            if (EnergyOf(entry) is { } energy) return new OdyleEnergyReading(energy, entry.Key);
        }
        return null;
    }

    // An update: a byte, then the entries; the energy is among them or not there. The byte is not a count to trust
    // when it is 00: updates with 00 there carry an entry too (A2Tools, 29e7193), so at least one entry is read. A
    // larger count is kept, so bytes after the entries are never read as more of them. With the energy's key known,
    // another key's slot-1 entry is passed over (ReadEntry).
    private static OdyleEnergyReading? InUpdate(ReadOnlySpan<byte> body, uint? energyKey)
    {
        if (body.IsEmpty) return null;
        var count = Math.Max((int)body[0], 1);
        var at = 1;
        for (var n = 0; n < count; n++)
        {
            if (!TryReadEntry(body, at, out var entry, out at)) return null;
            if (energyKey is { } key && entry.Key != key) continue;
            if (EnergyOf(entry) is { } energy) return new OdyleEnergyReading(energy, entry.Key);
        }
        return null;
    }

    // The energy, when the entry is it: slot 1, and every value its kind says it carries plausible (IsPlausible). The
    // casts are safe after the bound.
    private static OdyleEnergy? EnergyOf(Entry entry)
    {
        if (entry.Slot != EnergySlot) return null;
        return entry.Kind switch
        {
            BothValuesKind when IsPlausible(entry.First) && IsPlausible(entry.Second) => new OdyleEnergy((int)entry.First, (int)entry.Second),
            FirstValueKind when IsPlausible(entry.First) => new OdyleEnergy((int)entry.First, 0),
            SecondValueKind when IsPlausible(entry.First) => new OdyleEnergy(0, (int)entry.First),
            _ => null,
        };
    }

    private static bool IsKind(byte kind) => kind is FirstValueKind or SecondValueKind or BothValuesKind;

    // A value the energy entry carries: never 0, since the game leaves a zero out (that is what the kind says), so a 0
    // means bytes that only look like the entry (Scarlet's tightening of A2Tools' bound, which every documented slot-1
    // entry keeps to); and within the generous MaxValue, which turns away other look-alikes. Two such look-alikes: the
    // list's header ("0c 01 00 00 00"), and an update of the table it opens, whose entries appear to carry a 4-byte id
    // ("04 0c 00 00 00 ..." in A2Tools' update without the energy), so id 1's would read as "0c 01 00 00 00 ...": kind
    // 0c, slot 1, key 0, values 0 and 0, which is "0 (+0)" with nothing to back it (audit on PR #36; an inference from
    // A2Tools' samples). Since an update's first entry is now always read, this is what turns it away there.
    private static bool IsPlausible(uint value) => value is > 0 and <= MaxValue;

    // An entry as read: its kind, slot and key, and its one value (First) or two (First, Second).
    private readonly record struct Entry(byte Kind, byte Slot, uint Key, uint First, uint Second);

    // One entry at an offset: its kind, slot, key and one or two values, and where the next begins.
    private static bool TryReadEntry(ReadOnlySpan<byte> body, int at, out Entry entry, out int next)
    {
        entry = default;
        next = at;
        if (at < 0 || at + 2 > body.Length) return false;
        var kind = body[at];
        var values = kind switch { FirstValueKind or SecondValueKind => 1, BothValuesKind => 2, _ => 0 };
        if (values == 0) return false;
        var slot = body[at + 1];
        if (!GameRecords.TryReadVarInt(body, at + 2, out var key, out next)) return false;
        if (!GameRecords.TryReadVarInt(body, next, out var first, out next)) return false;
        uint second = 0;
        if (values == 2 && !GameRecords.TryReadVarInt(body, next, out second, out next)) return false;
        entry = new Entry(kind, slot, key, first, second);
        return true;
    }
}
