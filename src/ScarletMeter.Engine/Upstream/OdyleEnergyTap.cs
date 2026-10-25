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
/// Runs on the capture (or demo feed) thread, inside the stream buffer's callback: a record that is neither Odyle
/// record nor a bundle costs two byte reads, and nothing escapes, since an exception thrown back into the buffer would
/// stop the records after it from reaching upstream's pipeline. A failure is logged once until reading works again.
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
    private bool failing;
    private bool disposed;

    public OdyleEnergyTap(TcpStreamBuffer stream, PacketProcessor processor, ILogger<OdyleEnergyTap> logger)
    {
        this.stream = stream;
        this.logger = logger;
        openBundle = BindBundleOpener(processor);
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
    /// pinned release no longer has it with that shape.
    /// </summary>
    internal static Func<byte[], List<Packet>>? BindBundleOpener(PacketProcessor processor)
    {
        var method = typeof(PacketProcessor).GetMethod(
            "ProcessPacket", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, binder: null, [typeof(byte[])], modifiers: null);
        return method?.ReturnType == typeof(List<Packet>) ? method.CreateDelegate<Func<byte[], List<Packet>>>(processor) : null;
    }

    /// <summary>Reads one record from the stream: an Odyle record, or a bundle that may hold one.</summary>
    internal void Observe(byte[] record)
    {
        var opcode = OdyleEnergyRecords.OpcodeOf(record);
        if (opcode is OdyleEnergyRecords.ListOpcode or OdyleEnergyRecords.UpdateOpcode)
        {
            Report(OdyleEnergyRecords.Read(record));
            return;
        }
        if (opcode != BundleOpcode || openBundle is null) return;
        foreach (var inner in openBundle(record))
        {
            if (inner.Data is null) continue;
            if (OdyleEnergyRecords.OpcodeOf(inner.Data) is OdyleEnergyRecords.ListOpcode or OdyleEnergyRecords.UpdateOpcode)
                Report(OdyleEnergyRecords.Read(inner.Data));
        }
    }

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

    private void Report(OdyleEnergy? energy)
    {
        if (energy is not { } value) return;
        lock (gate)
        {
            if (current == value) return;
            current = value;
        }
        try
        {
            Updated?.Invoke(this, value);
        }
        catch (Exception ex)
        {
            // A subscriber's failure is its own; the capture thread carries on.
            logger.LogWarning(ex, "An Odyle energy subscriber failed.");
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        stream.PacketExtracted -= OnPacketExtracted;
    }
}
