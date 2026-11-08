using System.Buffers.Binary;
using AionDpsMeter.Services.PacketCapture;
using AionDpsMeter.Services.PacketProcessing.Routing;
using K4os.Compression.LZ4;
using ScarletMeter.Engine.Abyss;
using ScarletMeter.Engine.Maps;

namespace ScarletMeter.Tests;

/// <summary>
/// Synthetic game records and the stream they travel in, for the tests of the records upstream does not decode (the
/// entry list with Odyle energy, the map loaded, the Abyss's artifact holders), of its damage records (a player's class
/// from their skills) and of its party list (the server it gives a member of the same name as the character): framing,
/// LZ4 bundles and bringing upstream's reassembly into step. Never captured bytes.
/// </summary>
internal static class SyntheticRecords
{
    // Brings upstream's reassembly of one stream (a connection; "synthetic" unless named) into step, as live traffic
    // eventually does. Its PacketAccumulator (v1.11.5.341) looks for its keep-alive (0e 00 36…) from the end of the
    // bytes it holds, not the start, so it finds one only among bytes left from an earlier chunk: a chunk ending in a
    // keep-alive, then a shorter one, synchronises it, and the records after that are read. Should upstream search from
    // the start, the same two chunks synchronise it at once. Keep-alives are not handed on, so the tap sees nothing.
    public static void Synchronise(TcpStreamBuffer stream, string connection = "synthetic")
    {
        byte[] keepAlive = [0x0E, 0x00, 0x36, 0, 0, 0, 0, 0, 0, 0, 0];
        stream.AddData(connection, [.. new byte[20], .. keepAlive], 0);
        stream.AddData(connection, keepAlive, 0);
    }

    // An LZ4 bundle as upstream's PacketProcessor reads one: length, ff ff, the decompressed size (u32, little-endian),
    // then the compressed records.
    public static byte[] Bundle(byte[] records)
    {
        var compressed = new byte[LZ4Codec.MaximumOutputSize(records.Length)];
        var length = LZ4Codec.Encode(records, compressed);
        var size = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(size, records.Length);
        return Frame(0xFFFF, Convert.ToHexString([.. size, .. compressed.AsSpan(0, length)]));
    }

    // A record from its opcode and body, framed as EntryListRecordWriter frames them: the length varint counts what
    // follows it plus 4, as upstream's stream reassembly does.
    public static byte[] Frame(ushort opcode, string body)
    {
        var bytes = Hex(body);
        var header = new List<byte>();
        for (var value = (uint)(2 + bytes.Length + 4); ; value >>= 7)
        {
            var b = (byte)(value & 0x7F);
            if (value >> 7 == 0) { header.Add(b); break; }
            header.Add((byte)(b | 0x80));
        }
        return [.. header, (byte)(opcode & 0xFF), (byte)(opcode >> 8), .. bytes];
    }

    // A map-loaded record in the documented shape: the u32 before the id (A2Tools' "count"), the id, then restLength
    // made-up bytes (0x5a; 38 makes the 49-byte record, length 52, of every load in A2Tools' capture).
    public static byte[] MapLoad(int mapId, uint count = 1, int restLength = 38)
    {
        var body = new byte[8 + restLength];
        BinaryPrimitives.WriteUInt32LittleEndian(body, count);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), mapId);
        body.AsSpan(8).Fill(0x5A);
        return Frame(MapLoadRecords.Opcode, Convert.ToHexString(body));
    }

    public static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", string.Empty, StringComparison.Ordinal));

    // One boss of the world map's boss list (01 91) in A2Tools' documented shape: the up byte, the world-map id as a
    // varint, a made-up position (three f32) when up, the extra byte some bosses carry (0x10, the value in A2Tools' sample)
    // when asked, then the time (u64, Unix ms).
    public static byte[] FieldBossEntry(int id, bool up, long timeMs, bool extraByte = false, byte extra = 0x10)
    {
        var bytes = new List<byte> { up ? (byte)1 : (byte)0 };
        for (var value = (uint)id; ; value >>= 7)
        {
            var b = (byte)(value & 0x7F);
            if (value >> 7 == 0) { bytes.Add(b); break; }
            bytes.Add((byte)(b | 0x80));
        }
        if (up)
        {
            var position = new byte[12];
            BinaryPrimitives.WriteSingleLittleEndian(position, 1234.5f);
            BinaryPrimitives.WriteSingleLittleEndian(position.AsSpan(4), -56.25f);
            BinaryPrimitives.WriteSingleLittleEndian(position.AsSpan(8), 7.0f);
            bytes.AddRange(position);
        }
        if (extraByte) bytes.Add(extra);
        var time = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(time, timeMs);
        bytes.AddRange(time);
        return [.. bytes];
    }

    // The world map's boss list (01 91): the u16 before the map id (0 for the player's own map), the map id (u32), the count
    // as a varint (single-byte counts here), the entries, then any trailing bytes.
    public static byte[] FieldBossMap(ushort header, int mapId, byte[][] entries, byte[]? trailing = null, int? count = null)
    {
        var body = new byte[7];
        BinaryPrimitives.WriteUInt16LittleEndian(body, header);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(2), mapId);
        body[6] = (byte)(count ?? entries.Length);
        return Frame(FieldBossMapRecords.Opcode, Convert.ToHexString([.. body, .. entries.SelectMany(entry => entry), .. trailing ?? []]));
    }

    // An artifact war in A2Tools' documented shape: a made-up byte, the event id, two made-up bytes, the holding's start
    // (Unix ms) and, when given, its reset.
    public static byte[] ArtifactWar(int eventId, long sinceMs, long? untilMs)
    {
        var bytes = new byte[15 + (untilMs is null ? 0 : 8)];
        bytes[0] = 0x01;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(1), (uint)eventId);
        bytes[5] = 0x02;
        bytes[6] = 0x03;
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(7), sinceMs);
        if (untilMs is { } until) BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(15), until);
        return bytes;
    }

    // Artifacts in A2Tools' documented shape: their count, then each one's id, a made-up u32, the holder's byte and eight
    // made-up bytes.
    public static byte[] ArtifactList(params (int Id, byte Holder)[] artifacts)
    {
        var bytes = new byte[1 + artifacts.Length * 17];
        bytes[0] = (byte)artifacts.Length;
        for (var n = 0; n < artifacts.Length; n++)
        {
            var at = bytes.AsSpan(1 + n * 17);
            BinaryPrimitives.WriteUInt32LittleEndian(at, (uint)artifacts[n].Id);
            BinaryPrimitives.WriteUInt32LittleEndian(at[4..], 0x5A5A);
            at[8] = artifacts[n].Holder;
            at[9..17].Fill(0x3C);
        }
        return bytes;
    }

    // The Abyss map's record (07 e3): two zero bytes, the number of wars, the wars, then the artifacts.
    public static byte[] AbyssMap(byte[][] wars, byte[] artifacts) =>
        Frame(AbyssArtifactRecords.MapOpcode, Convert.ToHexString([0x00, 0x00, (byte)wars.Length, .. wars.SelectMany(war => war), .. artifacts]));

    // The record on entering the Abyss (05 e3): one war, then the artifacts.
    public static byte[] AbyssEntry(byte[] war, byte[] artifacts) =>
        Frame(AbyssArtifactRecords.EntryOpcode, Convert.ToHexString([.. war, .. artifacts]));

    // A damage record (04 38) as upstream's DamagePacketProcessor reads one (v1.11.5.341): the target, the switch value,
    // a flag and the actor as varints, the skill code (u32), a byte, the damage type (varint), the special block (eight
    // bytes for switch value 4, twelve for 5 to 7, unless told), a varint, then the damage (varint). Made-up bytes are zero.
    public static byte[] Damage(int targetId, int actorId, int skillCode, long damage = 1_000, uint switchValue = 4, int? specialLength = null)
    {
        List<byte> body = [.. VarInt((uint)targetId), .. VarInt(switchValue), 0x00, .. VarInt((uint)actorId)];
        var skill = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(skill, skillCode);
        var special = specialLength ?? ((switchValue & 0x0F) == 4 ? 8 : 12);
        body.AddRange([.. skill, 0x00, 0x01, .. new byte[special], 0x00, .. VarInt((uint)damage)]);
        return Frame(PacketOpcodes.Damage, Convert.ToHexString([.. body]));
    }

    // A party list (02 97) with one member, as upstream's PartyProcessor reads one (v1.11.5.341): the party's key, name,
    // size and dungeon, two bytes, the leader's id (u64), a byte of bits, two bytes and the member count; then the member's
    // presence mask (any but 0 is a member; 0x20 adds no field), slot, id (u64: the server id in its top 16 bits, the
    // character id in its low 32), name (a length byte, then UTF-8), a u32, the level and gear score (u32 each), its login
    // bit (taken from the header's byte of bits, so no byte of its own), its role, combat power (u64), no tickets and two
    // bytes. Made-up bytes are zero, with a few to spare at the end.
    public static byte[] Party(string name, int serverId, uint characterId, int level = 45, long combatPower = 50_000)
    {
        var id = ((ulong)(ushort)serverId << 48) | characterId;
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        var u64 = new byte[8];
        var u32 = new byte[4];
        List<byte> body = [0x01, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        BinaryPrimitives.WriteUInt64LittleEndian(u64, id);
        body.AddRange([.. u64, 0x00, 0x00, 0x00, .. VarInt(1)]);
        body.AddRange([0x20, 0x01, .. u64, (byte)nameBytes.Length, .. nameBytes, 0x00, 0x00, 0x00, 0x00]);
        BinaryPrimitives.WriteInt32LittleEndian(u32, level);
        body.AddRange([.. u32, 0x00, 0x00, 0x00, 0x00, 0x00]);
        BinaryPrimitives.WriteInt64LittleEndian(u64, combatPower);
        body.AddRange([.. u64, .. VarInt(0), 0x00, 0x00, .. new byte[8]]);
        return Frame(PacketOpcodes.PartyInfo, Convert.ToHexString([.. body]));
    }

    // A little-endian base-128 varint, as the game writes them.
    private static IEnumerable<byte> VarInt(uint value)
    {
        for (; value >= 0x80; value >>= 7) yield return (byte)(value | 0x80);
        yield return (byte)value;
    }
}
