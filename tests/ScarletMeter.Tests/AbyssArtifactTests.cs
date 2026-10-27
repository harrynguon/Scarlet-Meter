using AionDpsMeter.Services.PacketCapture;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ScarletMeter.Engine;
using ScarletMeter.Engine.Abyss;
using ScarletMeter.Engine.Upstream;
using static ScarletMeter.Tests.SyntheticRecords;

namespace ScarletMeter.Tests;

/// <summary>
/// Who holds each Abyss artifact, as A2Tools reads it (AbyssArtifactRecords; abyss_artifacts.rs at 6837e55): the Abyss
/// map's record (07 e3, both layers) and the one on entering the Abyss (05 e3), read whole or not at all, and the tap
/// that raises them. Every record here is synthetic, written in the shapes A2Tools documents with made-up bytes and
/// times (not its sample's); none was captured.
/// </summary>
public sealed class AbyssArtifactTests
{
    private static readonly DateTimeOffset Since = new(2026, 10, 24, 12, 9, 30, TimeSpan.Zero);
    private static readonly DateTimeOffset Until = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly long SinceMs = Since.ToUnixTimeMilliseconds();
    private static readonly long UntilMs = Until.ToUnixTimeMilliseconds();

    // The upper layer nobody holds (a single 0 for its war), the lower layer two to one.
    private static byte[] Map() => AbyssMap(
        [ArtifactWar(2001, 0, null), ArtifactWar(1001, SinceMs, UntilMs)],
        ArtifactList((2001, 0), (2002, 0), (2003, 0), (1001, 1), (1002, 2), (1003, 1)));

    private static byte[] Entry() => AbyssEntry(ArtifactWar(1001, SinceMs, UntilMs), ArtifactList((1001, 1), (1002, 2), (1003, 1)));

    [Fact]
    public void TheAbyssMapSaysWhoHoldsEachArtifactOnBothLayers()
    {
        var read = AbyssArtifactRecords.Read(Map());
        Assert.NotNull(read);
        Assert.Equal([new ArtifactWarRead(2001, 0, 0), new ArtifactWarRead(1001, SinceMs, UntilMs)], read.Wars);
        Assert.Equal(
            [new ArtifactRead(2001, 0), new ArtifactRead(2002, 0), new ArtifactRead(2003, 0), new ArtifactRead(1001, 1), new ArtifactRead(1002, 2), new ArtifactRead(1003, 1)],
            read.Artifacts);
    }

    [Fact]
    public void EnteringTheAbyssSaysTheSameForTheLayerEntered()
    {
        var read = AbyssArtifactRecords.Read(Entry());
        Assert.NotNull(read);
        Assert.Equal([new ArtifactWarRead(1001, SinceMs, UntilMs)], read.Wars);
        Assert.Equal([new ArtifactRead(1001, 1), new ArtifactRead(1002, 2), new ArtifactRead(1003, 1)], read.Artifacts);
    }

    // A war's reset is there or not: the reading that accounts for every byte is taken, whichever its start suggests.
    [Fact]
    public void AWarIsReadWithOrWithoutItsReset()
    {
        var startedNoReset = AbyssArtifactRecords.Read(AbyssEntry(ArtifactWar(1001, SinceMs, null), ArtifactList((1001, 1))));
        Assert.Equal([new ArtifactWarRead(1001, SinceMs, 0)], startedNoReset?.Wars);
        var resetNoStart = AbyssArtifactRecords.Read(AbyssEntry(ArtifactWar(1001, 0, UntilMs), ArtifactList((1001, 2))));
        Assert.Equal([new ArtifactWarRead(1001, 0, UntilMs)], resetNoStart?.Wars);
        var both = AbyssArtifactRecords.Read(AbyssMap([ArtifactWar(1001, SinceMs, null), ArtifactWar(2001, 0, UntilMs)], ArtifactList((1001, 1))));
        Assert.Equal([new ArtifactWarRead(1001, SinceMs, 0), new ArtifactWarRead(2001, 0, UntilMs)], both?.Wars);
    }

    // Only a record that falls into place is read: anything else in the shape is left alone rather than guessed at.
    [Fact]
    public void ARecordThatDoesNotFallIntoPlaceIsLeftAlone()
    {
        var map = Map();
        Assert.Null(AbyssArtifactRecords.Read(map[..^1])); // a byte short
        Assert.Null(AbyssArtifactRecords.Read([.. Entry(), 0x00])); // a byte over
        Assert.Null(AbyssArtifactRecords.Read(Frame(0x9105, "01 00")));
        Assert.Null(AbyssArtifactRecords.Read(AbyssMap([], ArtifactList((1001, 1))))); // no war
        Assert.Null(AbyssArtifactRecords.Read(AbyssMap([.. Enumerable.Repeat(ArtifactWar(1001, 0, null), AbyssArtifactRecords.MaxWars + 1)], ArtifactList((1001, 1)))));
        Assert.Null(AbyssArtifactRecords.Read(AbyssEntry(ArtifactWar(1001, SinceMs, UntilMs), ArtifactList()))); // no artifact
        Assert.Null(AbyssArtifactRecords.Read(AbyssEntry(ArtifactWar(1001, SinceMs, UntilMs), ArtifactList((0, 1)))));
        Assert.Null(AbyssArtifactRecords.Read(AbyssEntry(ArtifactWar(1001, SinceMs, UntilMs), ArtifactList((1_000_000, 1)))));
        Assert.Null(AbyssArtifactRecords.Read(AbyssEntry(ArtifactWar(0, SinceMs, UntilMs), ArtifactList((1001, 1)))));
        Assert.Null(AbyssArtifactRecords.Read(AbyssEntry(ArtifactWar(1001, 1_000, null), ArtifactList((1001, 1))))); // 1970
        Assert.Null(AbyssArtifactRecords.Read(AbyssEntry(ArtifactWar(1001, SinceMs, 4_102_444_800_000), ArtifactList((1001, 1))))); // 2100
        var notZero = Map();
        notZero[4] = 0x01; // the map's u16 before the count of wars
        Assert.Null(AbyssArtifactRecords.Read(notZero));
        Assert.Null(AbyssArtifactRecords.Read(Frame(AbyssArtifactRecords.MapOpcode, "00 00")));
        Assert.Null(AbyssArtifactRecords.Read([]));
    }

    // The tap raises every reading with when it was read, holders named as A2Tools reads them; it logs only a change, and
    // the record it came from, never who holds what.
    [Fact]
    public void TheTapRaisesWhoHoldsWhatWithWhenItWasRead()
    {
        var clock = new FakeTimeProvider(Since.AddHours(3));
        var log = new RecordingLogger<AbyssArtifactTap>();
        var tap = new AbyssArtifactTap(clock, log);
        List<AbyssArtifacts> raised = [];
        tap.Observed += (_, artifacts) => raised.Add(artifacts);

        tap.Observe(Map());
        var seen = Assert.Single(raised);
        Assert.Equal([new ArtifactWar(2001, null, null), new ArtifactWar(1001, Since, Until)], seen.Wars);
        Assert.Equal(clock.GetUtcNow(), seen.SeenAt);
        Assert.Equal(
            [ArtifactHolder.Nobody, ArtifactHolder.Nobody, ArtifactHolder.Nobody, ArtifactHolder.Elyos, ArtifactHolder.Asmodian, ArtifactHolder.Elyos],
            seen.Artifacts.Select(artifact => artifact.Holder));
        Assert.Equal(ArtifactHolder.Asmodian, seen.HolderOf(1002));
        Assert.Equal(ArtifactHolder.Unknown, seen.HolderOf(3001));
        Assert.Equal(new ArtifactWar(1001, Since, Until), seen.War(AbyssArtifacts.LowerLayer));
        Assert.Null(seen.War(3001));

        tap.Observe(Map());
        tap.Observe(AbyssEntry(ArtifactWar(1001, SinceMs, UntilMs), ArtifactList((1001, 9), (1002, 2), (1003, 1))));
        Assert.Equal(3, raised.Count);
        Assert.Equal(ArtifactHolder.Unknown, raised[^1].HolderOf(1001)); // a value A2Tools has not seen
        Assert.Equal(2, log.Entries.Count(e => e.Message.StartsWith("Abyss artifact holders read", StringComparison.Ordinal)));
        Assert.All(log.Entries, e => Assert.DoesNotContain("Elyos", e.Message, StringComparison.OrdinalIgnoreCase));

        tap.Observed += (_, _) => throw new InvalidOperationException("subscriber");
        tap.Observe(Entry()); // a subscriber's failure is logged, not thrown into the capture thread
        Assert.Single(log.Entries, e => e.Message.Contains("subscriber failed", StringComparison.Ordinal));
    }

    // An artifact listed twice is the last listing's (A2Tools' Roster plugin overwrites each), a war the first's (it finds
    // the first).
    [Fact]
    public void AnArtifactIsItsLastListingsAndAWarItsFirsts()
    {
        var reading = new AbyssArtifacts(
            [new ArtifactWar(1001, Since, null), new ArtifactWar(1001, null, Until)],
            [new AbyssArtifact(1001, ArtifactHolder.Elyos), new AbyssArtifact(1001, ArtifactHolder.Asmodian), new AbyssArtifact(1002, ArtifactHolder.Unknown)],
            Since);
        Assert.Equal(ArtifactHolder.Asmodian, reading.HolderOf(1001));
        Assert.Equal(new ArtifactWar(1001, Since, null), reading.War(1001));
    }

    // A server's side, by its id: 1001 and up Elyos, 2001 and up Asmodian; anything else not known.
    [Theory]
    [InlineData(1001, ArtifactHolder.Elyos)]
    [InlineData(1999, ArtifactHolder.Elyos)]
    [InlineData(2001, ArtifactHolder.Asmodian)]
    [InlineData(2999, ArtifactHolder.Asmodian)]
    [InlineData(0, ArtifactHolder.Unknown)]
    [InlineData(1000, ArtifactHolder.Unknown)]
    [InlineData(2000, ArtifactHolder.Unknown)]
    [InlineData(3001, ArtifactHolder.Unknown)]
    [InlineData(-1001, ArtifactHolder.Unknown)]
    public void AServersSideIsItsThousands(int serverId, ArtifactHolder side) => Assert.Equal(side, AbyssArtifacts.SideOf(serverId));

    // Through the real graph, inside an LZ4 bundle as the game sends most records: the engine raises the reading.
    [Fact]
    public void TheEngineReadsTheArtifactsInsideABundle()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var engine = host.Services.GetRequiredService<ICombatEngine>();
        var stream = host.Services.GetRequiredService<TcpStreamBuffer>();
        List<AbyssArtifacts> raised = [];
        engine.ArtifactsObserved += (_, artifacts) => raised.Add(artifacts);
        Synchronise(stream);
        stream.AddData("synthetic", Bundle([.. Frame(0x3603, "00 00 00 00 00 00 00 00 00"), .. Map()]), 0);
        Assert.Equal(ArtifactHolder.Elyos, Assert.Single(raised).HolderOf(1003));
    }
}
