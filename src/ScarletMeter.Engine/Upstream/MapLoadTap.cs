using Microsoft.Extensions.Logging;
using ScarletMeter.Engine.Maps;
using ScarletMeter.Engine.Records;

namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// The map the game last loaded, from the captured stream: upstream decodes no map record, so <see cref="RecordTap"/>
/// hands this the game's map-loaded records (<c>21 36</c>) from beside upstream's pipeline, plain or from an LZ4
/// bundle, and it reads them with <see cref="MapLoadRecords"/>. The game sends one on every zone load, but the one
/// right after a server change can be lost to upstream's reassembly (see <see cref="RecordTap"/>), so the map is
/// forgotten when the game moves to a new connection and stays unknown until a load is read on it; a meter started
/// mid-session has none until the next load it reads. The id is shown in Settings, About, logged, and followed by
/// <see cref="AbyssPause"/>.
/// </summary>
/// <remarks>
/// Runs on the capture thread, inside upstream's PacketAccumulator callback (see <see cref="RecordTap"/>, which contains
/// anything that escapes). A change is logged once, as the number alone: the record carries more after the id, which
/// A2Tools does not document (it may hold a position) and which is never read, logged or kept. A subscriber's failure
/// is logged each time, so the rest of a bundle is still read.
/// </remarks>
internal sealed class MapLoadTap : IRecordReader
{
    private readonly ILogger<MapLoadTap> logger;
    private readonly Lock gate = new();
    private int? current;

    public MapLoadTap(ILogger<MapLoadTap> logger)
    {
        this.logger = logger;
    }

    /// <summary>
    /// The id of the map the game last loaded, or null until a load is read, and again after a move to a new connection
    /// until a load is read on it.
    /// </summary>
    public int? Current
    {
        get { lock (gate) return current; }
    }

    /// <summary>
    /// Raised when the map changes (null: no longer known), on the capture thread; a load of the same map again raises
    /// nothing.
    /// </summary>
    public event EventHandler<int?>? Changed;

    /// <summary>The map-loaded record.</summary>
    public bool Reads(ushort opcode) => opcode == MapLoadRecords.Opcode;

    /// <summary>Reads one map-loaded record from the stream (<see cref="RecordTap"/>).</summary>
    public void Read(byte[] record, ushort opcode) => Take(record);

    /// <summary>
    /// Forgets the map: the move to a new connection is a server change, and its map record may have been lost before
    /// the tap could read it (on leaving the Abyss in Harry's game it was), so the old map would be wrong.
    /// </summary>
    public void ConnectionStarted()
    {
        lock (gate)
        {
            if (current is null) return;
            current = null;
        }
        logger.LogInformation("Map unknown: the game moved to a new connection; the next map load read names it.");
        Raise(null);
    }

    /// <summary>Reads one record when it is a map-loaded record, for tests.</summary>
    internal void Observe(byte[] record)
    {
        if (GameRecords.OpcodeOf(record) is { } opcode && Reads(opcode)) Take(record);
    }

    // Keeps the id a record names and, when it differs from the last, logs it and raises Changed.
    private void Take(ReadOnlySpan<byte> record)
    {
        if (MapLoadRecords.Read(record) is not { } id) return;
        lock (gate)
        {
            if (current == id) return;
            current = id;
        }
        logger.LogInformation("Map loaded: {MapId}", id);
        Raise(id);
    }

    // Raises Changed; a subscriber that throws is logged, so the rest of a bundle is still read.
    private void Raise(int? id)
    {
        try
        {
            Changed?.Invoke(this, id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "A map subscriber failed.");
        }
    }
}
