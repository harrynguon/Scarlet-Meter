using Microsoft.Extensions.Logging;
using ScarletMeter.Engine.Abyss;
using ScarletMeter.Engine.Records;

namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// Who holds each Abyss artifact, from the captured stream: upstream decodes no artifact record, so
/// <see cref="RecordTap"/> hands this the Abyss map's record and the one on entering the Abyss, plain or from an LZ4
/// bundle, and it reads them with <see cref="AbyssArtifactRecords"/>. It keeps nothing but the last reading, to log a
/// change once: whose world a reading is (the server it was read on) is the roster's to settle, as for a corridor entry.
/// </summary>
/// <remarks>
/// Runs on the capture thread, inside upstream's PacketAccumulator callback (see <see cref="RecordTap"/>, which contains
/// anything that escapes). The holders are the world's, not the player's, but the log says only that they were read and
/// from which record. A subscriber's failure is logged each time, so the rest of a bundle is still read.
/// </remarks>
internal sealed class AbyssArtifactTap : IRecordReader
{
    private readonly TimeProvider clock;
    private readonly ILogger<AbyssArtifactTap> logger;
    private readonly Lock gate = new();
    private ArtifactRecord? last;

    public AbyssArtifactTap(TimeProvider clock, ILogger<AbyssArtifactTap> logger)
    {
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>Raised with every artifact record read, on the capture thread.</summary>
    public event EventHandler<AbyssArtifacts>? Observed;

    /// <summary>The Abyss map's record and the one on entering the Abyss.</summary>
    public bool Reads(ushort opcode) => AbyssArtifactRecords.IsArtifacts(opcode);

    /// <summary>Reads one artifact record from the stream (<see cref="RecordTap"/>).</summary>
    public void Read(byte[] record, ushort opcode) => Take(record);

    /// <summary>Nothing to forget: the holders are the world's, not the connection's.</summary>
    public void ConnectionStarted()
    {
    }

    /// <summary>Reads one record when it is an artifact record, for tests.</summary>
    internal void Observe(byte[] record)
    {
        if (GameRecords.OpcodeOf(record) is { } opcode && Reads(opcode)) Take(record);
    }

    // Raises what a record says, with when it was read; logs it when it differs from the last record read.
    private void Take(ReadOnlySpan<byte> record)
    {
        if (AbyssArtifactRecords.Read(record) is not { } read) return;
        var map = GameRecords.OpcodeOf(record) == AbyssArtifactRecords.MapOpcode;
        bool changed;
        lock (gate)
        {
            changed = last is null || !last.Wars.SequenceEqual(read.Wars) || !last.Artifacts.SequenceEqual(read.Artifacts);
            last = read;
        }
        if (changed) logger.LogInformation("Abyss artifact holders read from {Record}.", map ? "the Abyss map (07 e3)" : "entering the Abyss (05 e3)");
        var artifacts = new AbyssArtifacts(
            [.. read.Wars.Select(war => new ArtifactWar(war.EventId, Time(war.SinceMs), Time(war.UntilMs)))],
            [.. read.Artifacts.Select(artifact => new AbyssArtifact(artifact.Id, Holder(artifact.Holder)))],
            clock.GetUtcNow());
        try
        {
            Observed?.Invoke(this, artifacts);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "An Abyss artifact subscriber failed.");
        }
    }

    // A time the reader took (0, or 2020 to 2100) as an instant; 0 is none.
    private static DateTimeOffset? Time(long ms) => ms == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(ms);

    // The holder's byte as A2Tools reads it; a value it has not seen is not named.
    private static ArtifactHolder Holder(byte holder) => holder switch
    {
        0 => ArtifactHolder.Nobody,
        1 => ArtifactHolder.Elyos,
        2 => ArtifactHolder.Asmodian,
        _ => ArtifactHolder.Unknown,
    };
}
