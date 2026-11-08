using Microsoft.Extensions.Logging;
using ScarletMeter.Engine.Maps;
using ScarletMeter.Engine.Records;

namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// The field bosses on a map, from the captured stream: upstream decodes no world-map record, so <see cref="RecordTap"/>
/// hands this the world map's boss list (<c>01 91</c>), which the game sends when the player opens the map, plain or from
/// an LZ4 bundle, and it reads it with <see cref="FieldBossMapRecords"/>. Only a list whose two bytes before the map id
/// are 0 is raised, as A2Tools shares only those: another value may be a map seen from a rift, another server's. It keeps
/// nothing: whose world a reading is (the server it was read on) is the subscriber's to settle, as for the Abyss's
/// artifact holders.
/// </summary>
/// <remarks>
/// Runs on the capture thread, inside upstream's PacketAccumulator callback (see <see cref="RecordTap"/>, which contains
/// anything that escapes). The log says which map was read and how many bosses it listed, never their times. The game
/// sends the list every few seconds while the map is open (Harry's log, 2026-11-08: a line every three seconds), so a line
/// is written when it says something new, or <see cref="LogEvery"/> after the same one. A subscriber's failure is logged
/// each time, so the rest of a bundle is still read.
/// </remarks>
internal sealed class FieldBossMapTap : IRecordReader
{
    /// <summary>How long the same line waits before it is written again.</summary>
    public static readonly TimeSpan LogEvery = TimeSpan.FromMinutes(10);

    private readonly TimeProvider clock;
    private readonly ILogger<FieldBossMapTap> logger;
    // The last line written and when. Touched only on the reading thread.
    private string? lastNote;
    private DateTimeOffset lastNoteAt;

    public FieldBossMapTap(TimeProvider clock, ILogger<FieldBossMapTap> logger)
    {
        this.clock = clock;
        this.logger = logger;
    }

    /// <summary>Raised with every map read, on the capture thread.</summary>
    public event EventHandler<FieldBossMap>? Observed;

    /// <summary>The world map's boss list.</summary>
    public bool Reads(ushort opcode) => opcode == FieldBossMapRecords.Opcode;

    /// <summary>Reads one world-map record from the stream (<see cref="RecordTap"/>).</summary>
    public void Read(byte[] record, ushort opcode) => Take(record);

    /// <summary>Nothing to forget: the tap keeps nothing.</summary>
    public void ConnectionStarted()
    {
    }

    /// <summary>Reads one record when it is a world-map record, for tests.</summary>
    internal void Observe(byte[] record)
    {
        if (GameRecords.OpcodeOf(record) is { } opcode && Reads(opcode)) Take(record);
    }

    private void Take(ReadOnlySpan<byte> record)
    {
        var now = clock.GetUtcNow();
        if (FieldBossMapRecords.Read(record, now) is not { } read)
        {
            if (IsNew("unread", now)) logger.LogInformation("A world map's boss list (01 91) did not read; nothing taken from it.");
            return;
        }
        if (read.Header != 0)
        {
            if (IsNew($"header {read.MapId} {read.Header}", now))
                logger.LogInformation("A world map's boss list for map {MapId} came with {Header} before the map id (another server's, from a rift?); not taken.", read.MapId, read.Header);
            return;
        }
        if (IsNew($"read {read.MapId} {read.Bosses.Count}", now))
            logger.LogInformation("Field bosses read from the world map: map {MapId}, {Count} bosses.", read.MapId, read.Bosses.Count);
        var map = new FieldBossMap(
            read.MapId,
            [.. read.Bosses.Select(boss => new FieldBossOnMap(boss.Id, boss.Up, boss.TimeMs == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(boss.TimeMs)))],
            now);
        try
        {
            Observed?.Invoke(this, map);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "A field boss map subscriber failed.");
        }
    }

    // Whether a line, by its key, says something new: another line, or the same one LogEvery after it was last written.
    private bool IsNew(string key, DateTimeOffset now)
    {
        if (key == lastNote && now - lastNoteAt < LogEvery) return false;
        lastNote = key;
        lastNoteAt = now;
        return true;
    }
}
