using System.Reflection;
using AionDpsMeter.Services.PacketCapture;
using AionDpsMeter.Services.PacketProcessing;
using AionDpsMeter.Services.PacketProcessing.Routing;
using Microsoft.Extensions.Logging;
using ScarletMeter.Engine.Odyle;

namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// Odyle energy from the captured stream. Upstream decodes no Odyle record, and its opcode table takes only processors
/// marked with its own internal attribute, so this listens to upstream's TCP reassembly beside its pipeline
/// (<see cref="TcpStreamBuffer.PacketExtracted"/>, the event the pipeline itself listens to), opens LZ4 bundles with
/// upstream's own <see cref="PacketProcessor"/> (its internal ProcessPacket, bound once by reflection; a test guards
/// the binding against the pinned release), and reads the two Odyle records with <see cref="OdyleEnergyRecords"/>.
/// The value is kept until the game sends another: it comes at login, after a server transfer and when it changes, so
/// a meter started mid-session has none until then.
/// </summary>
/// <remarks>
/// <para>Runs on the capture thread, inside upstream's PacketAccumulator callback and under its lock. A record that is
/// neither an Odyle record nor a bundle costs a varint and two byte reads. A bundle (<c>ff ff</c>, most of the game's
/// records) is opened a second time, after upstream's pipeline has opened it: a full LZ4 decompression into a buffer
/// sized from its header (up to 10 MB for a corrupt one), with a list and a copy of each inner record, and upstream
/// logs a broken frame once for each opening.</para>
/// <para>Nothing escapes. An exception thrown back into the accumulator would skip its removal of the record, so the
/// record would stay at the head of its buffer and come again with the next chunk, to upstream's pipeline too (its hits
/// counted twice), and one that always throws would stall the stream until the accumulator's 10 MB reset. A reading
/// failure is logged once until reading works again; a subscriber's, each time.</para>
/// </remarks>
internal sealed class OdyleEnergyTap : IDisposable
{
    // Upstream's opcode for an LZ4 bundle of records (PacketOpcodes.CompressedStream, bytes ff ff).
    private const ushort BundleOpcode = 0xFFFF;

    private readonly TcpStreamBuffer stream;
    private readonly Func<byte[], List<Packet>>? openBundle;
    private readonly ILogger<OdyleEnergyTap> logger;
    private readonly Lock gate = new();
    private OdyleEnergy? current;
    private bool failing; // touched only on the capture thread: upstream's CaptureDevice has one processing thread
    private bool disposed;

    public OdyleEnergyTap(TcpStreamBuffer stream, PacketProcessor processor, ILogger<OdyleEnergyTap> logger)
        : this(stream, BindBundleOpener(processor), logger)
    {
    }

    /// <summary>With a bundle opener of the caller's (null for none): the constructor above, and tests.</summary>
    internal OdyleEnergyTap(TcpStreamBuffer stream, Func<byte[], List<Packet>>? openBundle, ILogger<OdyleEnergyTap> logger)
    {
        this.stream = stream;
        this.openBundle = openBundle;
        this.logger = logger;
        if (openBundle is null)
            logger.LogWarning("Odyle energy is shown only from records outside LZ4 bundles: upstream's PacketProcessor no longer has the ProcessPacket(byte[]) this reads them with.");
        stream.PacketExtracted += OnPacketExtracted;
    }

    /// <summary>The latest Odyle energy the game sent, or null until it sends one.</summary>
    public OdyleEnergy? Current
    {
        get { lock (gate) return current; }
    }

    /// <summary>Raised when the value changes, on the capture (or demo feed) thread.</summary>
    public event EventHandler<OdyleEnergy>? Updated;

    /// <summary>
    /// Upstream's PacketProcessor.ProcessPacket(byte[]) as a delegate: a record that is an LZ4 bundle comes back as
    /// the records inside it, each with its own length and opcode; any other record comes back alone. Null when the
    /// pinned release no longer has it with that shape, or it cannot be bound, so the engine still starts.
    /// </summary>
    internal static Func<byte[], List<Packet>>? BindBundleOpener(PacketProcessor processor)
    {
        var method = typeof(PacketProcessor).GetMethod(
            "ProcessPacket", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, binder: null, [typeof(byte[])], modifiers: null);
        if (method?.ReturnType != typeof(List<Packet>)) return null;
        try
        {
            return method.CreateDelegate<Func<byte[], List<Packet>>>(processor);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Reads one record from the stream: an Odyle record, or a bundle that may hold one.</summary>
    internal void Observe(byte[] record)
    {
        var opcode = OdyleEnergyRecords.OpcodeOf(record);
        if (opcode is OdyleEnergyRecords.ListOpcode or OdyleEnergyRecords.UpdateOpcode)
        {
            Report(OdyleEnergyRecords.Read(record), opcode.Value);
            return;
        }
        if (opcode != BundleOpcode || openBundle is null) return;
        foreach (var inner in openBundle(record))
        {
            if (inner.Data is null) continue;
            if (OdyleEnergyRecords.OpcodeOf(inner.Data) is { } innerOpcode
                && innerOpcode is OdyleEnergyRecords.ListOpcode or OdyleEnergyRecords.UpdateOpcode)
                Report(OdyleEnergyRecords.Read(inner.Data), innerOpcode);
        }
    }

    // Upstream's reassembly found a record: read it, letting nothing back into the accumulator (see remarks).
    private void OnPacketExtracted(object? sender, TcpPacketEventArgs e)
    {
        try
        {
            Observe(e.Payload);
            if (failing) logger.LogInformation("Reading Odyle energy works again.");
            failing = false;
        }
        catch (Exception ex)
        {
            if (!failing) logger.LogWarning(ex, "Reading Odyle energy failed; trying again with the next record.");
            failing = true;
        }
    }

    // Keeps a value read from a record and raises Updated when it differs from the last; a subscriber that throws is
    // logged, so the rest of a bundle is still read. A change is logged with the kind of record it came from, never
    // the value (the player's progress) or the bytes, so a wrong reading can be traced to the list or an update.
    private void Report(OdyleEnergy? energy, ushort opcode)
    {
        if (energy is not { } value) return;
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

    /// <summary>Stops listening to the stream. Safe to call twice (the engine and the container both do).</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        stream.PacketExtracted -= OnPacketExtracted;
    }
}
