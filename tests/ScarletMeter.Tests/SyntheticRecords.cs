using System.Buffers.Binary;
using AionDpsMeter.Services.PacketCapture;
using K4os.Compression.LZ4;
using ScarletMeter.Engine.Maps;

namespace ScarletMeter.Tests;

/// <summary>
/// Synthetic game records and the stream they travel in, for the tests of the records upstream does not decode (Odyle
/// energy, the map loaded): framing, LZ4 bundles and bringing upstream's reassembly into step. Never captured bytes.
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

    // A record from its opcode and body, framed as OdyleEnergyRecordWriter frames them: the length varint counts what
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
}
