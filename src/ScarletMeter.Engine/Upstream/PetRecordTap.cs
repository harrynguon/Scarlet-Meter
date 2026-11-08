using Microsoft.Extensions.Logging;
using ScarletMeter.Engine.Pets;
using ScarletMeter.Engine.Records;

namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// The player's pet records from the captured stream: upstream decodes none, so <see cref="RecordTap"/> hands this the
/// four of them (<see cref="PetRecords"/>), plain or from an LZ4 bundle, and it raises each one read. It keeps nothing:
/// whose collection a record is about (the server it was read on) is the subscriber's to settle, as for the field bosses.
/// </summary>
/// <remarks>
/// Runs on the capture thread, inside upstream's PacketAccumulator callback (see <see cref="RecordTap"/>, which contains
/// anything that escapes). The log says when the collection is read, never how many pets it lists (Codex P1 on #76: the
/// count is the player's progress), and once (until one reads again) when a kind of record does not read; never a pet's
/// level or souls, and nothing for each pickup, which comes with every kill while farming. A pickup that gave no souls is
/// no misread: it is neither raised nor logged. A subscriber's failure is logged each time, so the rest of a bundle is still
/// read.
/// </remarks>
internal sealed class PetRecordTap : IRecordReader
{
    private readonly TimeProvider clock;
    private readonly ILogger<PetRecordTap> logger;
    // The opcodes whose last record did not read, logged once until one reads. Touched only on the reading thread.
    private readonly HashSet<ushort> failing = [];

    public PetRecordTap(TimeProvider clock, ILogger<PetRecordTap> logger)
    {
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>Raised with every pet record read, on the capture thread.</summary>
    public event EventHandler<PetRecord>? Observed;

    /// <summary>The four pet records.</summary>
    public bool Reads(ushort opcode) => PetRecords.IsPetRecord(opcode);

    /// <summary>Reads one pet record from the stream (<see cref="RecordTap"/>).</summary>
    public void Read(byte[] record, ushort opcode) => Take(record, opcode);

    /// <summary>Nothing to forget: the tap keeps nothing.</summary>
    public void ConnectionStarted()
    {
    }

    /// <summary>Reads one record when it is a pet record, for tests.</summary>
    internal void Observe(byte[] record)
    {
        if (GameRecords.OpcodeOf(record) is { } opcode && Reads(opcode)) Take(record, opcode);
    }

    private void Take(ReadOnlySpan<byte> record, ushort opcode)
    {
        if (PetRecords.Read(record, clock.GetUtcNow()) is not { } read)
        {
            if (failing.Add(opcode)) logger.LogInformation("A pet record ({Opcode}) did not read; nothing taken from it.", Bytes(opcode));
            return;
        }
        if (failing.Remove(opcode)) logger.LogInformation("Pet records ({Opcode}) read again.", Bytes(opcode));
        if (read is PetSoulsGained { Souls.Count: 0 }) return;
        if (read is PetCollectionRead) logger.LogInformation("The pet collection was read.");
        try
        {
            Observed?.Invoke(this, read);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "A pet record subscriber failed.");
        }
    }

    // The opcode as the bytes the game sends ("0D 90").
    private static string Bytes(ushort opcode) => $"{opcode & 0xFF:X2} {opcode >> 8:X2}";
}
