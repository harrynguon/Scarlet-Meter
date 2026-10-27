using System.Buffers.Binary;
using System.IO;
using System.Text.RegularExpressions;
using AionDpsMeter.Services.PacketCapture;
using AionDpsMeter.Services.PacketProcessing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ScarletMeter.App.Formatting;
using ScarletMeter.Engine;
using ScarletMeter.Engine.Maps;
using ScarletMeter.Engine.Entries;
using ScarletMeter.Engine.Upstream;
using static ScarletMeter.Tests.SyntheticRecords;

namespace ScarletMeter.Tests;

/// <summary>
/// The map probe: the game's map-loaded record as A2Tools documented it (MapLoadRecords; taengu/A2Tools-DPS-Meter
/// b1fcd53, unchanged up to 6abf5e7: <c>&lt;len&gt; 21 36 &lt;u32&gt; &lt;map id u32&gt; …</c>), read beside upstream's
/// pipeline from upstream's real stream reassembly, plain or inside an LZ4 bundle, by the record tap that also feeds
/// Odyle energy (RecordTap, MapLoadTap), and the names Settings shows beside the id (MapNames, MapText). Every byte here
/// is synthetic and the records' map ids are made up; none was captured.
/// </summary>
public sealed partial class MapProbeTests
{
    private const int MapA = 424242; // made up; "32 79 06 00" little-endian
    private const int MapB = 7311;   // made up
    // An update of the entry list carrying Odyle energy alone (EntryListTests has the list's own tests).
    private static byte[] OdyleUpdate(int basic, int charged) => EntryListRecordWriter.Update([new(EntryIds.OdyleEnergy, basic, charged)]);

    // The entry-list reader alone: no character is ever named here.
    private static EntryListTap NewEntryTap() => new(_ => null, TimeProvider.System, NullLogger<EntryListTap>.Instance);

    // The whole record in the shape A2Tools documents, written out: length 0x34 (52: what follows it plus 4, as upstream
    // counts), opcode 21 36, the u32 A2Tools calls a count (1 on entering), the id, then 38 more bytes to the 49 every
    // load in A2Tools' capture had. Those 38 are made up here (not the capture's), and MapLoad writes the same.
    [Fact]
    public void AMapLoadIsReadInTheDocumentedLayout()
    {
        var record = Hex("34 21 36 01 00 00 00 32 79 06 00" + string.Concat(Enumerable.Repeat(" 5A", 38)));
        Assert.Equal(49, record.Length);
        Assert.Equal(MapA, MapLoadRecords.Read(record));
        Assert.Equal(record, MapLoad(MapA));
        // A teleport inside an instance names it again, the u32 before the id then 2 in A2Tools' capture.
        Assert.Equal(MapA, MapLoadRecords.Read(MapLoad(MapA, count: 2)));
    }

    // Only the id is read, so the record's length past it does not matter: none at all, the documented 38 bytes, or
    // enough that the length takes a two-byte varint (the opcode then starts a byte later). A record the game lengthens
    // in a patch still names its map.
    [Theory]
    [InlineData(0)]
    [InlineData(38)]
    [InlineData(200)]
    public void TheIdIsReadWhateverFollowsIt(int restLength)
    {
        var record = MapLoad(MapB, restLength: restLength);
        Assert.Equal(restLength == 200 ? 2 : 1, record.TakeWhile(b => (b & 0x80) != 0).Count() + 1);
        Assert.Equal(MapB, MapLoadRecords.Read(record));
    }

    // A record cut before the id's last byte names no map; from there on the id is read. Nothing throws.
    [Fact]
    public void ACutRecordGivesNoneAndNeverThrows()
    {
        var record = MapLoad(MapA);
        for (var length = 0; length <= record.Length; length++)
            Assert.Equal(length < 11 ? (int?)null : MapA, MapLoadRecords.Read(record.AsSpan(0, length)));
        Assert.Null(MapLoadRecords.Read(Hex("80")));
    }

    // Another record with the same body names no map: the zone change A2Tools reads beside it (23 36), the opcode's
    // neighbours, its bytes swapped, the Odyle list and a bundle.
    [Theory]
    [InlineData(0x3623)]
    [InlineData(0x3622)]
    [InlineData(0x3620)]
    [InlineData(0x3721)]
    [InlineData(0x2136)]
    [InlineData(0x610B)]
    [InlineData(0xFFFF)]
    public void OtherRecordsGiveNone(int opcode)
    {
        var body = Convert.ToHexString(MapLoad(MapA).AsSpan(3));
        Assert.Equal(MapA, MapLoadRecords.Read(Frame(MapLoadRecords.Opcode, body)));
        Assert.Null(MapLoadRecords.Read(Frame((ushort)opcode, body)));
    }

    // The id is a little-endian u32 (A2Tools reads it so, then as an i32). 0, and ids past int's range, which no map id
    // A2Tools documents is, are refused rather than shown; 1 and int.MaxValue are ids.
    [Theory]
    [InlineData("00 00 00 00", null)]
    [InlineData("FF FF FF FF", null)]
    [InlineData("00 00 00 80", null)]
    [InlineData("01 00 00 00", 1)]
    [InlineData("FF FF FF 7F", int.MaxValue)]
    [InlineData("D2 04 00 00", 1234)]
    public void TheIdIsALittleEndianNumberAboveZero(string id, int? expected) =>
        Assert.Equal(expected, MapLoadRecords.Read(Frame(MapLoadRecords.Opcode, "01 00 00 00 " + id)));

    // Each new map is logged once at Information as "Map loaded: <id>", the number alone: never the u32 before it nor
    // the bytes after it (made-up 5a bytes here, where the game's record may hold a position). Loading the same map
    // again (a teleport inside an instance) logs and raises nothing; going back to an earlier map does.
    [Fact]
    public void EachNewMapIsLoggedOnceByItsNumberAlone()
    {
        var log = new RecordingLogger<MapLoadTap>();
        var tap = new MapLoadTap(log);
        var raised = new List<int?>();
        tap.Changed += (_, id) => raised.Add(id);
        Assert.Null(tap.Current);

        tap.Observe(MapLoad(MapA));
        tap.Observe(MapLoad(MapA, count: 2));
        tap.Observe(MapLoad(MapB));
        tap.Observe(MapLoad(MapA));
        Assert.Equal(MapA, tap.Current);
        Assert.Equal([MapA, MapB, MapA], raised);
        Assert.Equal(["Map loaded: 424242", "Map loaded: 7311", "Map loaded: 424242"], log.Entries.Select(e => e.Message));
        Assert.All(log.Entries, e => Assert.Equal(LogLevel.Information, e.Level));
    }

    // A record that names no map (another opcode, an id of 0, a cut one) leaves the last map as it was, unlogged.
    [Fact]
    public void ARecordThatNamesNoMapChangesNothing()
    {
        var log = new RecordingLogger<MapLoadTap>();
        var tap = new MapLoadTap(log);
        tap.Observe(MapLoad(MapA));
        tap.Observe(Frame(0x3623, Convert.ToHexString(MapLoad(MapB).AsSpan(3))));
        tap.Observe(Frame(MapLoadRecords.Opcode, "01 00 00 00 00 00 00 00"));
        tap.Observe(MapLoad(MapB)[..9]);
        Assert.Equal(MapA, tap.Current);
        Assert.Single(log.Entries);
    }

    // Through upstream's real reassembly, synchronised as its accumulator needs: the map reaches ICombatEngine.MapId and
    // MapChanged, the same map again raises nothing, and the id is kept across Reset and Stop.
    [Fact]
    public void TheEngineReadsTheMapFromTheStream()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var engine = host.Services.GetRequiredService<ICombatEngine>();
        var stream = host.Services.GetRequiredService<TcpStreamBuffer>();
        var raised = new List<int?>();
        engine.MapChanged += (_, id) => raised.Add(id);
        Assert.Null(engine.MapId);

        Synchronise(stream);
        stream.AddData("synthetic", MapLoad(MapA), 0);
        stream.AddData("synthetic", [.. MapLoad(MapA, count: 2), .. MapLoad(MapB)], 0);
        Assert.Equal(MapB, engine.MapId);
        Assert.Equal([MapA, MapB], raised);

        engine.Reset();
        engine.Stop();
        Assert.Equal(MapB, engine.MapId);
    }

    // Why the map is forgotten on a new connection: upstream reads a new connection only from its first keep-alive on,
    // so a map record that opens it is dropped before the tap could see it, and the old map would stay (what Harry saw
    // leaving the Abyss, 2026-10-26). Pins upstream's behaviour: should a release read new connections from their
    // start, this fails, and the record is read; then revisit ARCHITECTURE's "Records beside the pipeline".
    [Fact]
    public void UpstreamDropsTheMapRecordThatOpensANewConnection()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var engine = host.Services.GetRequiredService<ICombatEngine>();
        var stream = host.Services.GetRequiredService<TcpStreamBuffer>();
        Synchronise(stream, "Client:50001");
        stream.AddData("Client:50001", MapLoad(MapA), 0);
        Assert.Equal(MapA, engine.MapId);

        stream.AddData("Client:50002", MapLoad(MapB), 0);
        Assert.Equal(MapA, engine.MapId);
    }

    // The game moves to a new connection (a server change): at the first record the tap reads of it the map is
    // forgotten, raised as null and logged, until a load is read on it. Odyle energy, the character's, is kept. A
    // trailing record of the old connection is no new move, so the new map stays.
    [Fact]
    public void TheEngineForgetsTheMapWhenTheGameChangesConnection()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var engine = host.Services.GetRequiredService<ICombatEngine>();
        var stream = host.Services.GetRequiredService<TcpStreamBuffer>();
        var raised = new List<int?>();
        engine.MapChanged += (_, id) => raised.Add(id);
        var other = Frame(0x3603, "00 00 00 00 00 00 00 00 00");

        Synchronise(stream, "Client:50001");
        stream.AddData("Client:50001", [.. MapLoad(MapA), .. OdyleUpdate(30, 880)], 0);
        Assert.Equal(MapA, engine.MapId);

        Synchronise(stream, "Client:50002");
        stream.AddData("Client:50002", other, 0);
        Assert.Null(engine.MapId);
        Assert.Equal(new OdyleEnergy(30, 880), engine.OdyleEnergy);

        stream.AddData("Client:50002", MapLoad(MapB), 0);
        stream.AddData("Client:50001", other, 0);
        Assert.Equal(MapB, engine.MapId);
        Assert.Equal([MapA, null, MapB], raised);
    }

    // The map reader forgets the map at a new connection once, logging it in words (no number, nothing of the
    // connection); with no map known there is nothing to forget, raise or log.
    [Fact]
    public void ANewConnectionForgetsTheMapOnce()
    {
        var log = new RecordingLogger<MapLoadTap>();
        var tap = new MapLoadTap(log);
        var raised = new List<int?>();
        tap.Changed += (_, id) => raised.Add(id);
        tap.ConnectionStarted();
        Assert.Empty(raised);

        tap.Observe(MapLoad(MapA));
        tap.ConnectionStarted();
        tap.ConnectionStarted();
        Assert.Null(tap.Current);
        Assert.Equal([MapA, null], raised);
        Assert.Equal(["Map loaded: 424242", "Map unknown: the game moved to a new connection; the next map load read names it."],
            log.Entries.Select(e => e.Message));
    }

    // A connection counts as new the first time the tap reads a record of it; the last 16 are remembered, so the list
    // stays small over a long session, and one that falls out of it counts as new again (forgetting the map, the safe
    // side).
    [Fact]
    public void ConnectionsAreRememberedUpToALimit()
    {
        var stream = new TcpStreamBuffer(NullLogger<TcpStreamBuffer>.Instance);
        using var tap = new RecordTap(stream, openBundle: null, [], NullLogger<RecordTap>.Instance);
        Assert.True(tap.IsNewConnection("Client:1"));
        Assert.False(tap.IsNewConnection("Client:1"));
        for (var port = 2; port <= RecordTap.ConnectionsRemembered + 1; port++)
            Assert.True(tap.IsNewConnection($"Client:{port}"));
        Assert.Equal(RecordTap.ConnectionsRemembered, tap.RememberedConnections);
        Assert.False(tap.IsNewConnection($"Client:{RecordTap.ConnectionsRemembered + 1}"));
        Assert.True(tap.IsNewConnection("Client:1"));
        Assert.Equal(RecordTap.ConnectionsRemembered, tap.RememberedConnections);
    }

    // A reader that throws at a new connection does not keep the move from the other readers, nor the record from
    // being read; the failure is logged once.
    [Fact]
    public void AReaderFailingAtANewConnectionIsContained()
    {
        var stream = new TcpStreamBuffer(NullLogger<TcpStreamBuffer>.Instance);
        var log = new RecordingLogger<RecordTap>();
        var maps = new MapLoadTap(NullLogger<MapLoadTap>.Instance);
        using var tap = new RecordTap(stream, openBundle: null, [new ThrowingReader(0x0001, throwsOnNewConnection: true), maps], log);
        Synchronise(stream, "Client:50001");
        stream.AddData("Client:50001", MapLoad(MapA), 0);
        Assert.Equal(MapA, maps.Current);

        Synchronise(stream, "Client:50002");
        stream.AddData("Client:50002", Frame(0x3603, "00 00 00 00 00 00 00 00 00"), 0);
        Assert.Null(maps.Current);
        stream.AddData("Client:50002", MapLoad(MapB), 0);
        Assert.Equal(MapB, maps.Current);
        Assert.Single(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("failed", StringComparison.Ordinal));
    }

    // Records in an LZ4 bundle (ff ff), as most of the game's come: the map and Odyle energy in one bundle are both read.
    [Fact]
    public void TheEngineReadsTheMapInsideABundle()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var engine = host.Services.GetRequiredService<ICombatEngine>();
        var stream = host.Services.GetRequiredService<TcpStreamBuffer>();
        Synchronise(stream);
        stream.AddData("synthetic", Bundle([.. Frame(0x3603, "00 00 00 00 00 00 00 00 00"), .. MapLoad(MapA), .. OdyleUpdate(30, 880)]), 0);
        Assert.Equal(MapA, engine.MapId);
        Assert.Equal(new OdyleEnergy(30, 880), engine.OdyleEnergy);
    }

    // The point of one tap: each bundle is decompressed once beside upstream's pipeline however many readers there
    // are, not once per reader (each opening is a full LZ4 decompression on the capture thread, in the crowded fights
    // the map probe is for).
    [Fact]
    public void OneOpeningOfABundleServesEveryReader()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var open = RecordTap.BindBundleOpener(host.Services.GetRequiredService<PacketProcessor>());
        Assert.NotNull(open);
        var openings = 0;
        var stream = new TcpStreamBuffer(NullLogger<TcpStreamBuffer>.Instance);
        var odyle = NewEntryTap();
        var maps = new MapLoadTap(NullLogger<MapLoadTap>.Instance);
        using var tap = new RecordTap(stream, record => { openings++; return open(record); }, [odyle, maps], NullLogger<RecordTap>.Instance);
        Synchronise(stream);
        stream.AddData("synthetic", Bundle([.. OdyleUpdate(30, 880), .. MapLoad(MapA)]), 0);
        Assert.Equal(1, openings);
        Assert.Equal(MapA, maps.Current);
        Assert.Equal(new OdyleEnergy(30, 880), odyle.Odyle);
    }

    // The engine's graph has the one tap and its three readers, the same instances the engine reads from.
    [Fact]
    public void TheEngineHasOneTapWithItsReaders()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var readers = host.Services.GetServices<IRecordReader>().ToList();
        Assert.Equal(3, readers.Count);
        Assert.Same(host.Services.GetRequiredService<EntryListTap>(), Assert.Single(readers.OfType<EntryListTap>()));
        Assert.Same(host.Services.GetRequiredService<MapLoadTap>(), Assert.Single(readers.OfType<MapLoadTap>()));
        Assert.Same(host.Services.GetRequiredService<AbyssArtifactTap>(), Assert.Single(readers.OfType<AbyssArtifactTap>()));
        Assert.Same(host.Services.GetRequiredService<RecordTap>(), host.Services.GetRequiredService<RecordTap>());
    }

    // Only the record tap listens to upstream's reassembly in the engine: a reader that subscribed on its own would open
    // every bundle again. Read from the source with comments dropped, so a mention in a comment does not count.
    [Fact]
    public void OnlyTheRecordTapListensToTheStream()
    {
        var engine = Path.Combine(RepositoryRoot(), "src", "ScarletMeter.Engine");
        var listeners = Directory.EnumerateFiles(engine, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => Regex.IsMatch(WithoutComments(File.ReadAllText(f)), @"PacketExtracted\s*\+="))
            .Select(f => Path.GetRelativePath(engine, f))
            .ToList();
        Assert.Equal([Path.Combine("Upstream", "RecordTap.cs")], listeners);
    }

    // A reader that throws does not keep a record from the other readers, nor the rest of a bundle from any. The failure
    // is logged once while it lasts, once when a record is read without one (not again for the records after), and
    // again if it comes back.
    [Fact]
    public void AFailingReaderDoesNotKeepARecordFromTheOthers()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var open = RecordTap.BindBundleOpener(host.Services.GetRequiredService<PacketProcessor>());
        var stream = new TcpStreamBuffer(NullLogger<TcpStreamBuffer>.Instance);
        var log = new RecordingLogger<RecordTap>();
        var odyle = NewEntryTap();
        var maps = new MapLoadTap(NullLogger<MapLoadTap>.Instance);
        using var tap = new RecordTap(stream, open, [new ThrowingReader(MapLoadRecords.Opcode), maps, odyle], log);
        Synchronise(stream);

        stream.AddData("synthetic", [.. MapLoad(MapA), .. Bundle([.. MapLoad(MapB), .. OdyleUpdate(30, 880)])], 0);
        Assert.Equal(MapB, maps.Current);
        Assert.Equal(new OdyleEnergy(30, 880), odyle.Odyle);
        stream.AddData("synthetic", OdyleUpdate(20, 880), 0);
        stream.AddData("synthetic", OdyleUpdate(10, 880), 0);
        Assert.Equal(new OdyleEnergy(10, 880), odyle.Odyle);
        Assert.Single(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("failed", StringComparison.Ordinal));
        Assert.Single(log.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("works again", StringComparison.Ordinal));

        stream.AddData("synthetic", MapLoad(MapA), 0);
        Assert.Equal(MapA, maps.Current);
        Assert.Equal(2, log.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("failed", StringComparison.Ordinal)));
    }

    // A map subscriber that throws is logged as such at each change, and the rest of the bundle is still read; the tap
    // itself counts no failure.
    [Fact]
    public void AFailingMapSubscriberDoesNotStopTheRestOfABundle()
    {
        var warnings = new CapturingLoggerProvider();
        using var host = new TestEngineHost(CaptureMode.None, services => services.AddLogging(logging => logging.AddProvider(warnings)));
        var engine = host.Services.GetRequiredService<ICombatEngine>();
        var stream = host.Services.GetRequiredService<TcpStreamBuffer>();
        engine.MapChanged += (_, _) => throw new InvalidOperationException("A failing subscriber.");
        Synchronise(stream);
        stream.AddData("synthetic", Bundle([.. MapLoad(MapA), .. OdyleUpdate(30, 880)]), 0);
        stream.AddData("synthetic", Bundle([.. MapLoad(MapB), .. OdyleUpdate(20, 880)]), 0);
        Assert.Equal(MapB, engine.MapId);
        Assert.Equal(new OdyleEnergy(20, 880), engine.OdyleEnergy);
        Assert.Equal(2, warnings.Warnings.Count(w => w.StartsWith("A map subscriber failed.", StringComparison.Ordinal)));
        Assert.DoesNotContain(warnings.Warnings, w => w.StartsWith("Reading the game's records", StringComparison.Ordinal));
    }

    // The names as their sources give them: the open world and the Abyss (SkeeveAN's names for AetherDPS's ids) and
    // A2Tools' dungeons, with the difficulty in brackets where A2Tools gives one and its lower-case "the" capitalised;
    // and the Artifact Corridor, the id seen in Harry's game, the name Harry's.
    [Theory]
    [InlineData(503001, "Artifact Corridor")]
    [InlineData(1010, "Verteron")]
    [InlineData(1011, "Eltnen")]
    [InlineData(1110, "Altgard")]
    [InlineData(1111, "Morheim")]
    [InlineData(20, "Chaotic Lower Reshanta")]
    [InlineData(22, "Chaotic Middle Reshanta")]
    [InlineData(600021, "Fire Temple")]
    [InlineData(600001, "Krao Cave (Normal)")]
    [InlineData(600002, "Krao Cave (Exploration)")]
    [InlineData(600004, "Krao Cave (Hard)")]
    [InlineData(600131, "The Hall of Illusion (Exploration)")]
    [InlineData(610044, "Dying Dramata's Nest (Hard)")]
    public void KnownMapsHaveTheirNames(int mapId, string name) => Assert.Equal(name, MapNames.Of(mapId));

    // No source names these, so none is made up: 21 (on AetherDPS's Abyss list, but with no name), the open world's
    // other maps and layers, a seal, ids next to the named ones, and impossible ids.
    [Theory]
    [InlineData(21)]
    [InlineData(1)]
    [InlineData(101021)]
    [InlineData(310051)]
    [InlineData(600000)]
    [InlineData(600005)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void OtherMapsHaveNoName(int mapId) => Assert.Null(MapNames.Of(mapId));

    // The whole table: the six open-world and Abyss maps, the Artifact Corridor seen in the game, A2Tools' 60 dungeons
    // (all instance ids, 600000 to 699999), and every name tidy (no blank, no stray spaces), so a dropped or mistyped
    // line of the import shows.
    [Fact]
    public void TheNameTableHoldsTheSourcesWhole()
    {
        var ids = MapNames.Ids.ToList();
        Assert.Equal([20, 22, 1010, 1011, 1110, 1111, 503001], ids.Where(id => id < 600_000).Order());
        Assert.Equal(60, ids.Count(id => id is >= 600_000 and < 700_000));
        Assert.Equal(67, ids.Count);
        Assert.All(ids, id =>
        {
            var name = MapNames.Of(id);
            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.Equal(name!.Trim(), name);
            Assert.DoesNotContain("  ", name, StringComparison.Ordinal);
            Assert.True(char.IsUpper(name[0]), name);
        });
    }

    // The About line: the name and the id, the id alone when no name is known (so it can still be noted), and a word
    // for none yet. The id has no separators, as in the log line it is matched with.
    [Theory]
    [InlineData(600021, "Fire Temple (map 600021)")]
    [InlineData(20, "Chaotic Lower Reshanta (map 20)")]
    [InlineData(1234567, "Map 1234567 (no name known)")]
    [InlineData(null, "Not known: shown at the next map load the meter reads (one right after changing server can be missed).")]
    public void TheMapLineGivesTheNameAndTheId(int? mapId, string line) => Assert.Equal(line, MapText.Status(mapId));

    // A reader that takes one opcode and throws on every record of it, and also on a new connection when asked to.
    private sealed class ThrowingReader(ushort taken, bool throwsOnNewConnection = false) : IRecordReader
    {
        public bool Reads(ushort opcode) => opcode == taken;

        public void Read(byte[] record, ushort opcode) => throw new InvalidOperationException("A failing reader.");

        public void ConnectionStarted()
        {
            if (throwsOnNewConnection) throw new InvalidOperationException("A failing reader at a new connection.");
        }
    }

    // Source text without its comments, so a commented-out line neither counts nor hides one. "://" in a URL is kept.
    private static string WithoutComments(string source) =>
        LineComment().Replace(BlockComment().Replace(source, string.Empty), string.Empty);

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockComment();

    [GeneratedRegex(@"(?m)(?<!:)//.*$")]
    private static partial Regex LineComment();

    // The directory holding ScarletMeter.slnx, above the test assembly.
    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ScarletMeter.slnx"))) return dir.FullName;
        throw new InvalidOperationException("ScarletMeter.slnx was not found above " + AppContext.BaseDirectory);
    }
}
