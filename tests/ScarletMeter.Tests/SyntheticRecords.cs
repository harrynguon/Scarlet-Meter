using System.Buffers.Binary;
using AionDpsMeter.Services.PacketCapture;
using K4os.Compression.LZ4;
using ScarletMeter.Engine.Abyss;
using ScarletMeter.Engine.Maps;

namespace ScarletMeter.Tests;

/// <summary>
/// Synthetic game records and the stream they travel in, for the tests of the records upstream does not decode (the
/// entry list with Odyle energy, the map loaded, the Abyss's artifact holders): framing, LZ4 bundles and bringing
/// upstream's reassembly into step. Never captured bytes.
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
}
