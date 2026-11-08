using AionDpsMeter.Services.PacketCapture;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ScarletMeter.Engine;
using ScarletMeter.Engine.Maps;
using ScarletMeter.Engine.Upstream;
using static ScarletMeter.Tests.SyntheticRecords;

namespace ScarletMeter.Tests;

/// <summary>
/// The world map's boss list (<c>01 91</c>, FieldBossMapRecords and FieldBossMapTap), in A2Tools' documented shapes: a boss
/// dead with its respawn, one up with its position and since when, the extra byte some bosses carry, a zero time, the
/// Abyss's weekly bosses days ahead, trailing bytes after the last entry, and what does not read. Every record is made up
/// here (SyntheticRecords.FieldBossMap), modelled on the shapes A2Tools describes, never its captured bytes.
/// </summary>
public sealed class FieldBossMapTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 5, 0, 0, TimeSpan.Zero);
    private static readonly long NowMs = Now.ToUnixTimeMilliseconds();
    private const long Minute = 60_000;

    // The opcode is the two bytes 01 91 after the length, written out here rather than through the constant it pins.
    [Fact]
    public void TheOpcodeIsTheBytes0191()
    {
        Assert.Equal((ushort)0x9101, FieldBossMapRecords.Opcode);
        var record = FieldBossMap(0, 1010, [FieldBossEntry(101021, false, NowMs + 90 * Minute)]);
        Assert.Equal(0x01, record[1]);
        Assert.Equal(0x91, record[2]);
    }

    // A dead boss with its respawn, one up with its position and since when, and one carrying the extra byte, read in order.
    [Fact]
    public void AMapListsEachBossWithWhetherItIsUpAndItsTime()
    {
        var record = FieldBossMap(0, 1010,
        [
            FieldBossEntry(101021, false, NowMs + 90 * Minute),
            FieldBossEntry(101003, true, NowMs - 5 * Minute),
            FieldBossEntry(101007, true, NowMs - 80_000, extraByte: true),
            FieldBossEntry(101002, false, NowMs + 104_000, extraByte: true),
        ]);
        var map = Assert.IsType<FieldBossMapRecord>(FieldBossMapRecords.Read(record, Now));
        Assert.Equal((0, 1010), (map.Header, map.MapId));
        Assert.Equal(
            [
                new FieldBossMapEntry(101021, false, NowMs + 90 * Minute),
                new FieldBossMapEntry(101003, true, NowMs - 5 * Minute),
                new FieldBossMapEntry(101007, true, NowMs - 80_000),
                new FieldBossMapEntry(101002, false, NowMs + 104_000),
            ],
            map.Bosses);
    }

    // The Abyss's scheduled bosses carry 0, or their next spawn up to a week away (A2Tools' fix of 2026-11-07: three days
    // failed the whole map once 2006-2008 were five days out). Past eight days is a misread.
    [Fact]
    public void TheAbyssWeeklyBossesMayBeDaysAwayAndAZeroTimeIsNone()
    {
        var fiveDays = NowMs + 5L * 24 * 60 * Minute;
        var map = FieldBossMapRecords.Read(FieldBossMap(0, 20,
        [
            FieldBossEntry(2004, false, NowMs + 7 * 60 * Minute, extraByte: true),
            FieldBossEntry(2001, false, 0),
            FieldBossEntry(2006, false, fiveDays),
        ]), Now);
        Assert.NotNull(map);
        Assert.Equal([0L, fiveDays], map.Bosses.Skip(1).Select(boss => boss.TimeMs));

        var nineDays = NowMs + 9L * 24 * 60 * Minute;
        Assert.Null(FieldBossMapRecords.Read(FieldBossMap(0, 20, [FieldBossEntry(2006, false, nineDays)]), Now));
        var fifteenDaysAgo = NowMs - 15L * 24 * 60 * Minute;
        Assert.Null(FieldBossMapRecords.Read(FieldBossMap(0, 20, [FieldBossEntry(2006, false, fifteenDaysAgo)]), Now));
        // The bounds themselves are in.
        Assert.NotNull(FieldBossMapRecords.Read(FieldBossMap(0, 20, [FieldBossEntry(2006, false, NowMs + (long)FieldBossMapRecords.MaxAhead.TotalMilliseconds)]), Now));
        Assert.NotNull(FieldBossMapRecords.Read(FieldBossMap(0, 20, [FieldBossEntry(2006, false, NowMs - (long)FieldBossMapRecords.MaxPast.TotalMilliseconds)]), Now));
    }

    // An extra byte of 00 before a zero time reads as a zero time right after the id too; only where the next entry would
    // then start (on the time's last byte, no entry's start) tells the two places apart, as A2Tools reads it.
    [Fact]
    public void WhereTheNextEntryStartsTellsAZeroExtraByteFromTheTime()
    {
        var map = FieldBossMapRecords.Read(FieldBossMap(0, 20,
        [
            FieldBossEntry(2001, false, 0, extraByte: true, extra: 0x00),
            FieldBossEntry(2002, false, NowMs + 60 * Minute),
        ]), Now);
        Assert.NotNull(map);
        Assert.Equal([new FieldBossMapEntry(2001, false, 0), new FieldBossMapEntry(2002, false, NowMs + 60 * Minute)], map.Bosses);
    }

    // After the last entry more may follow (A2Tools reads only that the record holds the last time).
    [Fact]
    public void BytesAfterTheLastEntryAreLeftAlone()
    {
        var map = FieldBossMapRecords.Read(FieldBossMap(0, 1110, [FieldBossEntry(111021, false, NowMs + Minute)], trailing: [0, 0, 0, 0]), Now);
        Assert.Equal(new FieldBossMapEntry(111021, false, NowMs + Minute), Assert.Single(map!.Bosses));
    }

    // Anything that does not read from end to end gives nothing, never part of a list.
    [Fact]
    public void ARecordThatDoesNotReadCleanlyGivesNothing()
    {
        var good = FieldBossMap(0, 1010, [FieldBossEntry(101021, false, NowMs + Minute), FieldBossEntry(101003, false, NowMs + 2 * Minute)]);
        Assert.NotNull(FieldBossMapRecords.Read(good, Now)); // the control

        Assert.Null(FieldBossMapRecords.Read(good[..^3], Now)); // cut short
        Assert.Null(FieldBossMapRecords.Read(FieldBossMap(0, 1010, [], count: 0), Now)); // no bosses
        Assert.Null(FieldBossMapRecords.Read(FieldBossMap(0, 1010, [FieldBossEntry(101021, false, NowMs + Minute)], count: 2), Now)); // fewer than said
        var upByteTwo = FieldBossEntry(101021, false, NowMs + Minute);
        upByteTwo[0] = 2;
        Assert.Null(FieldBossMapRecords.Read(FieldBossMap(0, 1010, [upByteTwo]), Now)); // an up byte that is neither 0 nor 1
        Assert.Null(FieldBossMapRecords.Read(FieldBossMap(0, 0, [FieldBossEntry(101021, false, NowMs + Minute)]), Now)); // map 0
        // A time that is no time at either place, followed by an entry that then starts in the wrong place.
        Assert.Null(FieldBossMapRecords.Read(FieldBossMap(0, 1010, [FieldBossEntry(101021, false, 12345), FieldBossEntry(101003, false, NowMs)]), Now));
        // Another record with the same body.
        var other = good.ToArray();
        other[2] = 0x92;
        Assert.Null(FieldBossMapRecords.Read(other, Now));
    }

    // The tap raises a map with the player's own header only (0): another may be a map seen from a rift, another server's.
    [Fact]
    public void TheTapRaisesOnlyThePlayersOwnMap()
    {
        var tap = new FieldBossMapTap(new FakeTimeProvider(Now), NullLogger<FieldBossMapTap>.Instance);
        var raised = new List<FieldBossMap>();
        tap.Observed += (_, map) => raised.Add(map);

        tap.Observe(FieldBossMap(3, 1010, [FieldBossEntry(101021, false, NowMs + Minute)]));
        Assert.Empty(raised);

        tap.Observe(FieldBossMap(0, 1010, [FieldBossEntry(101021, false, NowMs + Minute), FieldBossEntry(101003, true, 0)]));
        var map = Assert.Single(raised);
        Assert.Equal((1010, Now), (map.MapId, map.SeenAt));
        Assert.Equal([new FieldBossOnMap(101021, false, Now.AddMinutes(1)), new FieldBossOnMap(101003, true, null)], map.Bosses);
    }

    // The game sends the list every few seconds while the map is open (Harry's log, 2026-11-08: a line every three seconds):
    // the same map with the same count is logged once, another map at once, the same again after LogEvery; every reading
    // is still raised.
    [Fact]
    public void TheSameMapReadAgainIsLoggedOnceInAWhile()
    {
        var clock = new FakeTimeProvider(Now);
        var log = new RecordingLogger<FieldBossMapTap>();
        var tap = new FieldBossMapTap(clock, log);
        var raised = 0;
        tap.Observed += (_, _) => raised++;
        int Lines() => log.Entries.Count(entry => entry.Message.StartsWith("Field bosses read from the world map", StringComparison.Ordinal));
        for (var i = 0; i < 3; i++)
        {
            tap.Observe(FieldBossMap(0, 1010, [FieldBossEntry(101021, false, NowMs + 90 * Minute)]));
            clock.Advance(TimeSpan.FromSeconds(3));
        }
        Assert.Equal((3, 1), (raised, Lines()));
        tap.Observe(FieldBossMap(0, 1110, [FieldBossEntry(111021, false, NowMs + 90 * Minute)]));
        Assert.Equal(2, Lines());
        tap.Observe(FieldBossMap(0, 1010, [FieldBossEntry(101021, false, NowMs + 90 * Minute)]));
        Assert.Equal(3, Lines());
        clock.Advance(FieldBossMapTap.LogEvery);
        tap.Observe(FieldBossMap(0, 1010, [FieldBossEntry(101021, false, NowMs + 90 * Minute)]));
        Assert.Equal(4, Lines());
        for (var i = 0; i < 3; i++) tap.Observe(FieldBossMap(0, 1010, [], count: 3));
        Assert.Single(log.Entries, entry => entry.Message.Contains("did not read", StringComparison.Ordinal));
    }

    // A subscriber that throws is logged, and the next map is still raised.
    [Fact]
    public void AFailingSubscriberDoesNotStopTheNextMap()
    {
        var log = new RecordingLogger<FieldBossMapTap>();
        var tap = new FieldBossMapTap(new FakeTimeProvider(Now), log);
        var calls = 0;
        tap.Observed += (_, _) => { calls++; throw new InvalidOperationException("subscriber"); };
        tap.Observe(FieldBossMap(0, 1010, [FieldBossEntry(101021, false, NowMs + Minute)]));
        tap.Observe(FieldBossMap(0, 1010, [FieldBossEntry(101021, false, NowMs + Minute)]));
        Assert.Equal(2, calls);
    }

    // Through upstream's real reassembly, plain and inside an LZ4 bundle: the map reaches ICombatEngine.FieldBossMapObserved.
    [Fact]
    public void TheEngineReadsTheMapFromTheStreamPlainAndInABundle()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var engine = host.Services.GetRequiredService<ICombatEngine>();
        var stream = host.Services.GetRequiredService<TcpStreamBuffer>();
        var raised = new List<FieldBossMap>();
        engine.FieldBossMapObserved += (_, map) => raised.Add(map);
        // A time near the host's real clock, which the tap reads.
        var soon = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeMilliseconds();

        Synchronise(stream);
        stream.AddData("synthetic", FieldBossMap(0, 1010, [FieldBossEntry(101021, false, soon)]), 0);
        stream.AddData("synthetic", Bundle(FieldBossMap(0, 1110, [FieldBossEntry(111021, false, soon)])), 0);

        Assert.Equal([1010, 1110], raised.Select(map => map.MapId));
        Assert.Equal(101021, Assert.Single(raised[0].Bosses).Id);
    }
}
