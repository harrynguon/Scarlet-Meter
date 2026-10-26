using AionDpsMeter.Core.Data;
using AionDpsMeter.Services.PacketCapture;
using AionDpsMeter.Services.PacketProcessing;
using AionDpsMeter.Services.PacketProcessing.Routing;
using AionDpsMeter.Services.Services.Entity;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Session.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ScarletMeter.Engine;
using ScarletMeter.Engine.Entries;
using ScarletMeter.Engine.Upstream;
using ScarletMeter.Engine.Upstream.Demo;
using static ScarletMeter.Tests.SyntheticRecords;

namespace ScarletMeter.Tests;

/// <summary>
/// The game's list of entry counts (Odyle energy, Nightmare, Ascension Trial and the rest) as A2Tools reads it
/// (EntryListRecords; b79969a, 2149936 and caa7ed4: an entry is a kind, a u32 id and its values), whose counts they are
/// (EntryListTap: the record naming the player's character, which upstream reads), read from upstream's real stream
/// reassembly beside its pipeline, plain or inside an LZ4 bundle, and the demo's records. Every byte here is synthetic,
/// written by hand in the shapes A2Tools documents (not its samples' bytes) or by EntryListRecordWriter; none was captured.
/// </summary>
public sealed class EntryListTests
{
    // Odyle energy's id, 60000001, as its four little-endian bytes: what the first port read as "slot 01, key 87 93 03".
    private const string OdyleId = "01 87 93 03";
    private static readonly EntryValue Odyle = new(EntryIds.OdyleEnergy, 30, 880);
    private static readonly CharacterIdentity Alpha = new("Alpha", "SIE", 16, "Spiritmaster", 41_000);
    private static readonly CharacterIdentity Beta = new("Beta", "SIE", 11, "Gladiator", 38_000);
    private static readonly DateTimeOffset Start = new(2026, 10, 27, 1, 0, 0, TimeSpan.Zero);

    // ---------- Reading ----------

    // An update in the layout A2Tools documents: a count, the Odyle entry (kind 0c, the id, basic 30, charged 880 as
    // "f0 06") and a five-byte tail ("01 <u32>", made up here).
    [Fact]
    public void AnUpdateIsReadInTheDocumentedLayout()
    {
        var read = EntryListRecords.Read(Frame(0x610C, $"01 0C {OdyleId} 1E F0 06 01 2A 00 00 00"));
        Assert.NotNull(read);
        Assert.False(read.IsWholeList);
        Assert.Equal([Odyle], read.Counts);
        Assert.Equal(new OdyleEnergy(30, 880), EntryListRecords.OdyleIn(read.Counts));
    }

    // An update whose first byte is 00 carries an entry all the same (A2Tools, 29e7193), with a one-byte tail.
    [Fact]
    public void AnUpdateThatStartsWithZeroCarriesOneEntry()
    {
        var read = EntryListRecords.Read(Frame(0x610C, "00 0C 01 00 00 00 0B 01 03"));
        Assert.Equal([new EntryValue(1, 11, 1)], read!.Counts);
    }

    // Each kind says which values follow, a zero being left out.
    [Theory]
    [InlineData("00", "", 0, 0)]
    [InlineData("04", "0E", 14, 0)]
    [InlineData("08", "02", 0, 2)]
    [InlineData("0C", "0E 02", 14, 2)]
    public void EachKindIsRead(string kind, string values, int first, int second)
    {
        var read = EntryListRecords.Read(Frame(0x610C, $"01 {kind} 04 00 00 00 {values} 03"));
        Assert.Equal([new EntryValue(EntryIds.Nightmare, first, second)], read!.Counts);
    }

    // A kind 01 entry holds a u64 time (an Abyss passage's), not a count: read past, not kept.
    [Fact]
    public void TimeEntriesAreReadPastAndNotKept()
    {
        var update = EntryListRecords.Read(Frame(0x610C, "00 01 82 96 98 00 11 22 33 00 00 00 00 00 03"));
        Assert.NotNull(update);
        Assert.Empty(update.Counts);

        var list = EntryListRecords.Read(EntryListRecordWriter.List([Odyle, new(EntryIds.Nightmare, 9, 0)]));
        Assert.Equal([Odyle, new EntryValue(EntryIds.Nightmare, 9, 0)], list!.Counts);
    }

    // An update must read whole, with no more than a short tail, or none of it is taken: a part read could be another
    // table's bytes.
    [Theory]
    [InlineData("01 02 04 00 00 00 0E 03")]                // a kind that is not one
    [InlineData("02 04 04 00 00 00 0E 03")]                // two entries counted, one there ("03" is no entry)
    [InlineData("01 04 04 00 00 00 0E 01 02 03 04 05 06")] // six bytes after the entries
    [InlineData("01 04 00 00 00 00 0E 03")]                // id 0
    [InlineData("01 04 00 E1 F5 05 0E 03")]                // id 100,000,000
    [InlineData("01 04 04 00 00 00 C1 84 3D 03")]          // a count of 1,000,001
    [InlineData("01 01 82 96 98 00 D0 FB 01")]             // a time cut short
    [InlineData("01 04 04 00 00")]                         // an id cut short
    [InlineData("")]                                       // nothing at all
    public void AnUpdateThatDoesNotReadWholeIsNotTaken(string body) => Assert.Null(EntryListRecords.Read(Frame(0x610C, body)));

    [Fact]
    public void TheBoundsAreInclusive()
    {
        var read = EntryListRecords.Read(Frame(0x610C, "01 04 FF E0 F5 05 C0 84 3D 03"));
        Assert.Equal([new EntryValue(EntryListRecords.MaxId, EntryListRecords.MaxValue, 0)], read!.Counts);
    }

    // A varint that does not fit 32 bits is refused rather than wrapped into a small count.
    [Theory]
    [InlineData("10")]
    [InlineData("20")]
    [InlineData("40")]
    public void AVarIntPastThirtyTwoBitsIsRefused(string fifth)
    {
        Assert.Null(EntryListRecords.Read(Frame(0x610C, $"01 04 04 00 00 00 9E 80 80 80 {fifth} 03")));
        Assert.Equal([new EntryValue(EntryIds.Nightmare, 30, 0)], EntryListRecords.Read(Frame(0x610C, "01 04 04 00 00 00 9E 80 80 80 00 03"))!.Counts);
    }

    // The whole list in A2Tools' shape: a count, then entries of every kind, the Odyle entry among them, running to the
    // record's end. Values and the time made up.
    [Fact]
    public void AListInTheGamesShapeIsRead()
    {
        var read = EntryListRecords.Read(Frame(0x610B,
            $"05 0C 01 00 00 00 05 07 01 03 00 00 00 44 33 22 11 00 00 00 00 00 04 00 00 00 04 07 00 00 00 0C 0C {OdyleId} F0 01 C0 07"));
        Assert.True(read!.IsWholeList);
        Assert.Equal([new EntryValue(1, 5, 7), new EntryValue(EntryIds.Nightmare, 0, 0), new EntryValue(7, 12, 0), new EntryValue(EntryIds.OdyleEnergy, 240, 960)], read.Counts);
    }

    // Other data comes before the list, so it is found where its count of entries ends at the record's end; the writer's
    // data holds a byte that reads as a count of 42, which a reader taking the first plausible count would follow.
    [Fact]
    public void TheListIsFoundWhereItEndsAtTheRecordsEnd()
    {
        EntryValue[] entries = [Odyle, new(EntryIds.Nightmare, 11, 1), new(EntryIds.AscensionTrial, 3, 0), new(1, 0, 0)];
        var read = EntryListRecords.Read(EntryListRecordWriter.List(entries));
        Assert.True(read!.IsWholeList);
        Assert.Equal(entries, read.Counts);
    }

    // A list that does not read whole still gives the Odyle energy, found by its id (A2Tools' odyle_energy_in_list).
    [Fact]
    public void AListThatDoesNotReadWholeGivesTheOdyleEnergyByItsId()
    {
        var read = EntryListRecords.Read(Frame(0x610B, $"13 37 0C {OdyleId} 1E F0 06 99"));
        Assert.True(read!.IsWholeList);
        Assert.Equal([Odyle], read.Counts);
    }

    // A count read further back can read entries that end before the record does: that is not the list. Here a count of 1
    // reads an Odyle entry and stops short; the list after it runs to the end.
    [Fact]
    public void ACountThatEndsBeforeTheRecordsEndIsNotTheList()
    {
        var read = EntryListRecords.Read(Frame(0x610B, $"01 0C {OdyleId} 1E F0 06 02 04 04 00 00 00 0E 0C {OdyleId} 28 F0 06"));
        Assert.Equal([new EntryValue(EntryIds.Nightmare, 14, 0), new EntryValue(EntryIds.OdyleEnergy, 40, 880)], read!.Counts);
    }

    // A list without the Odyle entry gives no counts (A2Tools takes a list only with it), but is still a login: the
    // record says a character logged in, so the last one's counts must go (2149936).
    [Fact]
    public void AListWithoutTheOdyleEnergyGivesNoCountsButIsALogin()
    {
        var read = EntryListRecords.Read(Frame(0x610B, "02 04 04 00 00 00 0E 04 07 00 00 00 03"));
        Assert.True(read!.IsWholeList);
        Assert.Empty(read.Counts);
    }

    [Fact]
    public void OdyleEnergyPastItsBoundIsNotTheEnergy()
    {
        Assert.Equal(new OdyleEnergy(EntryListRecords.MaxOdyleValue, 0), EntryListRecords.OdyleIn([new(EntryIds.OdyleEnergy, EntryListRecords.MaxOdyleValue, 0)]));
        Assert.Null(EntryListRecords.OdyleIn([new(EntryIds.OdyleEnergy, EntryListRecords.MaxOdyleValue + 1, 0)]));
        Assert.Null(EntryListRecords.OdyleIn([new(EntryIds.OdyleEnergy, 0, EntryListRecords.MaxOdyleValue + 1)]));
        Assert.Null(EntryListRecords.OdyleIn([new(EntryIds.Nightmare, 1, 1)]));
    }

    [Fact]
    public void OtherRecordsAndCutRecordsGiveNoneAndNeverThrow()
    {
        Assert.Null(EntryListRecords.Read(Hex("0E 03 36 01 0C 01 87 93 03 1E F0 06"))); // another opcode
        Assert.Null(EntryListRecords.Read([]));
        Assert.Null(EntryListRecords.Read(Hex("80")));
        var records = new[]
        {
            EntryListRecordWriter.List([Odyle, new(EntryIds.Nightmare, 11, 1)]),
            EntryListRecordWriter.Update([Odyle, new(EntryIds.Nightmare, 11, 1)]),
        };
        foreach (var record in records)
            for (var length = 0; length <= record.Length; length++)
                _ = EntryListRecords.Read(record.AsSpan(0, length));
    }

    // The writer writes the documented shapes, a zero left out as the game does, and its records read back.
    [Fact]
    public void TheWriterWritesTheGamesShapes()
    {
        Assert.Equal(Frame(0x610C, $"01 0C {OdyleId} 1E F0 06 03"), EntryListRecordWriter.Update([Odyle]));
        Assert.Equal(Frame(0x610C, "00 0C 01 00 00 00 0B 01 03"), EntryListRecordWriter.Update([new(1, 11, 1)], zeroCount: true));
        Assert.Equal(Frame(0x610C, "01 04 04 00 00 00 0E 03"), EntryListRecordWriter.Update([new(EntryIds.Nightmare, 14, 0)]));
        Assert.Equal(Frame(0x610C, "01 08 04 00 00 00 02 03"), EntryListRecordWriter.Update([new(EntryIds.Nightmare, 0, 2)]));
        Assert.Equal(Frame(0x610C, "01 00 04 00 00 00 03"), EntryListRecordWriter.Update([new(EntryIds.Nightmare, 0, 0)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => EntryListRecordWriter.Update([new(EntryIds.Nightmare, -1, 0)]));
        Assert.Throws<ArgumentException>(() => EntryListRecordWriter.Update([Odyle, Odyle], zeroCount: true));
        Assert.Equal(PacketOpcodes.PlayerInfo, EntryListRecordWriter.SelfRecordOpcode);
        Assert.Equal(PacketOpcodes.PlayerInfo, EntryListTap.SelfRecordOpcode);
    }

    // ---------- Whose the counts are ----------

    private static EntryListTap NewTap(Dictionary<int, CharacterIdentity> who, TimeProvider clock, ILogger<EntryListTap>? log = null) =>
        new(id => who.GetValueOrDefault(id), clock, log ?? NullLogger<EntryListTap>.Instance);

    // The player's own character record as the tap reads it: only the entity id counts; upstream names the character.
    private static byte[] SelfRecord(int entityId) => EntryListRecordWriter.SelfRecord(entityId, "Unread", 0, 0, 0);

    // A login's list comes before the record naming who logged in: its counts wait for that record, then go to them.
    [Fact]
    public void ALoginListWaitsForTheRecordNamingTheCharacter()
    {
        var clock = new FakeTimeProvider(Start);
        var tap = NewTap(new() { [7] = Alpha }, clock);
        List<CharacterProgress> raised = [];
        tap.ProgressObserved += (_, progress) => raised.Add(progress);

        tap.Observe(EntryListRecordWriter.List([Odyle, new(EntryIds.Nightmare, 11, 1)]));
        Assert.Empty(raised);
        Assert.Equal(new OdyleEnergy(30, 880), tap.Odyle); // the overlay shows it at once
        Assert.Null(tap.Character);

        clock.Advance(TimeSpan.FromSeconds(3));
        tap.Observe(SelfRecord(7));
        var progress = Assert.Single(raised);
        Assert.Same(Alpha, progress.Character);
        Assert.Same(Alpha, tap.Character);
        Assert.Equal(new EntryCount(30, 880, Start), progress.Entries[EntryIds.OdyleEnergy]);
        Assert.Equal(new EntryCount(11, 1, Start), progress.Entries[EntryIds.Nightmare]);
        Assert.Equal(new OdyleEnergy(30, 880), progress.Odyle);
    }

    // Once the character is named, each update is theirs at once, with every count since the list and when each was read.
    [Fact]
    public void UpdatesAfterThatAreTheCharactersAndKeepWhenEachWasRead()
    {
        var clock = new FakeTimeProvider(Start);
        var tap = NewTap(new() { [7] = Alpha }, clock);
        tap.Observe(EntryListRecordWriter.List([Odyle, new(EntryIds.Nightmare, 11, 1)]));
        tap.Observe(SelfRecord(7));
        List<CharacterProgress> raised = [];
        tap.ProgressObserved += (_, progress) => raised.Add(progress);

        clock.Advance(TimeSpan.FromMinutes(20));
        tap.Observe(EntryListRecordWriter.Update([new(EntryIds.Nightmare, 10, 1)]));
        var progress = Assert.Single(raised);
        Assert.Same(Alpha, progress.Character);
        Assert.Equal(new EntryCount(10, 1, Start.AddMinutes(20)), progress.Entries[EntryIds.Nightmare]);
        Assert.Equal(new EntryCount(30, 880, Start), progress.Entries[EntryIds.OdyleEnergy]);
    }

    // A switch: the new login's list waits (the character named so far is the one before), then the record names the
    // new character, who gets only the new list's counts.
    [Fact]
    public void ASwitchGivesTheNewListToTheNewCharacter()
    {
        var clock = new FakeTimeProvider(Start);
        var tap = NewTap(new() { [7] = Alpha, [9] = Beta }, clock);
        tap.Observe(EntryListRecordWriter.List([Odyle, new(EntryIds.Nightmare, 11, 1)]));
        tap.Observe(SelfRecord(7));
        List<CharacterProgress> raised = [];
        tap.ProgressObserved += (_, progress) => raised.Add(progress);

        tap.Observe(EntryListRecordWriter.List([new(EntryIds.OdyleEnergy, 500, 0), new(EntryIds.AscensionTrial, 2, 0)]));
        tap.Observe(EntryListRecordWriter.Update([new(EntryIds.AscensionTrial, 1, 0)])); // before the record: still waits
        Assert.Empty(raised);
        Assert.Same(Alpha, tap.Character);

        tap.Observe(SelfRecord(9));
        var progress = Assert.Single(raised);
        Assert.Same(Beta, progress.Character);
        Assert.Equal([EntryIds.OdyleEnergy, EntryIds.AscensionTrial], progress.Entries.Keys.Order());
        Assert.Equal(1, progress.Entries[EntryIds.AscensionTrial].First);
        Assert.Equal(new OdyleEnergy(500, 0), tap.Odyle);
    }

    // Another character named with no list between (the list lost to upstream's reassembly at a server change): the
    // counts were the last character's, so they go rather than be given to the new one.
    [Fact]
    public void AnotherCharacterWithoutAListDropsTheCounts()
    {
        var tap = NewTap(new() { [7] = Alpha, [9] = Beta }, new FakeTimeProvider(Start));
        tap.Observe(EntryListRecordWriter.List([Odyle]));
        tap.Observe(SelfRecord(7));
        List<CharacterProgress> raised = [];
        tap.ProgressObserved += (_, progress) => raised.Add(progress);

        tap.Observe(SelfRecord(9));
        var progress = Assert.Single(raised);
        Assert.Same(Beta, progress.Character);
        Assert.Empty(progress.Entries);
        Assert.Null(tap.Odyle);

        // The same character named again keeps them.
        tap.Observe(EntryListRecordWriter.Update([new(EntryIds.Nightmare, 4, 0)]));
        tap.Observe(SelfRecord(9));
        Assert.Equal(4, raised[^1].Entries[EntryIds.Nightmare].First);
    }

    // A meter started mid-session sees updates before any record names the character: they wait with the counts.
    [Fact]
    public void UpdatesBeforeAnyCharacterIsNamedWait()
    {
        var tap = NewTap(new() { [7] = Alpha }, new FakeTimeProvider(Start));
        List<CharacterProgress> raised = [];
        tap.ProgressObserved += (_, progress) => raised.Add(progress);
        tap.Observe(EntryListRecordWriter.Update([Odyle]));
        Assert.Empty(raised);
        Assert.Equal(new OdyleEnergy(30, 880), tap.Odyle);

        tap.Observe(SelfRecord(7));
        Assert.Equal(30, Assert.Single(raised).Entries[EntryIds.OdyleEnergy].First);
    }

    // A record upstream has no name for (it could not read it) changes nothing, and says so once until one is named.
    [Fact]
    public void ARecordUpstreamHasNoNameForChangesNothingAndIsLoggedOnce()
    {
        var log = new RecordingLogger<EntryListTap>();
        var tap = NewTap(new() { [7] = Alpha }, new FakeTimeProvider(Start), log);
        List<CharacterProgress> raised = [];
        tap.ProgressObserved += (_, progress) => raised.Add(progress);
        tap.Observe(EntryListRecordWriter.List([Odyle]));
        tap.Observe(SelfRecord(8));
        tap.Observe(SelfRecord(8));
        Assert.Empty(raised);
        Assert.Single(log.Entries, e => e.Message.Contains("no name for it", StringComparison.Ordinal));

        tap.Observe(SelfRecord(7));
        Assert.Same(Alpha, Assert.Single(raised).Character);
        tap.Observe(SelfRecord(8));
        Assert.Equal(2, log.Entries.Count(e => e.Message.Contains("no name for it", StringComparison.Ordinal)));
    }

    // An Odyle entry past its bound is not taken as the energy: the last one stays.
    [Fact]
    public void OdyleEnergyPastItsBoundIsNotKept()
    {
        var tap = NewTap(new() { [7] = Alpha }, new FakeTimeProvider(Start));
        tap.Observe(EntryListRecordWriter.List([Odyle]));
        tap.Observe(EntryListRecordWriter.Update([new(EntryIds.OdyleEnergy, EntryListRecords.MaxOdyleValue + 1, 0)]));
        Assert.Equal(new OdyleEnergy(30, 880), tap.Odyle);
        CharacterProgress? progress = null;
        tap.ProgressObserved += (_, raised) => progress = raised;
        tap.Observe(SelfRecord(7));
        Assert.Equal(30, progress!.Entries[EntryIds.OdyleEnergy].First);
    }

    // Upstream's player as the character: only the one marked as the user and named, its class by Global's name.
    [Fact]
    public void UpstreamsUserIsTheCharacter()
    {
        static AionDpsMeter.Core.Models.Player Player(string name = "Alpha", bool user = true, bool identified = true, bool classed = true) => new()
        {
            Id = 7, Name = name, IsIdentified = identified, IsUser = user, ServerName = "SIE", CombatPower = 41_000,
            CharacterClass = classed ? new AionDpsMeter.Core.Models.CharacterClass { Id = 16, Name = "Elementalist" } : null,
        };
        Assert.Equal(new CharacterIdentity("Alpha", "SIE", 16, "Spiritmaster", 41_000), UpstreamMapping.ToIdentity(Player()));
        Assert.Equal(new CharacterIdentity("Alpha", "SIE", 0, string.Empty, 41_000), UpstreamMapping.ToIdentity(Player(classed: false)));
        Assert.Null(UpstreamMapping.ToIdentity(Player(user: false)));
        Assert.Null(UpstreamMapping.ToIdentity(Player(identified: false)));
        Assert.Null(UpstreamMapping.ToIdentity(Player(name: " ")));
        Assert.Null(UpstreamMapping.ToIdentity(null));
    }

    // The counts are the character's, so the game moving to a new connection (a server change) keeps them.
    [Fact]
    public void TheCountsAreTheCharactersAcrossANewConnection()
    {
        var tap = NewTap(new() { [7] = Alpha }, new FakeTimeProvider(Start));
        tap.Observe(EntryListRecordWriter.List([Odyle]));
        tap.Observe(SelfRecord(7));
        tap.ConnectionStarted();
        Assert.Equal(new OdyleEnergy(30, 880), tap.Odyle);
        Assert.Same(Alpha, tap.Character);
    }

    // What is logged says which record a change came from and what happened, never a name, a server or a count.
    [Fact]
    public void TheTapLogsNoNameAndNoCount()
    {
        var log = new RecordingLogger<EntryListTap>();
        var tap = NewTap(new() { [7] = Alpha, [9] = Beta }, new FakeTimeProvider(Start), log);
        tap.Observe(EntryListRecordWriter.List([new(EntryIds.OdyleEnergy, 777, 1999), new(EntryIds.Nightmare, 13, 5)]));
        tap.Observe(SelfRecord(7));
        tap.Observe(EntryListRecordWriter.Update([new(EntryIds.OdyleEnergy, 767, 1999)]));
        tap.Observe(EntryListRecordWriter.Update([new(EntryIds.OdyleEnergy, 767, 1999)]));
        tap.Observe(SelfRecord(9));
        var changes = log.Entries.Where(e => e.Message.StartsWith("Odyle energy changed", StringComparison.Ordinal)).Select(e => e.Message).ToList();
        Assert.Equal(2, changes.Count);
        Assert.Contains("list of entry counts", changes[0], StringComparison.Ordinal);
        Assert.Contains("an update", changes[1], StringComparison.Ordinal);
        Assert.Contains(log.Entries, e => e.Message.Contains("matched to the character", StringComparison.Ordinal));
        Assert.Contains(log.Entries, e => e.Message.Contains("were dropped", StringComparison.Ordinal));
        Assert.All(log.Entries, e =>
        {
            foreach (var secret in new[] { "Alpha", "Beta", "SIE", "Spiritmaster", "Gladiator", "41000", "41,000", "777", "767", "1999", "1,999" })
                Assert.DoesNotContain(secret, e.Message, StringComparison.Ordinal);
        });
    }

    // A subscriber that throws is logged, and the next record is still read.
    [Fact]
    public void AFailingSubscriberIsContained()
    {
        var log = new RecordingLogger<EntryListTap>();
        var tap = NewTap(new() { [7] = Alpha }, new FakeTimeProvider(Start), log);
        tap.ProgressObserved += (_, _) => throw new InvalidOperationException("A failing subscriber.");
        tap.OdyleUpdated += (_, _) => throw new InvalidOperationException("A failing subscriber.");
        tap.Observe(EntryListRecordWriter.List([Odyle]));
        tap.Observe(SelfRecord(7));
        tap.Observe(EntryListRecordWriter.Update([new(EntryIds.OdyleEnergy, 20, 880)]));
        Assert.Equal(new OdyleEnergy(20, 880), tap.Odyle);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("subscriber failed", StringComparison.Ordinal));
    }

    // ---------- From the stream ----------

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
        stream.AddData("synthetic", EntryListRecordWriter.List([new(EntryIds.OdyleEnergy, 420, 1200)]), 0);
        Assert.Equal(new OdyleEnergy(420, 1200), engine.OdyleEnergy);
        stream.AddData("synthetic", EntryListRecordWriter.Update([Odyle]), 0);
        stream.AddData("synthetic", EntryListRecordWriter.Update([Odyle]), 0); // the same value again
        Assert.Equal(new OdyleEnergy(30, 880), engine.OdyleEnergy);
        Assert.Equal([new OdyleEnergy(420, 1200), new OdyleEnergy(30, 880)], raised);

        // Kept across a reset of the fight.
        engine.Reset();
        Assert.Equal(new OdyleEnergy(30, 880), engine.OdyleEnergy);
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
        stream.AddData("synthetic", Bundle([.. other, .. EntryListRecordWriter.Update([Odyle])]), 0);
        Assert.Equal(new OdyleEnergy(30, 880), engine.OdyleEnergy);
    }

    // Through the real graph: upstream's PlayerInfoProcessor reads the player's own record (name, server, combat power)
    // and marks the user before the tap, subscribed after upstream's pipeline, asks upstream who the record's entity is;
    // in a bundle, upstream has read the whole bundle first. The record is the writer's, in the layout upstream reads.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UpstreamNamesTheCharacterBeforeTheTapReadsItsRecord(bool bundled)
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var engine = host.Services.GetRequiredService<ICombatEngine>();
        var stream = host.Services.GetRequiredService<TcpStreamBuffer>();
        List<CharacterProgress> raised = [];
        engine.ProgressObserved += (_, progress) => raised.Add(progress);
        var server = ServerMap.Servers.First();
        var list = EntryListRecordWriter.List([Odyle, new(EntryIds.Nightmare, 11, 1)]);
        var self = EntryListRecordWriter.SelfRecord(4321, "Synthia", server.Key, 0, 1_234_567);

        Synchronise(stream);
        if (bundled) stream.AddData("synthetic", Bundle([.. list, .. self]), 0);
        else stream.AddData("synthetic", [.. list, .. self], 0);

        var progress = Assert.Single(raised);
        Assert.Equal(new CharacterIdentity("Synthia", server.Value, 0, string.Empty, 1_234_567), progress.Character);
        Assert.Equal(progress.Character, engine.Character);
        Assert.Equal(11, progress.Entries[EntryIds.Nightmare].First);
    }

    // The tap depends on upstream's pipeline, so even resolved first it is built, and listens to the stream, after it.
    [Fact]
    public void TheRecordTapIsBuiltAfterUpstreamsPipeline()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        _ = host.Services.GetRequiredService<RecordTap>();
        var tap = host.Services.GetRequiredService<EntryListTap>();
        var stream = host.Services.GetRequiredService<TcpStreamBuffer>();
        Synchronise(stream);
        stream.AddData("synthetic", [.. EntryListRecordWriter.List([Odyle]), .. EntryListRecordWriter.SelfRecord(4321, "Synthia", ServerMap.Servers.First().Key, 0, 0)], 0);
        Assert.Equal("Synthia", tap.Character?.Name);
    }

    // The reflection bind to upstream's internal ProcessPacket holds for the pinned release; an update of the pin
    // that renames or reshapes it fails here.
    [Fact]
    public void TheBundleOpenerBindsToThePinnedUpstream()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var open = RecordTap.BindBundleOpener(host.Services.GetRequiredService<PacketProcessor>());
        Assert.NotNull(open);
        var inner = EntryListRecordWriter.Update([Odyle]);
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
            [.. Frame(0xFFFF, "FF FF 10 00 00 00 01 02 03"), .. EntryListRecordWriter.Update([new(EntryIds.OdyleEnergy, 1, 2)]), .. EntryListRecordWriter.Update([Odyle])], 0);
        Assert.Equal(new OdyleEnergy(30, 880), engine.OdyleEnergy);
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
        stream.AddData("synthetic", Bundle([.. EntryListRecordWriter.Update([new(EntryIds.OdyleEnergy, 1, 2)]), .. EntryListRecordWriter.Update([Odyle])]), 0);
        Assert.Equal(new OdyleEnergy(30, 880), engine.OdyleEnergy);
    }

    // A reading failure (here a bundle opener that throws) is contained, logged once while it lasts and once more when
    // reading works again, and the chunk's next record is read.
    [Fact]
    public void AReadingFailureIsContainedAndLoggedOnce()
    {
        var stream = new TcpStreamBuffer(NullLogger<TcpStreamBuffer>.Instance);
        var log = new RecordingLogger<RecordTap>();
        var entries = NewTap([], TimeProvider.System);
        using var tap = new RecordTap(stream, _ => throw new InvalidOperationException("A failing bundle opener."), [entries], log);
        Synchronise(stream);
        var broken = Bundle(EntryListRecordWriter.Update([new(EntryIds.OdyleEnergy, 1, 2)]));
        stream.AddData("synthetic", [.. broken, .. broken, .. EntryListRecordWriter.Update([Odyle])], 0);
        Assert.Equal(new OdyleEnergy(30, 880), entries.Odyle);
        Assert.Single(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("failed", StringComparison.Ordinal));
        Assert.Single(log.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("works again", StringComparison.Ordinal));
    }

    [Fact]
    public void ADisposedTapStopsListening()
    {
        var stream = new TcpStreamBuffer(NullLogger<TcpStreamBuffer>.Instance);
        var entries = NewTap([], TimeProvider.System);
        var tap = new RecordTap(stream, openBundle: null, [entries], NullLogger<RecordTap>.Instance);
        tap.Dispose();
        tap.Dispose(); // twice is fine
        Synchronise(stream);
        stream.AddData("synthetic", EntryListRecordWriter.Update([Odyle]), 0);
        Assert.Null(entries.Odyle);
    }

    // ---------- The demo ----------

    // The demo hands the tap a login list and the user's own record when it begins, and an Odyle update as each later
    // fight starts and spends some. They go to the tap directly, not through the stream (upstream's reassembly needs a
    // keep-alive from an earlier chunk), and never bundled; the record names the demo's user, whom the tap asks upstream's
    // tracker about.
    [Fact]
    public void TheDemoSendsItsEntriesAndItsCharacter()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var engine = host.Services.GetRequiredService<ICombatEngine>();
        List<CharacterProgress> raised = [];
        engine.ProgressObserved += (_, progress) => raised.Add(progress);
        var feed = new DemoCombatFeed(
            host.Services.GetRequiredService<EntityTracker>(), host.Services.GetRequiredService<CombatSessionManager>(),
            host.Services.GetRequiredService<ICombatHistoryStore>(), NullLogger.Instance, new Random(7),
            host.Services.GetRequiredService<EntryListTap>());
        var clock = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        feed.Begin(clock);
        Assert.Equal(new OdyleEnergy(420, 1200), engine.OdyleEnergy);
        var login = Assert.Single(raised);
        var user = DemoRoster.Party.Single(member => member.IsUser);
        Assert.Equal(user.Name, login.Character.Name);
        Assert.Equal(DemoRoster.ServerName, login.Character.Server);
        Assert.Equal(user.ClassId, login.Character.ClassId);
        Assert.Equal(new OdyleEnergy(420, 1200), login.Odyle);
        Assert.Equal(11, login.Entries[EntryIds.Nightmare].First);
        Assert.Equal(3, login.Entries[EntryIds.AscensionTrial].First);

        // Past the first fight (at most 90 seconds) and its 10-second break: the second fight spends 10.
        for (var end = clock.AddSeconds(105); clock < end; clock = clock.AddMilliseconds(100))
            feed.Step(clock, TestContext.Current.CancellationToken);
        Assert.Equal(new OdyleEnergy(410, 1200), engine.OdyleEnergy);
        Assert.Equal(new OdyleEnergy(410, 1200), raised[^1].Odyle);
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

    // However long the demo runs, its energy is never 0 and 0, so the display keeps moving (audit on PR #36: at 0 and 0
    // after about 160 fights it froze), and each value is one its update writes and reads back.
    [Fact]
    public void TheDemoNeverRunsOutOfOdyle()
    {
        var energy = new OdyleEnergy(420, 1200);
        for (var fight = 0; fight < 1000; fight++)
        {
            energy = DemoCombatFeed.AfterFight(energy);
            Assert.False(energy is { Basic: 0, Charged: 0 }, $"0 and 0 after fight {fight + 1}");
            var update = EntryListRecordWriter.Update([new(EntryIds.OdyleEnergy, energy.Basic, energy.Charged)]);
            Assert.Equal(energy, EntryListRecords.OdyleIn(EntryListRecords.Read(update)!.Counts));
        }
    }
}
