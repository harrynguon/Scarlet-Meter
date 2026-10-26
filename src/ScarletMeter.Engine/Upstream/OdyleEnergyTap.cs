using Microsoft.Extensions.Logging;
using ScarletMeter.Engine.Odyle;
using ScarletMeter.Engine.Records;

namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// Odyle energy from the captured stream. Upstream decodes no Odyle record, so <see cref="RecordTap"/> hands this the
/// game's two Odyle records from beside upstream's pipeline, plain or from an LZ4 bundle, and it reads them with
/// <see cref="OdyleEnergyRecords"/>. The value is kept until the game sends another: it comes at login, after a server
/// transfer and when it changes, so a meter started mid-session has none until then. The energy entry's key from the
/// last list picks the energy among an update's entries; before any list, an update's first slot-1 entry is taken.
/// </summary>
/// <remarks>
/// Runs on the capture thread, inside upstream's PacketAccumulator callback (see <see cref="RecordTap"/>, which contains
/// anything that escapes), or on the demo feed's thread, whose records come through <see cref="Observe"/>. A
/// subscriber's failure is logged each time, so the rest of a bundle is still read.
/// </remarks>
internal sealed class OdyleEnergyTap : IRecordReader
{
    private readonly ILogger<OdyleEnergyTap> logger;
    private readonly Lock gate = new();
    private OdyleEnergy? current;
    // The energy entry's key from the last list, so an update's other slot-1 entries are passed over; null until a
    // list comes (OdyleEnergyRecords.ReadEntry). Under the gate: the capture and the demo feed both read records.
    private uint? energyKey;

    public OdyleEnergyTap(ILogger<OdyleEnergyTap> logger)
    {
        this.logger = logger;
    }

    /// <summary>The latest Odyle energy the game sent, or null until it sends one.</summary>
    public OdyleEnergy? Current
    {
        get { lock (gate) return current; }
    }

    /// <summary>Raised when the value changes, on the capture (or demo feed) thread.</summary>
    public event EventHandler<OdyleEnergy>? Updated;

    /// <summary>The list of entry counts and its updates.</summary>
    public bool Reads(ushort opcode) => opcode is OdyleEnergyRecords.ListOpcode or OdyleEnergyRecords.UpdateOpcode;

    /// <summary>Reads one Odyle record from the stream (<see cref="RecordTap"/>).</summary>
    public void Read(byte[] record, ushort opcode) => Take(record, opcode);

    /// <summary>Nothing to forget: the energy is the character's, so it stays the same on a new connection.</summary>
    public void ConnectionStarted()
    {
    }

    /// <summary>Reads one record when it is an Odyle record: the demo feed's way in (its records are never bundled), and tests'.</summary>
    internal void Observe(byte[] record)
    {
        if (GameRecords.OpcodeOf(record) is { } opcode && Reads(opcode)) Take(record, opcode);
    }

    // Reads one Odyle record with the energy key from the last list, keeps the key a list defines, and reports the
    // energy it holds.
    private void Take(ReadOnlySpan<byte> record, ushort opcode)
    {
        OdyleEnergyReading? reading;
        lock (gate)
        {
            reading = OdyleEnergyRecords.ReadEntry(record, energyKey);
            if (reading is { } read && opcode == OdyleEnergyRecords.ListOpcode) energyKey = read.Key;
        }
        if (reading is { } value) Report(value.Energy, opcode);
    }

    // Keeps a value read from a record and raises Updated when it differs from the last; a subscriber that throws is
    // logged, so the rest of a bundle is still read. A change is logged with the kind of record it came from, never
    // the value (the player's progress) or the bytes, so a wrong reading can be traced to the list or an update.
    private void Report(OdyleEnergy value, ushort opcode)
    {
        lock (gate)
        {
            if (current == value) return;
            current = value;
        }
        logger.LogInformation("Odyle energy changed, read from {Record}.",
            opcode == OdyleEnergyRecords.ListOpcode ? "the list of entry counts (0b 61)" : "an update (0c 61)");
        try
        {
            Updated?.Invoke(this, value);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "An Odyle energy subscriber failed.");
        }
    }
}
