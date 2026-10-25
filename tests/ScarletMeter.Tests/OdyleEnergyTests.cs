using System.Buffers.Binary;
using AionDpsMeter.Services.PacketCapture;
using AionDpsMeter.Services.PacketProcessing;
using AionDpsMeter.Services.Services.Entity;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Session.Persistence;
using K4os.Compression.LZ4;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ScarletMeter.Engine;
using ScarletMeter.Engine.Odyle;
using ScarletMeter.Engine.Upstream;
using ScarletMeter.Engine.Upstream.Demo;

namespace ScarletMeter.Tests;

/// <summary>
/// Odyle energy: the game's two records as A2Tools documented them (OdyleEnergyRecords; #63 and its fixes 2723bff and
/// 29e7193: the entry's three kinds and updates that start 00), read from upstream's real
/// stream reassembly beside its pipeline, plain or inside an LZ4 bundle (OdyleEnergyTap), and the demo's records.
/// Every byte here is synthetic, written by hand or by OdyleEnergyRecordWriter; none was captured.
/// </summary>
public sealed class OdyleEnergyTests
{
    private const uint Key = 0x1092; // made up; "92 21" as a varint
    private static readonly OdyleEnergy Sample = new(30, 880);

    // An update whose one entry is the energy, in the layout A2Tools documents: length 0x0e (10 bytes follow, plus 4),
    // opcode 0c 61, one entry, kind 0c, slot 01, key, basic 30, charged 880 ("f0 06"). The writer writes exactly that.
    // The game's updates carry more bytes after the entries (A2Tools' sample does), which are ignored; these are made up.
    [Fact]
    public void AnUpdateIsReadInTheDocumentedLayout()
    {
        var record = Hex("0E 0C 61 01 0C 01 92 21 1E F0 06");
        Assert.Equal(Sample, OdyleEnergyRecords.Read(record));
        Assert.Equal(record, OdyleEnergyRecordWriter.Update(Sample, Key));
        Assert.Equal(Sample, OdyleEnergyRecords.Read(Hex("13 0C 61 01 0C 01 92 21 1E F0 06 01 17 00 00 00")));
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

    // The list's energy entry is followed by its second entry, slot 2 with the same key. In the game's own list (A2Tools'
    // sample) that follower is a one-value entry (kind 04), which the writer writes; a two-value follower is taken too
    // (audit on PR #34: every list here had a two-value follower, so a "tightening" to that kind would have passed).
    [Theory]
    [InlineData("0C 01 92 21 1E F0 06 04 02 92 21 14")]          // a one-value follower, as in the game's list
    [InlineData("0C 01 92 21 1E F0 06 0C 02 92 21 0E 00")]       // a two-value follower
    [InlineData("2A 00 00 00 05 01 01 02 0C 01 92 21 1E F0 06 04 02 92 21 14 04 03 92 21 0E")] // data before, entries after
    [InlineData("0C 01 05 09 09 00 00 0C 01 92 21 1E F0 06 04 02 92 21 14")] // a "0c 01" no follower backs is passed over
    public void TheListFindsTheEnergyByItsShape(string body) => Assert.Equal(Sample, OdyleEnergyRecords.Read(Frame(0x610B, body)));

    [Fact]
    public void TheWritersListIsTheGamesShape()
    {
        var list = OdyleEnergyRecordWriter.List(Sample, Key);
        Assert.Equal(Sample, OdyleEnergyRecords.Read(list));
        Assert.Contains("0C019221 1EF006 04029221".Replace(" ", string.Empty, StringComparison.Ordinal), Convert.ToHexString(list), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0C 01 92 21 1E F0 06 04 02 93 21 14")]    // the second entry has another key
    [InlineData("0C 01 92 21 1E F0 06 04 03 92 21 14")]    // the second entry is not in slot 2
    [InlineData("0C 01 92 21 1E F0 06 0C 02 93 21 0E 00")] // a two-value second entry with another key
    [InlineData("0C 01 92 21 1E F0 06")]                   // no second entry
    [InlineData("04 01 92 21 1E 04 03 92 21 14")]          // a one-value entry whose second entry is not in slot 2
    [InlineData("08 01 92 21 1E 04 02 93 21 14")]          // a charged-only entry whose second entry has another key
    public void AListWithoutTheShapeGivesNone(string body) => Assert.Null(OdyleEnergyRecords.Read(Frame(0x610B, body)));

    // The kind says which values follow, a zero being left out (A2Tools 2723bff and 29e7193): 0c both, 04 basic only,
    // 08 charged only. Reading only 0c missed the energy of anyone with either at 0 (Harry: "odyle counter doesnt
    // work"). In the list (with the data before it) and in an update.
    [Theory]
    [InlineData(0x610B, "0C 01 92 21 1E F0 06 04 02 92 21 0E", 30, 880)]
    [InlineData(0x610B, "04 01 92 21 C8 06 04 02 92 21 0E", 840, 0)]
    [InlineData(0x610B, "08 01 92 21 AB 0E 04 02 92 21 0E", 0, 1835)]
    [InlineData(0x610B, "2A 00 00 00 05 01 01 02 08 01 92 21 AB 0E 04 02 92 21 14 04 03 92 21 0E", 0, 1835)]
    [InlineData(0x610C, "01 04 01 92 21 C8 06", 840, 0)]
    [InlineData(0x610C, "01 08 01 92 21 DB 0D", 0, 1755)]
    [InlineData(0x610C, "02 04 05 01 07 08 01 92 21 DB 0D", 0, 1755)]
    public void TheEnergyIsReadInEachOfTheGamesKinds(int opcode, string body, int basic, int charged) =>
        Assert.Equal(new OdyleEnergy(basic, charged), OdyleEnergyRecords.Read(Frame((ushort)opcode, body)));

    // An update's first byte is not a count to trust: updates with 00 there carry an entry too (A2Tools 29e7193), so
    // at least one entry is read; one without the energy after it still gives none.
    [Theory]
    [InlineData("00 08 01 92 21 DB 0D 03", 0, 1755)]
    [InlineData("00 0C 01 92 21 1E F0 06", 30, 880)]
    [InlineData("00 00 09 92 21 03", -1, -1)]       // no entry kind after the 00
    [InlineData("00 04 0C 00 00 00 05 03", -1, -1)] // an entry in another slot
    public void AnUpdateThatStartsWithZeroIsRead(string body, int basic, int charged) =>
        Assert.Equal(basic < 0 ? (OdyleEnergy?)null : new OdyleEnergy(basic, charged), OdyleEnergyRecords.Read(Frame(0x610C, body)));

    // A count above 0 is kept, so bytes after the entries are never read as more of them (A2Tools keeps it too; audit
    // on PR #36: ignoring it passed every test).
    [Fact]
    public void AnUpdatesCountAboveZeroIsKept() =>
        Assert.Null(OdyleEnergyRecords.Read(Frame(0x610C, "01 04 05 01 07 0C 01 92 21 1E F0 06")));

    // The game never sends a zero in an entry, so an entry with a 0 where its kind says a value is, is bytes that only
    // look like one. Two such look-alikes, modelled on A2Tools' samples (their shapes, not their bytes): the data
    // before the list opens with "0c 01 00 00 00", here backed by a slot-2 look-alike with the real entry (charged
    // only) after it; and an update of the table that data opens, whose id-1 entry reads as kind 0c, slot 1, key 0,
    // values 0 and 0, with or without a leading 00. Under the old rules (only 0c, zeros taken) each read as 0 (+0),
    // what Harry saw; which one the game sent is not known (audit on PR #36).
    [Fact]
    public void ALookAlikeWithZerosIsNeverTheEnergy()
    {
        Assert.Equal(new OdyleEnergy(0, 1835),
            OdyleEnergyRecords.Read(Frame(0x610B, "2A 0C 01 00 00 00 04 02 00 0E 08 01 92 21 AB 0E 04 02 92 21 0E")));
        Assert.Null(OdyleEnergyRecords.Read(Frame(0x610B, "2A 0C 01 00 00 00 04 02 00 0E")));
        Assert.Null(OdyleEnergyRecords.Read(Frame(0x610C, "01 0C 01 00 00 00 07 03")));
        Assert.Null(OdyleEnergyRecords.Read(Frame(0x610C, "00 0C 01 00 00 00 07 03")));
        Assert.Null(OdyleEnergyRecords.Read(Frame(0x610C, "01 0C 01 92 21 1E 00")));
        Assert.Null(OdyleEnergyRecords.Read(Frame(0x610C, "01 0C 01 92 21 00 F0 06")));
        Assert.Null(OdyleEnergyRecords.Read(Frame(0x610C, "01 04 01 92 21 00")));
        Assert.Null(OdyleEnergyRecords.Read(Frame(0x610C, "01 08 01 92 21 00")));
    }

    // The list's energy entry gives its key; with the key known, an update's slot-1 entry under another key is passed
    // over, even before the energy (A2Tools' list holds such an entry, a one-value count in slot 1, whose update would
    // otherwise read as "2 (+0)": audit on PR #36). Without a key, the first slot-1 entry is taken, as A2Tools does.
    [Fact]
    public void AnUpdateTakesTheEnergyByTheListsKey()
    {
        Assert.Equal(new OdyleEnergyReading(Sample, Key), OdyleEnergyRecords.ReadEntry(OdyleEnergyRecordWriter.List(Sample, Key), energyKey: null));
        var otherFirst = Frame(0x610C, "02 04 01 93 21 02 0C 01 92 21 1E F0 06");
        Assert.Equal(new OdyleEnergyReading(Sample, Key), OdyleEnergyRecords.ReadEntry(otherFirst, Key));
        Assert.Equal(new OdyleEnergyReading(new(2, 0), 0x1093), OdyleEnergyRecords.ReadEntry(otherFirst, energyKey: null));
        Assert.Null(OdyleEnergyRecords.ReadEntry(Frame(0x610C, "01 04 01 93 21 02"), Key));
        Assert.Null(OdyleEnergyRecords.ReadEntry(Frame(0x610C, "01 08 01 93 21 02"), Key));
    }

    // The tap keeps the key of the last list: another key's update changes nothing after it, the energy's does.
    // Before any list (a meter started mid-session) an update's first slot-1 entry is taken.
    [Fact]
    public void TheTapReadsUpdatesByTheLastListsKey()
    {
        var stream = new TcpStreamBuffer(NullLogger<TcpStreamBuffer>.Instance);
        using var tap = new OdyleEnergyTap(stream, openBundle: null, NullLogger<OdyleEnergyTap>.Instance);
        var otherKey = Frame(0x610C, "01 04 01 93 21 02");
        tap.Observe(otherKey);
        Assert.Equal(new OdyleEnergy(2, 0), tap.Current);
        tap.Observe(OdyleEnergyRecordWriter.List(Sample, Key));
        Assert.Equal(Sample, tap.Current);
        tap.Observe(otherKey);
        Assert.Equal(Sample, tap.Current);
        tap.Observe(OdyleEnergyRecordWriter.Update(new(20, 880), Key));
        Assert.Equal(new OdyleEnergy(20, 880), tap.Current);

        // Only a list defines the key: an update before any list does not, so it cannot lock out the energy's.
        using var fresh = new OdyleEnergyTap(stream, openBundle: null, NullLogger<OdyleEnergyTap>.Instance);
        fresh.Observe(otherKey);
        fresh.Observe(OdyleEnergyRecordWriter.Update(Sample, Key));
        Assert.Equal(Sample, fresh.Current);
    }

    // The writer leaves a zero out as the game does, and refuses no energy at all, whose form is not documented.
    [Fact]
    public void TheWriterLeavesAZeroOutAsTheGameDoes()
    {
        Assert.Equal(Hex("0D 0C 61 01 04 01 92 21 C8 06"), OdyleEnergyRecordWriter.Update(new(840, 0), Key));
        Assert.Equal(Hex("0D 0C 61 01 08 01 92 21 AB 0E"), OdyleEnergyRecordWriter.Update(new(0, 1835), Key));
        foreach (var energy in new OdyleEnergy[] { new(840, 0), new(0, 1835), Sample })
        {
            Assert.Equal(energy, OdyleEnergyRecords.Read(OdyleEnergyRecordWriter.Update(energy, Key)));
            Assert.Equal(energy, OdyleEnergyRecords.Read(OdyleEnergyRecordWriter.List(energy, Key)));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => OdyleEnergyRecordWriter.Update(new(0, 0), Key));
        Assert.Throws<ArgumentOutOfRangeException>(() => OdyleEnergyRecordWriter.List(new(-1, 5), Key));
    }

    [Fact]
    public void ImplausibleValuesAreTurnedAway()
    {
        Assert.Equal(new OdyleEnergy(OdyleEnergyRecords.MaxValue, 0), OdyleEnergyRecords.Read(OdyleEnergyRecordWriter.Update(new(OdyleEnergyRecords.MaxValue, 0), Key)));
        Assert.Null(OdyleEnergyRecords.Read(OdyleEnergyRecordWriter.Update(new(OdyleEnergyRecords.MaxValue + 1, 0), Key)));
        Assert.Null(OdyleEnergyRecords.Read(OdyleEnergyRecordWriter.Update(new(0, OdyleEnergyRecords.MaxValue + 1), Key)));
    }

    // A five-byte varint whose top byte carries any bit past 32, or goes on to a sixth byte, is refused, so it cannot
    // wrap round to a small value (audit on PR #34: one bit alone was tested). Bits within 32 are read: 30, padded.
    [Theory]
    [InlineData("10")]
    [InlineData("20")]
    [InlineData("40")]
    [InlineData("70")]
    [InlineData("80 00")]
    public void AVarIntPastThirtyTwoBitsIsRefused(string fifth)
    {
        Assert.Null(OdyleEnergyRecords.Read(Frame(0x610C, $"01 0C 01 92 21 9E 80 80 80 {fifth} F0 06")));
        Assert.Equal(Sample, OdyleEnergyRecords.Read(Frame(0x610C, "01 0C 01 92 21 9E 80 80 80 00 F0 06")));
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

    // Nothing the tap meets escapes into upstream's reassembly. An exception thrown back into its accumulator would skip
    // the record's removal, so the record would come again with the next chunk, to upstream's pipeline too, and its hits
    // would count twice. A broken bundle and a failing subscriber here: the chunk's next record is still read.
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
        Assert.Equal(Sample, engine.OdyleEnergy);
    }

    // A subscriber that throws on one record of a bundle does not stop the bundle's later records from being read
    // (audit on PR #34: the outer catch hid the subscriber catch from the stream test above).
    [Fact]
    public void AFailingSubscriberDoesNotStopTheRestOfABundle()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var engine = host.Services.GetRequiredService<ICombatEngine>();
        var stream = host.Services.GetRequiredService<TcpStreamBuffer>();
        engine.OdyleEnergyUpdated += (_, _) => throw new InvalidOperationException("A failing subscriber.");
        Synchronise(stream);
        stream.AddData("synthetic", Bundle([.. OdyleEnergyRecordWriter.Update(new(1, 2), Key), .. OdyleEnergyRecordWriter.Update(Sample, Key)]), 0);
        Assert.Equal(Sample, engine.OdyleEnergy);
    }

    // A reading failure (here a bundle opener that throws) is contained, logged once while it lasts and once more when
    // reading works again, and the chunk's next record is read.
    [Fact]
    public void AReadingFailureIsContainedAndLoggedOnce()
    {
        var stream = new TcpStreamBuffer(NullLogger<TcpStreamBuffer>.Instance);
        var log = new ListLogger();
        using var tap = new OdyleEnergyTap(stream, _ => throw new InvalidOperationException("A failing bundle opener."), log);
        Synchronise(stream);
        var broken = Bundle(OdyleEnergyRecordWriter.Update(new(1, 2), Key));
        stream.AddData("synthetic", [.. broken, .. broken, .. OdyleEnergyRecordWriter.Update(Sample, Key)], 0);
        Assert.Equal(Sample, tap.Current);
        Assert.Single(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("failed", StringComparison.Ordinal));
        Assert.Single(log.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("works again", StringComparison.Ordinal));
    }

    // A change is logged with the record it came from, the list or an update, so a wrong reading can be traced; never
    // the value (the player's progress) nor the bytes. The same value again logs nothing.
    [Fact]
    public void TheTapLogsWhereAChangeCameFromButNotTheValue()
    {
        var stream = new TcpStreamBuffer(NullLogger<TcpStreamBuffer>.Instance);
        var log = new ListLogger();
        using var tap = new OdyleEnergyTap(stream, openBundle: null, log);
        tap.Observe(OdyleEnergyRecordWriter.List(new(777, 1999), Key));
        tap.Observe(OdyleEnergyRecordWriter.Update(new(767, 1999), Key));
        tap.Observe(OdyleEnergyRecordWriter.Update(new(767, 1999), Key));
        Assert.Equal(new OdyleEnergy(767, 1999), tap.Current);
        var changes = log.Entries.Where(e => e.Message.StartsWith("Odyle energy changed", StringComparison.Ordinal)).Select(e => e.Message).ToList();
        Assert.Equal(2, changes.Count);
        Assert.Contains("list of entry counts", changes[0], StringComparison.Ordinal);
        Assert.Contains("an update", changes[1], StringComparison.Ordinal);
        Assert.All(log.Entries, e =>
        {
            foreach (var number in new[] { "777", "767", "1999", "1,999", "92 21", "9221" })
                Assert.DoesNotContain(number, e.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ADisposedTapStopsListening()
    {
        var stream = new TcpStreamBuffer(NullLogger<TcpStreamBuffer>.Instance);
        var tap = new OdyleEnergyTap(stream, openBundle: null, NullLogger<OdyleEnergyTap>.Instance);
        tap.Dispose();
        tap.Dispose(); // twice is fine
        Synchronise(stream);
        stream.AddData("synthetic", OdyleEnergyRecordWriter.Update(Sample, Key), 0);
        Assert.Null(tap.Current);
    }

    // The demo hands the tap a login list when it begins and an update as each later fight starts and spends some.
    // It goes to the tap directly, not through the stream (upstream's reassembly needs a keep-alive from an earlier
    // chunk), and never bundled.
    [Fact]
    public void TheDemoSendsOdyleEnergyToTheTap()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var engine = host.Services.GetRequiredService<ICombatEngine>();
        var feed = new DemoCombatFeed(
            host.Services.GetRequiredService<EntityTracker>(), host.Services.GetRequiredService<CombatSessionManager>(),
            host.Services.GetRequiredService<ICombatHistoryStore>(), NullLogger.Instance, new Random(7),
            host.Services.GetRequiredService<OdyleEnergyTap>());
        var clock = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        feed.Begin(clock);
        Assert.Equal(new OdyleEnergy(420, 1200), engine.OdyleEnergy);

        // Past the first fight (at most 90 seconds) and its 10-second break: the second fight spends 10.
        for (var end = clock.AddSeconds(105); clock < end; clock = clock.AddMilliseconds(100))
            feed.Step(clock, TestContext.Current.CancellationToken);
        Assert.Equal(new OdyleEnergy(410, 1200), engine.OdyleEnergy);
    }

    // The demo's spending: 10 a fight, a recharge of up to 200 from charged once basic runs low, never below 0
    // (audit on PR #34: the recharge was untested).
    [Theory]
    [InlineData(420, 1200, 410, 1200)]
    [InlineData(15, 1200, 205, 1000)] // 5 left: 200 recharged
    [InlineData(15, 50, 55, 0)]       // only 50 to recharge with
    [InlineData(25, 0, 15, 0)]        // nothing to recharge with
    [InlineData(5, 0, 420, 1200)]     // both would be 0: it starts again
    [InlineData(0, 0, 420, 1200)]
    public void TheDemoSpendsAndRecharges(int basic, int charged, int basicAfter, int chargedAfter) =>
        Assert.Equal(new OdyleEnergy(basicAfter, chargedAfter), DemoCombatFeed.AfterFight(new OdyleEnergy(basic, charged)));

    // However long the demo runs, its energy is never 0 and 0, which the writer cannot write: the display keeps
    // moving and a later Begin never throws (audit on PR #36: at 0 and 0 after about 160 fights it froze, and a Stop
    // then Start of the demo threw).
    [Fact]
    public void TheDemoNeverRunsOutOfOdyle()
    {
        var energy = new OdyleEnergy(420, 1200);
        for (var fight = 0; fight < 1000; fight++)
        {
            energy = DemoCombatFeed.AfterFight(energy);
            Assert.False(energy is { Basic: 0, Charged: 0 }, $"0 and 0 after fight {fight + 1}");
            Assert.Equal(energy, OdyleEnergyRecords.Read(OdyleEnergyRecordWriter.List(energy, Key)));
        }
    }

    // The messages a tap logs, for the tests that count them.
    private sealed class ListLogger : ILogger<OdyleEnergyTap>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
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
