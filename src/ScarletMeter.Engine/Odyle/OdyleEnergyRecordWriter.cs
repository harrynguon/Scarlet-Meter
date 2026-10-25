namespace ScarletMeter.Engine.Odyle;

/// <summary>
/// Writes synthetic Odyle energy records in the game's framing (see <see cref="OdyleEnergyRecords"/>), for demo mode
/// and tests: never captured bytes. A record starts with its length as a varint, counted the way upstream's stream
/// reassembly counts it: the record's whole size is the varint's value plus its own length less 4, so the value is the
/// size of what follows it plus 4.
/// </summary>
internal static class OdyleEnergyRecordWriter
{
    /// <summary>A <c>0c 61</c> record whose one changed entry is the energy.</summary>
    public static byte[] Update(OdyleEnergy energy, uint key)
    {
        var body = new List<byte> { 1 };
        AddEnergyEntry(body, energy, key);
        return Frame(OdyleEnergyRecords.UpdateOpcode, body);
    }

    /// <summary>
    /// A <c>0b 61</c> record: unrelated data, then the list of entry counts, the energy first and its second entry
    /// (slot 2, the same key) after it, then a one-value entry.
    /// </summary>
    public static byte[] List(OdyleEnergy energy, uint key)
    {
        var body = new List<byte> { 0x49, 0x00, 0x00, 0x00, 0x0A, 0x01, 0x01, 0x03 };
        AddEnergyEntry(body, energy, key);
        body.AddRange([0x0C, 0x02]);
        AddVarInt(body, key);
        AddVarInt(body, 14);
        AddVarInt(body, 0);
        body.AddRange([0x04, 0x03]);
        AddVarInt(body, key + 1);
        AddVarInt(body, 2);
        return Frame(OdyleEnergyRecords.ListOpcode, body);
    }

    private static void AddEnergyEntry(List<byte> body, OdyleEnergy energy, uint key)
    {
        body.AddRange([0x0C, 0x01]);
        AddVarInt(body, key);
        AddVarInt(body, (uint)energy.Basic);
        AddVarInt(body, (uint)energy.Charged);
    }

    // Length varint, two opcode bytes (little-endian), body.
    private static byte[] Frame(ushort opcode, List<byte> body)
    {
        var record = new List<byte>(body.Count + 8);
        AddVarInt(record, (uint)(2 + body.Count + 4));
        record.Add((byte)(opcode & 0xFF));
        record.Add((byte)(opcode >> 8));
        record.AddRange(body);
        return [.. record];
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
