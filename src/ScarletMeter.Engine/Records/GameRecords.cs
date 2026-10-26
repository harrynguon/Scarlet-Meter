namespace ScarletMeter.Engine.Records;

/// <summary>
/// The framing every game record shares, as upstream's stream reassembly hands records over: the record's length as a
/// varint, two opcode bytes, then its body. Shared by the readers of records upstream does not decode (Odyle energy,
/// the map loaded) and the tap that hands records to them. Pure and allocation-free; every read is bounds-checked.
/// </summary>
internal static class GameRecords
{
    /// <summary>
    /// The two opcode bytes of a record, read little-endian (bytes <c>21 36</c> are <c>0x3621</c>), or null when the
    /// record is too short to have them.
    /// </summary>
    public static ushort? OpcodeOf(ReadOnlySpan<byte> record) => TryOpen(record, out var opcode, out _) ? opcode : null;

    /// <summary>A record's opcode and body (everything after the opcode), or false when it is too short to have an opcode.</summary>
    public static bool TryOpen(ReadOnlySpan<byte> record, out ushort opcode, out ReadOnlySpan<byte> body)
    {
        opcode = 0;
        body = default;
        if (!TryReadVarInt(record, 0, out _, out var at) || record.Length < at + 2) return false;
        opcode = (ushort)(record[at] | (record[at + 1] << 8));
        body = record[(at + 2)..];
        return true;
    }

    /// <summary>
    /// A little-endian base-128 varint of at most five bytes, as upstream's PacketReader reads them. One that does not
    /// fit 32 bits is refused rather than wrapped, so it can never pass for a small value.
    /// </summary>
    public static bool TryReadVarInt(ReadOnlySpan<byte> data, int at, out uint value, out int next)
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
