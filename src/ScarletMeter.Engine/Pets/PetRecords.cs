using System.Buffers.Binary;
using ScarletMeter.Engine.Records;

namespace ScarletMeter.Engine.Pets;

/// <summary>
/// The game's records about the player's pets, which upstream does not decode: the whole collection at login
/// (<c>00 90</c>), souls picked up (<c>0D 90</c>), a level reached (<c>14 90</c>) and a pet unlocked from a chest
/// (<c>09 90</c>). The layouts are A2Tools-Plugins' pet-souls decoders (manifest.json at f206c25c49, GPL-3.0;
/// THIRD-PARTY-NOTICES.md), read as A2Tools' meter reads a decoder (src-tauri/src/plugins/decoder.rs at 7eba4b36f0): the
/// body is what follows the two opcode bytes, numbers are little-endian, and bytes after the last field are not read.
/// <list type="bullet">
/// <item>Collection: 4 bytes, then the levels (a varint count of entries: pet u32, 4 bytes, level u32), then the points
/// (a varint count of entries: pet u32, points u32). A2Tools first read the counts as one byte, which put the whole list
/// one byte off for a player with more than 127 pets (2026-10-13).</item>
/// <item>Souls: a one-byte count of entries, pet u32 and souls u32. A2Tools first read one pet, and a kill that gave souls
/// to two pets counted the first only (2026-10-19).</item>
/// <item>Level reached and unlocked: 2 bytes, pet u32, level u32.</item>
/// </list>
/// A record that does not read whole gives nothing, and so does one with a value no pet can have (a level past 3, a pet id
/// of 0 or past <see cref="int.MaxValue"/>, more points or souls than any pet holds or a pickup gives): a layout the game
/// has changed gives nothing rather than wrong counts. Pure and bounds-checked; a list is sized only once its bytes are
/// known to be there.
/// </summary>
internal static class PetRecords
{
    /// <summary>The whole collection (bytes <c>00 90</c>).</summary>
    public const ushort CollectionOpcode = 0x9000;

    /// <summary>A pet unlocked from a chest, at a level (bytes <c>09 90</c>).</summary>
    public const ushort UnlockedOpcode = 0x9009;

    /// <summary>Souls picked up (bytes <c>0D 90</c>).</summary>
    public const ushort SoulsOpcode = 0x900D;

    /// <summary>A level reached (bytes <c>14 90</c>).</summary>
    public const ushort LevelUpOpcode = 0x9014;

    /// <summary>A pet's highest level.</summary>
    public const int MaxLevel = 3;

    /// <summary>Most entries a list may have, as A2Tools' decoder allows (its MAX_LIST); the game has about two hundred pets.</summary>
    public const int MaxEntries = 4096;

    /// <summary>
    /// Most soul points a pet may show: a level takes 75 at most, but a pet at level 3 may keep the souls it is still given,
    /// so the bound is far above that, there only to turn away a misread.
    /// </summary>
    public const int MaxPoints = 1_000_000;

    /// <summary>Most souls one pickup may give a pet (a monster gives 1, a Pet Soul Chest a few): a bound against a misread.</summary>
    public const int MaxSoulsAtOnce = 10_000;

    private const int LevelEntryBytes = 12;
    private const int PointsEntryBytes = 8;
    private const int SoulsEntryBytes = 8;

    /// <summary>Whether the opcode is one of the four pet records.</summary>
    public static bool IsPetRecord(ushort opcode) => opcode is CollectionOpcode or UnlockedOpcode or SoulsOpcode or LevelUpOpcode;

    /// <summary>
    /// Reads one framed record (its length varint, two opcode bytes, then its body), stamped <paramref name="now"/>, or null
    /// when it is not a pet record or does not read (see the class). A pickup that read but gave no souls (no entries, or
    /// every entry 0) is one with no souls, not null: it is no misread, and the tap neither raises nor logs it.
    /// </summary>
    public static PetRecord? Read(ReadOnlySpan<byte> record, DateTimeOffset now)
    {
        if (!GameRecords.TryOpen(record, out var opcode, out var body)) return null;
        return opcode switch
        {
            CollectionOpcode => ReadCollection(body, now),
            SoulsOpcode => ReadSouls(body, now),
            LevelUpOpcode => ReadLevel(body, unlocked: false, now),
            UnlockedOpcode => ReadLevel(body, unlocked: true, now),
            _ => null,
        };
    }

    // 4 bytes, the levels, then the points.
    private static PetCollectionRead? ReadCollection(ReadOnlySpan<byte> body, DateTimeOffset now)
    {
        var at = 4;
        if (!TryCount(body, ref at, LevelEntryBytes, out var levelCount)) return null;
        var levels = new PetLevel[levelCount];
        for (var i = 0; i < levelCount; i++, at += LevelEntryBytes)
        {
            if (!TryPet(body, at, out var pet) || !TryNumber(body, at + 8, MaxLevel, out var level)) return null;
            levels[i] = new PetLevel(pet, level);
        }
        if (!TryCount(body, ref at, PointsEntryBytes, out var pointsCount)) return null;
        var points = new PetSouls[pointsCount];
        for (var i = 0; i < pointsCount; i++, at += PointsEntryBytes)
        {
            if (!TryPet(body, at, out var pet) || !TryNumber(body, at + 4, MaxPoints, out var value)) return null;
            points[i] = new PetSouls(pet, value);
        }
        return new PetCollectionRead(levels, points, now);
    }

    // A one-byte count, then pet and souls for each; entries that gave none are left out (possibly all of them).
    private static PetSoulsGained? ReadSouls(ReadOnlySpan<byte> body, DateTimeOffset now)
    {
        if (body.IsEmpty) return null;
        var count = body[0];
        if (body.Length < 1 + count * SoulsEntryBytes) return null;
        var souls = new List<PetSouls>(count);
        for (int i = 0, at = 1; i < count; i++, at += SoulsEntryBytes)
        {
            if (!TryPet(body, at, out var pet) || !TryNumber(body, at + 4, MaxSoulsAtOnce, out var amount)) return null;
            if (amount > 0) souls.Add(new PetSouls(pet, amount));
        }
        return new PetSoulsGained(souls, now);
    }

    // 2 bytes, the pet, its level: a level reached is 1 or more; a pet unlocked may come at any level.
    private static PetLevelReached? ReadLevel(ReadOnlySpan<byte> body, bool unlocked, DateTimeOffset now)
    {
        if (!TryPet(body, 2, out var pet) || !TryNumber(body, 6, MaxLevel, out var level)) return null;
        if (!unlocked && level == 0) return null;
        return new PetLevelReached(pet, level, unlocked, now);
    }

    // A list's varint count at `at`, moved past; false when it is not there, is past MaxEntries, or its entries would run
    // past the body.
    private static bool TryCount(ReadOnlySpan<byte> body, ref int at, int entryBytes, out int count)
    {
        count = 0;
        if (!GameRecords.TryReadVarInt(body, at, out var value, out var next) || value > MaxEntries) return false;
        if ((long)body.Length - next < (long)value * entryBytes) return false;
        count = (int)value;
        at = next;
        return true;
    }

    // A pet id: a u32 of 1 or more that fits an int.
    private static bool TryPet(ReadOnlySpan<byte> body, int at, out int pet)
    {
        pet = 0;
        if (at < 0 || body.Length - at < 4) return false;
        var value = BinaryPrimitives.ReadUInt32LittleEndian(body[at..]);
        if (value is 0 or > int.MaxValue) return false;
        pet = (int)value;
        return true;
    }

    // A u32 of at most `max`.
    private static bool TryNumber(ReadOnlySpan<byte> body, int at, int max, out int number)
    {
        number = 0;
        if (at < 0 || body.Length - at < 4) return false;
        var value = BinaryPrimitives.ReadUInt32LittleEndian(body[at..]);
        if (value > (uint)max) return false;
        number = (int)value;
        return true;
    }
}
