namespace ScarletMeter.Engine.Odyle;

/// <summary>
/// Reads Odyle energy from the game's records, as worked out by A2Tools (taengu/A2Tools-DPS-Meter#63, GPL-3.0; see
/// THIRD-PARTY-NOTICES.md): it is the first entry of the game's list of entry counts. An entry is
/// <c>&lt;kind&gt; &lt;slot&gt; &lt;key varint&gt; &lt;value varint&gt;…</c>, kind <c>04</c> with one value (a
/// dungeon's entries left) and kind <c>0c</c> with two; Odyle energy is <c>0c 01 &lt;key&gt; &lt;basic&gt;
/// &lt;charged&gt;</c>. Record <c>0b 61</c> carries the whole list (at login and after a server transfer), with other
/// data before it; record <c>0c 61 &lt;count&gt; &lt;entries&gt;</c> carries the entries that changed (a recharge item,
/// a raid). Pure and allocation-free; every read is bounds-checked, so malformed bytes give no value rather than an
/// exception.
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

    private const byte OneValueKind = 0x04;
    private const byte TwoValueKind = 0x0C;
    private const byte EnergySlot = 0x01;

    /// <summary>
    /// The Odyle energy in one record (its length varint, two opcode bytes, then its body), or null when the record
    /// is neither kind or does not carry it.
    /// </summary>
    public static OdyleEnergy? Read(ReadOnlySpan<byte> record)
    {
        if (!TryReadVarInt(record, 0, out _, out var at)) return null;
        if (record.Length < at + 2) return null;
        var opcode = (ushort)(record[at] | (record[at + 1] << 8));
        var body = record[(at + 2)..];
        return opcode switch
        {
            ListOpcode => InList(body),
            UpdateOpcode => InUpdate(body),
            _ => null,
        };
    }

    /// <summary>The two opcode bytes of a record, or null when it is too short to have them.</summary>
    public static ushort? OpcodeOf(ReadOnlySpan<byte> record) =>
        TryReadVarInt(record, 0, out _, out var at) && record.Length >= at + 2 ? (ushort)(record[at] | (record[at + 1] << 8)) : null;

    // The list: the "0c 01" entry that the list's second entry follows with slot 2 and the same key. Other data comes
    // before the list, so the entry is found by its shape, not by an offset.
    private static OdyleEnergy? InList(ReadOnlySpan<byte> body)
    {
        for (var i = 0; i + 1 < body.Length; i++)
        {
            if (body[i] != TwoValueKind || body[i + 1] != EnergySlot) continue;
            if (!TryReadEntry(body, i, out var entry, out var next)) continue;
            if (!TryReadEntry(body, next, out var following, out _)) continue;
            if (following.Slot != 2 || following.Key != entry.Key) continue;
            if (EnergyOf(entry) is { } energy) return energy;
        }
        return null;
    }

    // An update: a count, then that many entries; the energy is among them or not there.
    private static OdyleEnergy? InUpdate(ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty) return null;
        var count = body[0];
        var at = 1;
        for (var n = 0; n < count; n++)
        {
            if (!TryReadEntry(body, at, out var entry, out at)) return null;
            if (EnergyOf(entry) is { } energy) return energy;
        }
        return null;
    }

    private static OdyleEnergy? EnergyOf(Entry entry) =>
        entry.Kind == TwoValueKind && entry.Slot == EnergySlot && IsPlausible(entry.First) && IsPlausible(entry.Second)
            ? new OdyleEnergy((int)entry.First, (int)entry.Second)
            : null;

    private static bool IsPlausible(uint value) => value <= MaxValue;

    private readonly record struct Entry(byte Kind, byte Slot, uint Key, uint First, uint Second);

    // One entry at an offset: its kind, slot, key and one or two values, and where the next begins.
    private static bool TryReadEntry(ReadOnlySpan<byte> body, int at, out Entry entry, out int next)
    {
        entry = default;
        next = at;
        if (at < 0 || at + 2 > body.Length) return false;
        var kind = body[at];
        var values = kind switch { OneValueKind => 1, TwoValueKind => 2, _ => 0 };
        if (values == 0) return false;
        var slot = body[at + 1];
        if (!TryReadVarInt(body, at + 2, out var key, out next)) return false;
        if (!TryReadVarInt(body, next, out var first, out next)) return false;
        uint second = 0;
        if (values == 2 && !TryReadVarInt(body, next, out second, out next)) return false;
        entry = new Entry(kind, slot, key, first, second);
        return true;
    }

    // A little-endian base-128 varint of at most five bytes, as upstream's PacketReader reads them. One that does not
    // fit 32 bits is refused rather than wrapped, so it can never pass for a small value.
    private static bool TryReadVarInt(ReadOnlySpan<byte> data, int at, out uint value, out int next)
    {
        value = 0;
        next = at;
        for (var shift = 0; shift < 35; shift += 7)
        {
            if (next < 0 || next >= data.Length) return false;
            var b = data[next++];
            if (shift == 28 && (b & 0x70) != 0) return false;
            value |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return true;
        }
        return false;
    }
}
