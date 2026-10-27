using System.Reflection;
using AionDpsMeter.Services.Models;
using AionDpsMeter.Services.PacketCapture;
using AionDpsMeter.Services.PacketProcessing;
using AionDpsMeter.Services.PacketProcessing.Routing;
using Microsoft.Extensions.Logging;
using ScarletMeter.Engine.Records;

namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// The game's records beside upstream's pipeline, for the readers of records upstream does not decode
/// (<see cref="EntryListTap"/>, <see cref="MapLoadTap"/>, <see cref="AbyssArtifactTap"/>). Upstream's opcode table takes only processors marked with
/// its own internal attribute, so this listens to upstream's TCP reassembly (<see cref="TcpStreamBuffer.PacketExtracted"/>,
/// the event the pipeline itself listens to), opens LZ4 bundles with upstream's own <see cref="PacketProcessor"/> (its
/// internal ProcessPacket, bound once by reflection; a test guards the binding against the pinned release), and hands
/// each record, plain or from a bundle, to every <see cref="IRecordReader"/> that takes its opcode. A bundle is opened
/// once for all the readers, so a new reader adds no decompression.
/// </summary>
/// <remarks>
/// <para>A server change (into or out of a dungeon or the Abyss) moves the game to a new connection, which upstream
/// keeps apart by the client's port (the event's stream key). Upstream reads a new connection only from its first
/// keep-alive on and drops what comes before (PacketAccumulator, v1.11.5.341), so the first records of a new connection
/// can be lost: in Harry's game on 2026-10-26 the map record entering the Abyss came after that point and was read,
/// the ones leaving a dungeon and the Abyss came before it and were not. So the tap tells every reader when it first
/// reads a record of a connection it has not seen lately (<see cref="IRecordReader.ConnectionStarted"/>), just before
/// that record, and a reader decides whether its value outlives the move. It then raises <see cref="ConnectionStarted"/>
/// (<see cref="ICombatEngine.ConnectionStarted"/>), for what waits outside the engine on the connection left.</para>
/// <para>Runs on the capture thread, inside upstream's PacketAccumulator callback and under its lock. A record that no
/// reader takes and that is not a bundle costs a varint, two byte reads and a question to each reader. A bundle
/// (<c>ff ff</c>, most of the game's records) is opened a second time, after upstream's pipeline has opened it: a full
/// LZ4 decompression into a buffer sized from its header (up to 10 MB for a corrupt one), with a list and a copy of each
/// inner record, and upstream logs a broken frame once for each opening.</para>
/// <para>Nothing escapes. An exception thrown back into the accumulator would skip its removal of the record, so the
/// record would stay at the head of its buffer and come again with the next chunk, to upstream's pipeline too (its hits
/// counted twice), and one that always throws would stall the stream until the accumulator's 10 MB reset. A reader that
/// throws does not keep the record from the other readers, nor the rest of a bundle from any; a failure is logged once
/// until a record is read without one.</para>
/// </remarks>
internal sealed class RecordTap : IDisposable
{
    // Upstream's opcode for an LZ4 bundle of records (PacketOpcodes.CompressedStream, bytes ff ff).
    private const ushort BundleOpcode = 0xFFFF;

    /// <summary>How many connections are remembered as seen; an older one coming back counts as new, which is safe.</summary>
    internal const int ConnectionsRemembered = 16;

    private readonly TcpStreamBuffer stream;
    private readonly Func<byte[], List<Packet>>? openBundle;
    private readonly IRecordReader[] readers;
    private readonly ILogger<RecordTap> logger;
    // The connections (upstream's stream keys) records came from lately, oldest first, so a trailing record of the old
    // connection after a move does not count as another move. Touched only on the capture thread, like failing.
    private readonly Queue<string> recentConnections = new();
    private readonly HashSet<string> recentConnectionSet = [];
    private bool failing; // touched only on the capture thread: upstream's CaptureDevice has one processing thread
    private bool disposed;

    /// <param name="pipeline">
    /// Upstream's pipeline, taken only so the container builds it first: it subscribes to the stream in its constructor,
    /// so upstream has read each record, and every record of a bundle, before the tap hands it on. The entry list's
    /// owner depends on it (<see cref="EntryListTap"/> asks upstream who the player's own record named).
    /// </param>
    public RecordTap(TcpStreamBuffer stream, PacketProcessor processor, IPacketService pipeline, IEnumerable<IRecordReader> readers, ILogger<RecordTap> logger)
        : this(stream, BindBundleOpener(processor), readers, logger)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
    }

    /// <summary>With a bundle opener of the caller's (null for none): the constructor above, and tests.</summary>
    internal RecordTap(TcpStreamBuffer stream, Func<byte[], List<Packet>>? openBundle, IEnumerable<IRecordReader> readers, ILogger<RecordTap> logger)
    {
        this.stream = stream;
        this.openBundle = openBundle;
        this.readers = [.. readers];
        this.logger = logger;
        if (openBundle is null)
            logger.LogWarning("Odyle energy and the map are read only from records outside LZ4 bundles: upstream's PacketProcessor no longer has the ProcessPacket(byte[]) this reads them with.");
        stream.PacketExtracted += OnPacketExtracted;
    }

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

    /// <summary>
    /// Reads one record from the stream: hands it to each reader that takes its opcode, or opens a bundle and does so
    /// for every record inside. Returns the first failure (the bundle opener's, or a reader's once every other reader
    /// and record has had its turn), or null when there was none.
    /// </summary>
    internal Exception? Observe(byte[] record)
    {
        if (GameRecords.OpcodeOf(record) is not { } opcode) return null;
        if (opcode != BundleOpcode) return Hand(record, opcode);
        if (openBundle is null) return null;
        List<Packet> inner;
        try
        {
            inner = openBundle(record);
        }
        catch (Exception ex)
        {
            return ex;
        }
        Exception? failure = null;
        foreach (var packet in inner)
        {
            if (packet.Data is null || GameRecords.OpcodeOf(packet.Data) is not { } innerOpcode) continue;
            var failed = Hand(packet.Data, innerOpcode);
            failure ??= failed;
        }
        return failure;
    }

    // Hands a record to every reader that takes its opcode; one reader's failure does not keep it from the others.
    private Exception? Hand(byte[] record, ushort opcode)
    {
        Exception? failure = null;
        foreach (var reader in readers)
        {
            if (!reader.Reads(opcode)) continue;
            try
            {
                reader.Read(record, opcode);
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }
        return failure;
    }

    /// <summary>
    /// Notes the connection a record came from and says whether it is one not seen lately: the first record the tap
    /// reads of a new connection. Remembers the last <see cref="ConnectionsRemembered"/>.
    /// </summary>
    internal bool IsNewConnection(string connection)
    {
        if (recentConnectionSet.Contains(connection)) return false;
        recentConnections.Enqueue(connection);
        recentConnectionSet.Add(connection);
        if (recentConnections.Count > ConnectionsRemembered) recentConnectionSet.Remove(recentConnections.Dequeue());
        return true;
    }

    /// <summary>The connections remembered, for tests.</summary>
    internal int RememberedConnections => recentConnections.Count;

    /// <summary>
    /// Raised when the tap first reads a record of a connection it has not seen lately (a server change or a switch), after
    /// every reader was told and just before that record is read, on the capture thread. Each subscriber is called on its
    /// own, so one that throws keeps the move from no other (it guards what a subscriber holds); a failure is logged as a
    /// reader's is.
    /// </summary>
    public event EventHandler? ConnectionStarted;

    // Tells every reader the game has moved to a new connection, then the subscribers; one's failure does not keep it
    // from the others.
    private Exception? StartConnection()
    {
        Exception? failure = null;
        foreach (var reader in readers)
        {
            try
            {
                reader.ConnectionStarted();
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }
        foreach (var subscriber in ConnectionStarted?.GetInvocationList() ?? [])
        {
            try
            {
                ((EventHandler)subscriber)(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }
        return failure;
    }

    // Upstream's reassembly found a record: note a new connection, then read it, letting nothing back into the
    // accumulator (see remarks).
    private void OnPacketExtracted(object? sender, TcpPacketEventArgs e)
    {
        Exception? failure;
        try
        {
            var started = IsNewConnection(e.StreamKey) ? StartConnection() : null;
            var read = Observe(e.Payload);
            failure = started ?? read;
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        if (failure is null)
        {
            if (failing) logger.LogInformation("Reading the game's records beside upstream's pipeline works again.");
            failing = false;
            return;
        }
        if (!failing) logger.LogWarning(failure, "Reading the game's records beside upstream's pipeline (Odyle energy, the map, the Abyss's artifacts) failed; trying again with the next record.");
        failing = true;
    }

    /// <summary>Stops listening to the stream. Safe to call twice (the engine and the container both do).</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        stream.PacketExtracted -= OnPacketExtracted;
    }
}
