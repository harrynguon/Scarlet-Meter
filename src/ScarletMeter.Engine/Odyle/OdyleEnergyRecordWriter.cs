namespace ScarletMeter.Engine.Odyle;

/// <summary>
/// Writes synthetic Odyle energy records in the game's framing (see <see cref="OdyleEnergyRecords"/>), for demo mode
/// and tests: never captured bytes. A record starts with its length as a varint, counted the way upstream's stream
/// reassembly counts it: the record's whole size is the varint's value plus its own length less 4, so the value is the
/// size of what follows it plus 4.
/// </summary>
internal static class OdyleEnergyRecordWriter
{
    /// <summary>A <c>0c 61</c> record whose one changed entry is the energy (not both parts 0: see AddEnergyEntry).</summary>
    public static byte[] Update(OdyleEnergy energy, uint key)
    {
        var body = new List<byte> { 1 };
        AddEnergyEntry(body, energy, key);
        return Frame(OdyleEnergyRecords.UpdateOpcode, body);
    }

    /// <summary>
    /// A <c>0b 61</c> record: unrelated data, then the list of entry counts, the energy first and its second entry
    /// after it (slot 2, the same key, a one-value entry as in the game's own list), then another entry.
    /// </summary>
    public static byte[] List(OdyleEnergy energy, uint key)
    {
        var body = new List<byte> { 0x49, 0x00, 0x00, 0x00, 0x0A, 0x01, 0x01, 0x03 };
        AddEnergyEntry(body, energy, key);
        body.AddRange([0x04, 0x02]);
        AddVarInt(body, key);
        AddVarInt(body, 20);
        body.AddRange([0x04, 0x03]);
        AddVarInt(body, key);
        AddVarInt(body, 14);
        return Frame(OdyleEnergyRecords.ListOpcode, body);
    }

    // The energy entry as the game writes it, a zero left out: both values (0c), basic only (04) or charged only (08).
    // How the game writes both at 0 is not documented, so the writer refuses it rather than make a form up.
    private static void AddEnergyEntry(List<byte> body, OdyleEnergy energy, uint key)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(energy.Basic);
        ArgumentOutOfRangeException.ThrowIfNegative(energy.Charged);
        var kind = (energy.Basic > 0, energy.Charged > 0) switch
        {
            (true, true) => OdyleEnergyRecords.BothValuesKind,
            (true, false) => OdyleEnergyRecords.FirstValueKind,
            (false, true) => OdyleEnergyRecords.SecondValueKind,
            _ => throw new ArgumentOutOfRangeException(nameof(energy), "The game's form for no energy at all is not documented."),
        };
        body.AddRange([kind, OdyleEnergyRecords.EnergySlot]);
        AddVarInt(body, key);
        if (energy.Basic > 0) AddVarInt(body, (uint)energy.Basic);
        if (energy.Charged > 0) AddVarInt(body, (uint)energy.Charged);
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
