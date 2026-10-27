using System.Buffers.Binary;
using System.Text;
using AionDpsMeter.Services.PacketProcessing.Routing;

namespace ScarletMeter.Engine.Entries;

/// <summary>
/// Writes synthetic records in the game's framing, for demo mode and tests: never captured bytes. The entry list's
/// records follow <see cref="EntryListRecords"/>' layout, the self record the layout upstream's PlayerInfoProcessor
/// reads (v1.11.5.341). A record starts with its length as a varint, counted the way upstream's stream reassembly
/// counts it: the record's whole size is the varint's value plus its own length less 4, so the value is the size of
/// what follows it plus 4.
/// </summary>
internal static class EntryListRecordWriter
{
    /// <summary>Upstream's opcode for the player's own character record (PacketOpcodes.PlayerInfo, bytes 33 36).</summary>
    public const ushort SelfRecordOpcode = PacketOpcodes.PlayerInfo;

    /// <summary>
    /// Made-up data before a whole list, as the game's list has other data first (its length is not the game's): a
    /// byte that reads as a count of 42, so a reader that takes the first plausible count rather than the place
    /// where the list ends at the record's end reads garbage.
    /// </summary>
    private static readonly byte[] DataBeforeList = [0x00, 0x00, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x00];

    /// <summary>
    /// A <c>0b 61</c> record: made-up data, then the list of entry counts, with an Abyss passage's time entry after the
    /// first count, as the game's list mixes them.
    /// </summary>
    public static byte[] List(IReadOnlyList<EntryValue> entries)
    {
        ArgumentOutOfRangeException.ThrowIfZero(entries.Count);
        var body = new List<byte>(DataBeforeList);
        AddVarInt(body, (uint)entries.Count + 1);
        AddEntry(body, entries[0]);
        AddTimeEntry(body, 10_000_002, 130_000);
        foreach (var entry in entries.Skip(1)) AddEntry(body, entry);
        return Frame(EntryListRecords.ListOpcode, body);
    }

    /// <summary>
    /// A <c>0c 61</c> record carrying <paramref name="entries"/>: the count byte as the game writes it (the number of
    /// entries, or <c>00</c> for one, when <paramref name="zeroCount"/>), then the entries and a one-byte tail.
    /// </summary>
    public static byte[] Update(IReadOnlyList<EntryValue> entries, bool zeroCount = false)
    {
        ArgumentOutOfRangeException.ThrowIfZero(entries.Count);
        if (zeroCount && entries.Count != 1) throw new ArgumentException("A 00 count carries one entry.", nameof(entries));
        var body = new List<byte> { zeroCount ? (byte)0 : checked((byte)entries.Count) };
        foreach (var entry in entries) AddEntry(body, entry);
        body.Add(0x03);
        return Frame(EntryListRecords.UpdateOpcode, body);
    }

    /// <summary>
    /// The player's own character record as upstream reads it: the entity id, a four-byte mask, a byte whose bit 0
    /// says a name follows, the name (its length, then UTF-8), the server id (u16) and the class code, then made-up
    /// bytes and the combat power twice (current and highest, u64), where upstream looks for it.
    /// </summary>
    public static byte[] SelfRecord(int entityId, string name, int serverId, int jobCode, int combatPower)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entityId);
        var body = new List<byte>();
        AddVarInt(body, (uint)entityId);
        body.AddRange([0x00, 0x00, 0x00, 0x00, 0x37]);
        var nameBytes = Encoding.UTF8.GetBytes(name);
        AddVarInt(body, (uint)nameBytes.Length);
        body.AddRange(nameBytes);
        body.Add((byte)(serverId & 0xFF));
        body.Add((byte)(serverId >> 8));
        body.Add((byte)jobCode);
        body.AddRange(new byte[12]);
        var power = new byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(power, (ulong)combatPower);
        BinaryPrimitives.WriteUInt64LittleEndian(power.AsSpan(8), (ulong)combatPower);
        body.AddRange(power);
        return Frame(SelfRecordOpcode, body);
    }

    /// <summary>
    /// The whole inventory at world entry (<c>11 56</c>): a made-up header, the Kina slots given, and made-up entries of
    /// other items in the inventory and a storage until the record is past <see cref="InventoryRecords.MinListingBytes"/>,
    /// as the game's is 15 KB or more.
    /// </summary>
    public static byte[] InventoryListing(IReadOnlyList<KinaSlot> kina)
    {
        var body = new List<byte> { 0x00, 0x00, 0x01 };
        foreach (var slot in kina) AddInventoryEntry(body, 1, slot.Item, slot.Quantity, InventoryRecords.InventoryContainer, slot.Slot);
        for (byte n = 0; body.Count < InventoryRecords.MinListingBytes + 100; n++)
            AddInventoryEntry(body, 1, 510_116_006, 3, n % 2 == 0 ? InventoryRecords.InventoryContainer : (byte)15, n);
        return Frame(InventoryRecords.ListingOpcode, body);
    }

    /// <summary>A change of the inventory (<c>1b 56</c>): a count, then each slot's entry with its kind (0 new, 1 changed, 2 gone).</summary>
    public static byte[] InventoryChange(IReadOnlyList<(KinaSlot Slot, byte Kind)> entries)
    {
        var body = new List<byte> { checked((byte)entries.Count) };
        foreach (var (slot, kind) in entries) AddInventoryEntry(body, kind, slot.Item, slot.Quantity, InventoryRecords.InventoryContainer, slot.Slot);
        return Frame(InventoryRecords.ChangeOpcode, body);
    }

    // One inventory entry in SkeeveAN's documented layout: kind, a byte, a made-up item instance (u32) and a u32, then the
    // item, its quantity (u64), the container and the slot: the kind ten bytes before the item.
    private static void AddInventoryEntry(List<byte> body, byte kind, uint item, long quantity, byte container, byte slot)
    {
        body.AddRange([kind, 0x00, 0x24, 0x9E, slot, 0x00, 0x00, 0x10, 0x00, 0x00]);
        AddId(body, item);
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, (ulong)quantity);
        body.AddRange(bytes);
        body.Add(container);
        body.Add(slot);
    }

    /// <summary>An entry as the game writes it, a zero left out: no value (00), the first (04), the second (08) or both (0c).</summary>
    public static void AddEntry(List<byte> body, EntryValue entry)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entry.First);
        ArgumentOutOfRangeException.ThrowIfNegative(entry.Second);
        var kind = (entry.First > 0, entry.Second > 0) switch
        {
            (true, true) => EntryListRecords.BothValuesKind,
            (true, false) => EntryListRecords.FirstValueKind,
            (false, true) => EntryListRecords.SecondValueKind,
            _ => EntryListRecords.NoValueKind,
        };
        body.Add(kind);
        AddId(body, entry.Id);
        if (entry.First > 0) AddVarInt(body, (uint)entry.First);
        if (entry.Second > 0) AddVarInt(body, (uint)entry.Second);
    }

    /// <summary>A time entry (kind 01): the id, then a u64.</summary>
    public static void AddTimeEntry(List<byte> body, uint id, ulong time)
    {
        body.Add(EntryListRecords.TimeKind);
        AddId(body, id);
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, time);
        body.AddRange(bytes);
    }

    /// <summary>Length varint, two opcode bytes (little-endian), body.</summary>
    public static byte[] Frame(ushort opcode, List<byte> body)
    {
        var record = new List<byte>(body.Count + 8);
        AddVarInt(record, (uint)(2 + body.Count + 4));
        record.Add((byte)(opcode & 0xFF));
        record.Add((byte)(opcode >> 8));
        record.AddRange(body);
        return [.. record];
    }

    private static void AddId(List<byte> body, uint id)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, id);
        body.AddRange(bytes);
    }

    private static void AddVarInt(List<byte> bytes, uint value)
    {
        do
        {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            bytes.Add(value != 0 ? (byte)(b | 0x80) : b);
        }
        while (value != 0);
    }
}
