using System.Buffers.Binary;
using AionDpsMeter.Services.PacketCapture;
using AionDpsMeter.Services.PacketProcessing;
using AionDpsMeter.Services.Services.Entity;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Session.Persistence;
using K4os.Compression.LZ4;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ScarletMeter.Engine;
using ScarletMeter.Engine.Odyle;
using ScarletMeter.Engine.Upstream;
using ScarletMeter.Engine.Upstream.Demo;

namespace ScarletMeter.Tests;

/// <summary>
/// Odyle energy: the game's two records as A2Tools documented them (OdyleEnergyRecords), read from upstream's real
/// stream reassembly beside its pipeline, plain or inside an LZ4 bundle (OdyleEnergyTap), and the demo's records.
/// Every byte here is synthetic, written by hand or by OdyleEnergyRecordWriter; none was captured.
/// </summary>
public sealed class OdyleEnergyTests
{
    private const uint Key = 0x1092; // made up; "92 21" as a varint
    private static readonly OdyleEnergy Sample = new(30, 880);

    // An update whose one entry is the energy, byte for byte: length 0x0e (10 bytes follow, plus 4), opcode 0c 61,
    // one entry, kind 0c, slot 01, key, basic 30, charged 880 ("f0 06").
    [Fact]
    public void AnUpdateIsReadAsTheGameWritesIt()
    {
        var record = Hex("0E 0C 61 01 0C 01 92 21 1E F0 06");
        Assert.Equal(Sample, OdyleEnergyRecords.Read(record));
        Assert.Equal(record, OdyleEnergyRecordWriter.Update(Sample, Key));
    }

    [Fact]
    public void AnUpdateFindsTheEnergyAmongOtherEntries()
    {
        // Two entries: a one-value entry (kind 04) first, then the energy.
        Assert.Equal(Sample, OdyleEnergyRecords.Read(Hex("12 0C 61 02 04 05 01 07 0C 01 92 21 1E F0 06")));
    }

    [Theory]
    [InlineData("07 0C 61 00")]                 // no entry changed
    [InlineData("0B 0C 61 01 04 02 01 03")]     // only a one-value entry
    [InlineData("0D 0C 61 01 0C 02 92 21 1E F0 06")] // a two-value entry in slot 2, not the energy
    [InlineData("0E 0C 61 02 04 05 01 07")]     // says two entries, holds one
    [InlineData("08 0C 61 01 0C")]              // cut inside the entry
    public void AnUpdateWithoutTheEnergyGivesNone(string hex) => Assert.Null(OdyleEnergyRecords.Read(Hex(hex)));

    [Fact]
    public void TheListFindsTheEnergyByItsShape()
    {
        Assert.Equal(Sample, OdyleEnergyRecords.Read(OdyleEnergyRecordWriter.List(Sample, Key)));

        // A "0c 01" in the data before the list, which no slot-2 entry with its key follows, is passed over.
        Assert.Equal(Sample, OdyleEnergyRecords.Read(Frame(0x610B,
            "0C 01 05 09 09 00 00 0C 01 92 21 1E F0 06 0C 02 92 21 0E 00 04 03 93 21 02")));
    }

    [Theory]
    [InlineData("0C 01 92 21 1E F0 06 0C 02 93 21 0E 00")] // the second entry has another key
    [InlineData("0C 01 92 21 1E F0 06 0C 03 92 21 0E 00")] // the second entry is not in slot 2
    [InlineData("0C 01 92 21 1E F0 06")]                   // no second entry
    [InlineData("04 01 92 21 1E 0C 02 92 21 0E 00")]       // a one-value entry in slot 1
    public void AListWithoutTheShapeGivesNone(string body) => Assert.Null(OdyleEnergyRecords.Read(Frame(0x610B, body)));

    [Fact]
    public void ImplausibleValuesAreTurnedAway()
    {
        Assert.Equal(new OdyleEnergy(OdyleEnergyRecords.MaxValue, 0), OdyleEnergyRecords.Read(OdyleEnergyRecordWriter.Update(new(OdyleEnergyRecords.MaxValue, 0), Key)));
        Assert.Null(OdyleEnergyRecords.Read(OdyleEnergyRecordWriter.Update(new(OdyleEnergyRecords.MaxValue + 1, 0), Key)));
        Assert.Null(OdyleEnergyRecords.Read(OdyleEnergyRecordWriter.Update(new(0, OdyleEnergyRecords.MaxValue + 1), Key)));
    }

    // A five-byte varint whose top byte carries bits past 32 is refused, so it cannot wrap round to a small value.
    [Fact]
    public void AVarIntPastThirtyTwoBitsIsRefused()
    {
        Assert.Null(OdyleEnergyRecords.Read(Frame(0x610C, "01 0C 01 92 21 9E 80 80 80 10 F0 06")));
        Assert.Equal(Sample, OdyleEnergyRecords.Read(Frame(0x610C, "01 0C 01 92 21 9E 80 80 80 00 F0 06"))); // 30, padded
    }

    [Fact]
    public void OtherRecordsAndCutRecordsGiveNoneAndNeverThrow()
    {
        Assert.Null(OdyleEnergyRecords.Read(Hex("0E 03 36 01 0C 01 92 21 1E F0 06"))); // another opcode
        Assert.Null(OdyleEnergyRecords.Read([]));
        Assert.Null(OdyleEnergyRecords.Read(Hex("80")));
        foreach (var record in new[] { OdyleEnergyRecordWriter.List(Sample, Key), OdyleEnergyRecordWriter.Update(Sample, Key) })
            for (var length = 0; length < record.Length; length++)
                _ = OdyleEnergyRecords.Read(record.AsSpan(0, length));
    }

    // Through upstream's real reassembly (TcpStreamBuffer), once it is in step with the stream (Synchronise).
    [Fact]
    public void TheEngineReadsOdyleEnergyFromTheStream()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var engine = host.Services.GetRequiredService<ICombatEngine>();
        var stream = host.Services.GetRequiredService<TcpStreamBuffer>();
        var raised = new List<OdyleEnergy>();
        engine.OdyleEnergyUpdated += (_, energy) => raised.Add(energy);
        Assert.Null(engine.OdyleEnergy);

        Synchronise(stream);
        stream.AddData("synthetic", OdyleEnergyRecordWriter.List(new(420, 1200), Key), 0);
        Assert.Equal(new OdyleEnergy(420, 1200), engine.OdyleEnergy);
        stream.AddData("synthetic", OdyleEnergyRecordWriter.Update(Sample, Key), 0);
        stream.AddData("synthetic", OdyleEnergyRecordWriter.Update(Sample, Key), 0); // the same value again
        Assert.Equal(Sample, engine.OdyleEnergy);
        Assert.Equal([new OdyleEnergy(420, 1200), Sample], raised);

        // Kept across a reset of the fight.
        engine.Reset();
        Assert.Equal(Sample, engine.OdyleEnergy);
    }

    // Most of the game's records arrive in LZ4 bundles (ff ff), which the tap opens with upstream's own PacketProcessor.
    [Fact]
    public void TheEngineReadsOdyleEnergyInsideABundle()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var engine = host.Services.GetRequiredService<ICombatEngine>();
        var stream = host.Services.GetRequiredService<TcpStreamBuffer>();
        var other = Frame(0x3603, "00 00 00 00 00 00 00 00 00");
        Synchronise(stream);
        stream.AddData("synthetic", Bundle([.. other, .. OdyleEnergyRecordWriter.Update(Sample, Key)]), 0);
        Assert.Equal(Sample, engine.OdyleEnergy);
    }

    // The reflection bind to upstream's internal ProcessPacket holds for the pinned release; an update of the pin
    // that renames or reshapes it fails here.
    [Fact]
    public void TheBundleOpenerBindsToThePinnedUpstream()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var open = OdyleEnergyTap.BindBundleOpener(host.Services.GetRequiredService<PacketProcessor>());
        Assert.NotNull(open);
        var inner = OdyleEnergyRecordWriter.Update(Sample, Key);
        var records = open(Bundle(inner));
        Assert.Equal(inner, Assert.Single(records).Data);
    }

    // Nothing the tap meets escapes into upstream's reassembly, where an exception would stop the rest of the chunk's
    // records from being read (by the tap and by upstream's own pipeline): a broken bundle, a subscriber that throws.
    [Fact]
    public void NothingEscapesIntoTheStream()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var engine = host.Services.GetRequiredService<ICombatEngine>();
        var stream = host.Services.GetRequiredService<TcpStreamBuffer>();
        engine.OdyleEnergyUpdated += (_, _) => throw new InvalidOperationException("A failing subscriber.");
        Synchronise(stream);
        stream.AddData("synthetic",
            [.. Frame(0xFFFF, "FF FF 10 00 00 00 01 02 03"), .. OdyleEnergyRecordWriter.Update(new(1, 2), Key), .. OdyleEnergyRecordWriter.Update(Sample, Key)], 0);
        Assert.Equal(Sample, engine.OdyleEnergy); // the record after the failing subscriber's was read too
    }

    // The demo sends the login list when it begins and an update as each later fight starts and spends some.
    [Fact]
    public void TheDemoSendsOdyleEnergyThroughTheStream()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var engine = host.Services.GetRequiredService<ICombatEngine>();
        var feed = new DemoCombatFeed(
            host.Services.GetRequiredService<EntityTracker>(), host.Services.GetRequiredService<CombatSessionManager>(),
            host.Services.GetRequiredService<ICombatHistoryStore>(), NullLogger.Instance, new Random(7),
            host.Services.GetRequiredService<OdyleEnergyTap>());
        var clock = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        feed.Begin(clock);
        feed.Step(clock, TestContext.Current.CancellationToken);
        Assert.Equal(new OdyleEnergy(420, 1200), engine.OdyleEnergy);

        // Past the first fight (at most 90 seconds) and its 10-second break: the second fight spends 10.
        for (var end = clock.AddSeconds(105); clock < end; clock = clock.AddMilliseconds(100))
            feed.Step(clock, TestContext.Current.CancellationToken);
        Assert.Equal(new OdyleEnergy(410, 1200), engine.OdyleEnergy);
    }

    // Brings upstream's reassembly of the "synthetic" stream into step, as live traffic eventually does. Its
    // PacketAccumulator (v1.11.5.341) looks for its keep-alive (0e 00 36…) from the end of the bytes it holds, not the
    // start, so it finds one only among bytes left from an earlier chunk: a chunk ending in a keep-alive, then a
    // shorter one, synchronises it, and the records after that are read. Should upstream search from the start, the
    // same two chunks synchronise it at once.
    private static void Synchronise(TcpStreamBuffer stream)
    {
        byte[] keepAlive = [0x0E, 0x00, 0x36, 0, 0, 0, 0, 0, 0, 0, 0];
        stream.AddData("synthetic", [.. new byte[20], .. keepAlive], 0);
        stream.AddData("synthetic", keepAlive, 0);
    }

    // An LZ4 bundle as upstream's PacketProcessor reads one: length, ff ff, the decompressed size (u32, little-endian),
    // then the compressed records.
    private static byte[] Bundle(byte[] records)
    {
        var compressed = new byte[LZ4Codec.MaximumOutputSize(records.Length)];
        var length = LZ4Codec.Encode(records, compressed);
        var size = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(size, records.Length);
        return Frame(0xFFFF, Convert.ToHexString([.. size, .. compressed.AsSpan(0, length)]));
    }

    // A record from its opcode and body, framed as OdyleEnergyRecordWriter frames them.
    private static byte[] Frame(ushort opcode, string body)
    {
        var bytes = Hex(body);
        var header = new List<byte>();
        for (var value = (uint)(2 + bytes.Length + 4); ; value >>= 7)
        {
            var b = (byte)(value & 0x7F);
            if (value >> 7 == 0) { header.Add(b); break; }
            header.Add((byte)(b | 0x80));
        }
        return [.. header, (byte)(opcode & 0xFF), (byte)(opcode >> 8), .. bytes];
    }

    private static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", string.Empty, StringComparison.Ordinal));
}
