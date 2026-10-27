using System.Buffers.Binary;
using ScarletMeter.Engine.Records;

namespace ScarletMeter.Engine.Entries;

/// <summary>One inventory slot holding Kina: which of the two items, the slot, and its quantity (0 when the slot is gone).</summary>
internal readonly record struct KinaSlot(uint Item, byte Slot, long Quantity);

/// <summary>
/// What one inventory record says of the character's Kina: the Kina slots it carries, and whether it is the whole
/// inventory at world entry (<see cref="IsListing"/>), which lists every slot, so a slot missing from it holds none.
/// </summary>
internal sealed record KinaReading(IReadOnlyList<KinaSlot> Slots, bool IsListing);

/// <summary>
/// Reads the character's Kina from its inventory records, as SkeeveAN/Aion-DPS-Meter reads the inventory
/// (<c>Client/Aion2/Aion2Stock.cs</c> at 010e994, its only commit, up to 729cb91; MIT; THIRD-PARTY-NOTICES.md). At world
/// entry the server sends the whole inventory in one large record (bytes <c>11 56</c>, 15 KB or more), and each change
/// later as <c>1b 56</c>, one entry per slot with the slot's new quantity. An entry is found by its anchor rather than at
/// an offset: <c>&lt;kind u8: 0 new, 1 changed, 2 gone&gt; … (9 bytes) &lt;item id u32&gt; &lt;quantity u64&gt;
/// &lt;container u8&gt; &lt;slot u8&gt;</c>, the kind ten bytes before the item, the quantity's top three bytes zero.
/// Kina lives in container 1, the inventory's cube with the currencies, as items 930100031 (Kina) and 930100001
/// (Kina (Bound)), the ids and names of the client's item table as SkeeveAN publishes it (item_names.json at 2d4d2f1).
/// Only those two items are taken, so the anchor is an exact id, not a guess at any item. Pure; every read is
/// bounds-checked.
/// </summary>
internal static class InventoryRecords
{
    /// <summary>The whole inventory at world entry (bytes <c>11 56</c> after the length).</summary>
    public const ushort ListingOpcode = 0x5611;

    /// <summary>A change of the inventory (bytes <c>1b 56</c> after the length).</summary>
    public const ushort ChangeOpcode = 0x561B;

    /// <summary>Kina's item id.</summary>
    public const uint KinaItem = 930_100_031;

    /// <summary>Kina (Bound)'s item id.</summary>
    public const uint BoundKinaItem = 930_100_001;

    /// <summary>The inventory's container, the cube that holds the currencies.</summary>
    public const byte InventoryContainer = 1;

    /// <summary>The smallest whole listing taken: SkeeveAN found it always 15 KB or more; a smaller one is something else.</summary>
    public const int MinListingBytes = 5000;

    /// <summary>The kind of an entry whose slot is gone (its quantity is then none).</summary>
    public const byte GoneKind = 2;

    /// <summary>Whether a record with this opcode is one of the two.</summary>
    public static bool IsInventory(ushort opcode) => opcode is ListingOpcode or ChangeOpcode;

    /// <summary>
    /// The Kina slots in one record (its length varint, two opcode bytes, then its body), or null when the record is
    /// neither kind, a listing too small to be one, or a change that touches no Kina slot. A slot named twice counts
    /// once, its first entry, as SkeeveAN reads them.
    /// </summary>
    public static KinaReading? Read(ReadOnlySpan<byte> record)
    {
        if (!GameRecords.TryOpen(record, out var opcode, out var body) || !IsInventory(opcode)) return null;
        var listing = opcode == ListingOpcode;
        if (listing && body.Length < MinListingBytes) return null;
        List<KinaSlot> slots = [];
        for (var i = 10; i + 14 <= body.Length; i++)
        {
            var item = BinaryPrimitives.ReadUInt32LittleEndian(body[i..]);
            if (item is not (KinaItem or BoundKinaItem)) continue;
            var kind = body[i - 10];
            if (kind > GoneKind || body[i + 9] != 0 || body[i + 10] != 0 || body[i + 11] != 0 || body[i + 12] != InventoryContainer) continue;
            var slot = body[i + 13];
            var quantity = kind == GoneKind ? 0 : (long)BinaryPrimitives.ReadUInt64LittleEndian(body[(i + 4)..]);
            if (!slots.Exists(s => s.Slot == slot)) slots.Add(new KinaSlot(item, slot, quantity));
            i += 13;
        }
        return listing || slots.Count > 0 ? new KinaReading(slots, listing) : null;
    }
}
