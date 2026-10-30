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

    // A kind 01 entry holds a u64 time (an Abyss passage's usage time left), not a count: kept apart, little-endian.
    [Fact]
    public void TimeEntriesAreKeptApartFromTheCounts()
    {
        var update = EntryListRecords.Read(Frame(0x610C, "00 01 82 96 98 00 11 22 33 00 00 00 00 00 03"));
        Assert.NotNull(update);
        Assert.Empty(update.Counts);
        Assert.Equal([new TimeEntry(10_000_002, 0x33_22_11)], update.Times);

        var list = EntryListRecords.Read(EntryListRecordWriter.List([Odyle, new(EntryIds.Nightmare, 9, 0)]));
        Assert.Equal([Odyle, new EntryValue(EntryIds.Nightmare, 9, 0)], list!.Counts);
        Assert.Equal([new TimeEntry(10_000_002, 130_000)], list.Times);
        Assert.Empty(EntryListRecords.Read(EntryListRecordWriter.Update([new(EntryIds.Nightmare, 9, 0)]))!.Times);
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

    private static EntryListTap NewTap(
        Dictionary<int, CharacterIdentity> who, TimeProvider clock, ILogger<EntryListTap>? log = null, Func<int, string>? classNameOf = null) =>
        new(id => who.GetValueOrDefault(id), clock, log ?? NullLogger<EntryListTap>.Instance, classNameOf);

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

    // The Abyss passages come as the game sends them, with the counts' rules: still to run as a time, used as a count with
    // no value. An id is in one or the other as last sent (entering a corridor sends its time, leaving it a count); a
    // time alone is a report; a whole list starts afresh. Nothing of them is logged.
    [Fact]
    public void ThePassagesGoWithTheCountsAsTimesOrCounts()
    {
        var clock = new FakeTimeProvider(Start);
        var log = new RecordingLogger<EntryListTap>();
        var tap = NewTap(new() { [7] = Alpha, [9] = Beta }, clock, log);
        List<CharacterProgress> raised = [];
        tap.ProgressObserved += (_, progress) => raised.Add(progress);
        tap.Observe(EntryListRecordWriter.List([Odyle, new(10_000_002, 0, 0), new(10_000_003, 0, 0)], [(10_000_001, 130_000)]));
        tap.Observe(SelfRecord(7));
        var login = Assert.Single(raised);
        Assert.Equal(new EntryTime(130_000, Start), Assert.Single(login.Times).Value);
        Assert.Equal(10_000_001u, Assert.Single(login.Times).Key);
        Assert.Equal(new EntryCount(0, 0, Start), login.Entries[10_000_002]);
        Assert.Equal(new EntryCount(0, 0, Start), login.Entries[10_000_003]);

        clock.Advance(TimeSpan.FromMinutes(5));
        tap.Observe(EntryListRecordWriter.Update([new(10_000_001, 0, 0)], zeroCount: true)); // run: a count now
        Assert.Empty(raised[^1].Times);
        Assert.Equal(new EntryCount(0, 0, Start.AddMinutes(5)), raised[^1].Entries[10_000_001]);

        clock.Advance(TimeSpan.FromMinutes(5));
        tap.Observe(EntryListRecordWriter.TimeUpdate(10_000_002, 120_000)); // a time alone is reported
        Assert.Equal(3, raised.Count);
        Assert.Equal(new EntryTime(120_000, Start.AddMinutes(10)), raised[^1].Times[10_000_002]);
        Assert.False(raised[^1].Entries.ContainsKey(10_000_002));

        tap.Observe(EntryListRecordWriter.List([Odyle], [])); // a new login's list without passages: those before are gone
        tap.Observe(SelfRecord(7));
        Assert.Empty(raised[^1].Times);
        Assert.DoesNotContain(raised[^1].Entries.Keys, EntryIds.AbyssPassages.Contains);

        tap.Observe(EntryListRecordWriter.TimeUpdate(10_000_003, 130_000));
        tap.Observe(SelfRecord(9)); // another character with no list between: the last one's passages go with its counts
        Assert.Same(Beta, raised[^1].Character);
        Assert.Empty(raised[^1].Times);

        Assert.DoesNotContain(log.Entries, e => e.Message.Contains("passage", StringComparison.OrdinalIgnoreCase));
        Assert.All(log.Entries, e => Assert.DoesNotContain("130", e.Message, StringComparison.Ordinal));
        Assert.All(log.Entries, e => Assert.DoesNotContain("10000", e.Message, StringComparison.Ordinal));
    }

    // Only the passages' time entries are kept: another id's time (none is read) neither reports nor drops that id's
    // count (deep audit on #66: a time under the Shugo keys' id dropped the keys). A passage's time after a login's list
    // that did not read (no counts) waits for the character named and goes to them, as counts do.
    [Fact]
    public void OnlyThePassagesTimesAreKept()
    {
        var clock = new FakeTimeProvider(Start);
        var log = new RecordingLogger<EntryListTap>();
        var tap = NewTap(new() { [7] = Alpha }, clock, log);
        List<CharacterProgress> raised = [];
        tap.ProgressObserved += (_, progress) => raised.Add(progress);
        tap.Observe(Frame(0x610B, "FF EE DD CC")); // a login's list that does not read: no counts, and the owner awaited
        tap.Observe(EntryListRecordWriter.TimeUpdate(10_000_001, 130_000));
        Assert.Empty(raised);
        tap.Observe(SelfRecord(7));
        Assert.Equal(new EntryTime(130_000, Start), Assert.Single(raised).Times[10_000_001]);
        Assert.Contains(log.Entries, e => e.Message.Contains("matched to the character", StringComparison.Ordinal));

        tap.Observe(EntryListRecordWriter.Update([new(EntryIds.ShugoFestivalKeys, 6, 9)]));
        Assert.Equal(2, raised.Count);
        tap.Observe(EntryListRecordWriter.TimeUpdate(EntryIds.ShugoFestivalKeys, 5));
        tap.Observe(EntryListRecordWriter.TimeUpdate(3, 999));
        Assert.Equal(2, raised.Count);
        Assert.Equal(new ShugoKeys(6, 9), tap.Shugo);
        Assert.Equal([10_000_001u], raised[^1].Times.Keys.ToArray());
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
        Assert.Null(tap.Character); // who is playing is not sure until the record names them

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

    // A new connection (a server change, or a switch whose list and self record upstream lost) keeps the counts but holds
    // their reports until a self record names the character again: the same one gets them, another drops them, so a
    // switch never puts one character's counts on another (deep audit on #48).
    [Fact]
    public void ANewConnectionHoldsReportsUntilTheCharacterIsNamedAgain()
    {
        var tap = NewTap(new() { [7] = Alpha, [9] = Beta }, new FakeTimeProvider(Start));
        tap.Observe(EntryListRecordWriter.List([Odyle]));
        tap.Observe(SelfRecord(7));
        List<CharacterProgress> raised = [];
        tap.ProgressObserved += (_, progress) => raised.Add(progress);

        tap.ConnectionStarted();
        tap.Observe(EntryListRecordWriter.Update([new(EntryIds.OdyleEnergy, 20, 880)]));
        Assert.Empty(raised);
        Assert.Equal(new OdyleEnergy(20, 880), tap.Odyle); // the overlay shows it at once
        Assert.Null(tap.Character); // not sure until a record names the character again
        tap.Observe(SelfRecord(7));
        Assert.Same(Alpha, tap.Character);
        Assert.Equal(20, Assert.Single(raised).Entries[EntryIds.OdyleEnergy].First);
        tap.Observe(EntryListRecordWriter.Update([new(EntryIds.OdyleEnergy, 10, 880)]));
        Assert.Equal(2, raised.Count); // confirmed again: updates are raised at once

        tap.ConnectionStarted();
        tap.Observe(EntryListRecordWriter.Update([new(EntryIds.OdyleEnergy, 120, 0)]));
        tap.Observe(SelfRecord(9));
        Assert.Same(Beta, raised[^1].Character);
        Assert.Empty(raised[^1].Entries);
        Assert.Null(tap.Odyle);
        Assert.DoesNotContain(raised, progress => progress.Character == Alpha && progress.Entries.TryGetValue(EntryIds.OdyleEnergy, out var energy) && energy.First == 120);
    }

    // The server id after the name goes with the character when upstream knows it and named the character's server by it:
    // SIE is both 1001 and 2002, so only the id tells the side. An id upstream does not know, or one whose short name is not
    // the character's (the identity read from another record), is not taken.
    [Fact]
    public void TheServerIdGoesWithTheCharacterWhenUpstreamAgrees()
    {
        var tap = NewTap(new() { [7] = Alpha }, new FakeTimeProvider(Start));
        List<CharacterProgress> raised = [];
        tap.ProgressObserved += (_, progress) => raised.Add(progress);
        tap.Observe(EntryListRecordWriter.SelfRecord(7, "Alpha", 2002, 16, 41_000));
        Assert.Equal(Alpha with { ServerId = 2002 }, tap.Character);
        Assert.Equal(2002, raised[^1].Character.ServerId);
        tap.Observe(EntryListRecordWriter.SelfRecord(7, "Alpha", 1001, 16, 41_000));
        Assert.Equal(Alpha with { ServerId = 1001 }, tap.Character);
        tap.Observe(EntryListRecordWriter.SelfRecord(7, "Alpha", 1002, 16, 41_000)); // NEZ, not the server upstream named
        Assert.Equal(Alpha, tap.Character);
        tap.Observe(EntryListRecordWriter.SelfRecord(7, "Alpha", 2999, 16, 41_000)); // not a server upstream knows
        Assert.Equal(Alpha, tap.Character);
        tap.Observe(EntryListRecordWriter.SelfRecord(7, "Alpha", 1503, 16, 41_000)); // a Global server, but upstream named SIE
        Assert.Equal(Alpha, tap.Character);
        Assert.Equal(5, raised.Count);
    }

    // Upstream's list of servers is Korea's, so it names no Global server and a Global character's short name is empty
    // (Harry's roster, 2026-10-27: every character had no server id, and so no class, level or gear score from its
    // login). An id upstream does not know is taken when it is a server that exists and upstream named none: made-up ids
    // in Global's ranges here, never a real character's.
    [Fact]
    public void AGlobalServerIdGoesWithTheCharacterUpstreamNamesNoServerFor()
    {
        var global = new CharacterIdentity("Gamma", string.Empty, 0, string.Empty, 30_000);
        var tap = NewTap(new() { [8] = global }, new FakeTimeProvider(Start));
        tap.Observe(EntryListRecordWriter.SelfRecord(8, "Gamma", 1503, 30, 30_000));
        Assert.Equal(global with { ServerId = 1503, ClassId = 17, ClassName = "Cleric" }, tap.Character);
        tap.Observe(EntryListRecordWriter.SelfRecord(8, "Gamma", 2323, 30, 30_000)); // Europe's last slot, Asmodian
        Assert.Equal(2323, tap.Character!.ServerId);
        foreach (var id in new[] { 1511, 2324, 1100, 1600, 3101, 1059, 999 }) // past the open slots, or no server at all
        {
            tap.Observe(EntryListRecordWriter.SelfRecord(8, "Gamma", id, 30, 30_000));
            Assert.Equal(global, tap.Character);
        }
        tap.Observe(EntryListRecordWriter.SelfRecord(8, "Gamma", 1001, 30, 30_000)); // upstream names 1001, but not for Gamma
        Assert.Equal(global, tap.Character);
    }

    // The servers that exist, as A2Tools' list has them (servers.rs at 5c6f02c, from NC's notice): each Global region's
    // first and last open slot for both factions and none past it, 106 Global servers in all, and Korea's and Taiwan's 58
    // a side.
    [Fact]
    public void TheGameServersAreThoseThatExist()
    {
        foreach (var (first, last) in new[] { (1101, 1109), (1201, 1205), (1301, 1323), (1401, 1406), (1501, 1510) })
        {
            foreach (var id in new[] { first, last, first + 1000, last + 1000 })
                Assert.True(GameServers.Exists(id), id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.False(GameServers.Exists(last + 1));
            Assert.False(GameServers.Exists(last + 1001));
        }
        Assert.Equal(106, Enumerable.Range(1000, 2000).Count(id => GameServers.Exists(id) && id % 1000 >= 100));
        Assert.True(GameServers.Exists(1001) && GameServers.Exists(2058));
        foreach (var id in new[] { 1059, 1000, 0, -1, 999, 3101, 1100, 2100, 1600, int.MaxValue, int.MinValue })
            Assert.False(GameServers.Exists(id), id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.All(ServerMap.Servers.Keys, id => Assert.True(GameServers.Exists(id))); // upstream's are among them
    }

    // The class codes after the server id come four to a class, as A2Tools reads them (JobClass::from_roster_class at
    // 5c6f02c; a Cleric's record held 30), mapped to upstream's class ids; anything else is no class.
    [Theory]
    [InlineData(0u, 0)]
    [InlineData(4u, 0)]
    [InlineData(5u, 11)]
    [InlineData(8u, 11)]
    [InlineData(9u, 12)]
    [InlineData(12u, 12)]
    [InlineData(13u, 14)]
    [InlineData(16u, 14)]
    [InlineData(17u, 13)]
    [InlineData(20u, 13)]
    [InlineData(21u, 16)]
    [InlineData(24u, 16)]
    [InlineData(25u, 15)]
    [InlineData(28u, 15)]
    [InlineData(29u, 17)]
    [InlineData(30u, 17)]
    [InlineData(32u, 17)]
    [InlineData(33u, 18)]
    [InlineData(36u, 18)]
    [InlineData(37u, 0)]
    [InlineData(0x0100001Fu, 0)]
    [InlineData(uint.MaxValue, 0)]
    public void TheClassCodesAreFourToAClass(uint code, int classId) => Assert.Equal(classId, SelfRecords.ClassIdOf(code));

    // Every class a code can be is in upstream's game data, by the name AION 2 Global gives it (Spiritmaster for upstream's
    // Elementalist), so the roster names it as a fight would.
    [Theory]
    [InlineData(5u, "Gladiator")]
    [InlineData(9u, "Templar")]
    [InlineData(13u, "Ranger")]
    [InlineData(17u, "Assassin")]
    [InlineData(21u, "Spiritmaster")]
    [InlineData(25u, "Sorcerer")]
    [InlineData(29u, "Cleric")]
    [InlineData(33u, "Chanter")]
    public void UpstreamsDataNamesEveryClassACodeCanBe(uint code, string name) =>
        Assert.Equal(name, UpstreamMapping.ClassNameOf(SelfRecords.ClassIdOf(code)));

    // The class code is the u32 right after the server id, read only after a real name and only when all four bytes are
    // there; the bytes are written out here, made up (none of A2Tools' captured bytes), so the layout is pinned apart from
    // the writer.
    [Fact]
    public void TheRecordHoldsTheClassCodeAfterTheServerId()
    {
        Assert.True(SelfRecords.TryRead(Frame(0x3633, "07 00 00 00 00 37 05 41 6C 70 68 61 E9 03 1F 00 00 00 02 21"), out var read));
        Assert.Equal((1001, 31u), (read.ServerId, read.ClassCode));
        Assert.True(SelfRecords.TryRead(Frame(0x3633, "07 00 00 00 00 37 05 41 6C 70 68 61 E9 03 1F 00 00"), out read));
        Assert.Equal((1001, 0u), (read.ServerId, read.ClassCode)); // cut inside the class code
        Assert.True(SelfRecords.TryRead(Frame(0x3633, "07 00 00 00 00 37 05 41 6C 70 68 61 E9 03 1F 00 00 01"), out read));
        Assert.Equal(0x0100001Fu, read.ClassCode); // read whole, little-endian: no class
        Assert.True(SelfRecords.TryRead(EntryListRecordWriter.SelfRecord(7, "$Kc03", 1001, 30, 0), out read));
        Assert.Equal(0u, read.ClassCode); // no real name, so no class either
        Assert.True(SelfRecords.TryRead(EntryListRecordWriter.SelfRecord(7, "Synthia", 2002, 22, 0), out read));
        Assert.Equal(22u, read.ClassCode);
    }

    // After the class code, a byte, the level (u32) and two gear scores (u32 each), as A2Tools reads them; the bytes written
    // out here are made up in that layout (class 31, level 33, gear score 517; level 52, 1,234), none of its captured bytes
    // (deep audit on #56: the first rows had been its fixture's): the level only after a code that is a class and only from
    // 1 to 99; the gear score, the first of the two, only with a level, from 1 to 100,000, when the second is that or more
    // within the same bound (a local probe on Harry's characters, 2026-10-28: the sheet's was the first, the second higher
    // for two of them; A2Tools' "both agree" showed those none).
    [Theory]
    [InlineData("1F 00 00 00 02 21 00 00 00 05 02 00 00 05 02 00 00", 33, 517)]
    [InlineData("1F 00 00 00 02 34 00 00 00 D2 04 00 00 D2 04 00 00", 52, 1234)]
    [InlineData("25 00 00 00 02 21 00 00 00 05 02 00 00 05 02 00 00", 0, 0)] // 37 is no class: nothing after it read
    [InlineData("1F 00 00 00 02 00 00 00 00 05 02 00 00 05 02 00 00", 0, 0)] // level 0
    [InlineData("1F 00 00 00 02 64 00 00 00 05 02 00 00 05 02 00 00", 0, 0)] // level 100
    [InlineData("1F 00 00 00 02 63 00 00 00 05 02 00 00 05 02 00 00", 99, 517)] // level 99
    [InlineData("1F 00 00 00 02 21 00 00 00 05 02 00 00 06 02 00 00", 33, 517)] // the second higher: the first taken
    [InlineData("1F 00 00 00 02 21 00 00 00 06 02 00 00 05 02 00 00", 33, 0)] // the second lower: none
    [InlineData("1F 00 00 00 02 21 00 00 00 05 02 00 00 A0 86 01 00", 33, 517)] // the second at the bound
    [InlineData("1F 00 00 00 02 21 00 00 00 05 02 00 00 A1 86 01 00", 33, 0)] // the second past the bound: none
    [InlineData("1F 00 00 00 02 21 00 00 00 A1 86 01 00 A1 86 01 00", 33, 0)] // 100,001
    [InlineData("1F 00 00 00 02 21 00 00 00 A0 86 01 00 A0 86 01 00", 33, 100_000)]
    [InlineData("1F 00 00 00 02 21 00 00 00 00 00 00 00 00 00 00 00", 33, 0)] // gear score 0
    [InlineData("1F 00 00 00 02 21 00 00 00 05 02 00 00 05 02 00", 33, 0)] // cut inside the second copy
    [InlineData("1F 00 00 00 02 21 00 00", 0, 0)] // cut inside the level
    public void TheRecordHoldsTheLevelAndGearScoreAfterTheClass(string profile, int level, int gearScore)
    {
        Assert.True(SelfRecords.TryRead(Frame(0x3633, "07 00 00 00 00 37 05 41 6C 70 68 61 E9 03 " + profile), out var read));
        Assert.Equal((1001, level, gearScore), (read.ServerId, read.Level, read.GearScore));
    }

    // While upstream knows no class for the character (it learns one only from skills in a fight), the record's goes with
    // it, from the first login; upstream's own, once known, is kept. Only from a record whose server id checked out (the
    // class sits right after it), only a code that is a class, and only a class upstream's data names.
    [Fact]
    public void TheClassGoesWithTheCharacterWhileUpstreamKnowsNone()
    {
        var unknown = new CharacterIdentity("Gamma", "SIE", 0, string.Empty, 30_000);
        var tap = NewTap(new() { [7] = Alpha, [8] = unknown }, new FakeTimeProvider(Start));
        List<CharacterProgress> raised = [];
        tap.ProgressObserved += (_, progress) => raised.Add(progress);

        tap.Observe(EntryListRecordWriter.SelfRecord(8, "Gamma", 1001, 30, 30_000));
        Assert.Equal(unknown with { ServerId = 1001, ClassId = 17, ClassName = "Cleric" }, tap.Character);
        Assert.Equal(17, raised[^1].Character.ClassId);
        tap.Observe(EntryListRecordWriter.SelfRecord(8, "Gamma", 1001, 22, 30_000));
        Assert.Equal((16, "Spiritmaster"), (tap.Character!.ClassId, tap.Character.ClassName));
        tap.Observe(EntryListRecordWriter.SelfRecord(8, "Gamma", 1002, 30, 30_000)); // NEZ, not the server upstream named
        Assert.Equal(unknown, tap.Character);
        tap.Observe(EntryListRecordWriter.SelfRecord(8, "Gamma", 1001, 37, 30_000)); // no class
        Assert.Equal(unknown with { ServerId = 1001 }, tap.Character);
        tap.Observe(EntryListRecordWriter.SelfRecord(7, "Alpha", 2002, 30, 41_000)); // upstream knows Alpha's: kept
        Assert.Equal(Alpha with { ServerId = 2002 }, tap.Character);

        var unnamedClass = NewTap(new() { [8] = unknown }, new FakeTimeProvider(Start), classNameOf: _ => string.Empty);
        unnamedClass.Observe(EntryListRecordWriter.SelfRecord(8, "Gamma", 1001, 30, 30_000));
        Assert.Equal(unknown with { ServerId = 1001 }, unnamedClass.Character); // a class upstream's data does not name
    }

    // The record's level and gear score go with the character, whatever upstream knows of its class (upstream reads
    // neither), but only from a record whose server id checked out, like the class; each record's own, so a level gained
    // shows at the next record.
    [Fact]
    public void TheLevelAndGearScoreGoWithTheCharacter()
    {
        var tap = NewTap(new() { [7] = Alpha }, new FakeTimeProvider(Start));
        List<CharacterProgress> raised = [];
        tap.ProgressObserved += (_, progress) => raised.Add(progress);

        tap.Observe(EntryListRecordWriter.SelfRecord(7, "Alpha", 2002, 22, 41_000, level: 45, gearScore: 2_040));
        Assert.Equal(Alpha with { ServerId = 2002, Level = 45, GearScore = 2_040 }, tap.Character);
        Assert.Equal((45, 2_040), (raised[^1].Character.Level, raised[^1].Character.GearScore));
        tap.Observe(EntryListRecordWriter.SelfRecord(7, "Alpha", 2002, 22, 41_000, level: 46, gearScore: 2_100));
        Assert.Equal((46, 2_100), (tap.Character!.Level, tap.Character.GearScore));
        tap.Observe(EntryListRecordWriter.SelfRecord(7, "Alpha", 1002, 22, 41_000, level: 46, gearScore: 2_100)); // NEZ: not upstream's server
        Assert.Equal(Alpha, tap.Character);
        tap.Observe(EntryListRecordWriter.SelfRecord(7, "Alpha", 2002, 37, 41_000, level: 46, gearScore: 2_100)); // no class: nothing after it
        Assert.Equal(Alpha with { ServerId = 2002 }, tap.Character);
    }

    // A self record without a real name names no one: no name flag, or a tutorial character's placeholder ("$" and a
    // code, which upstream's decoding turns into a plain name by dropping the "$"; A2Tools' is_placeholder_name).
    [Fact]
    public void ARecordWithoutARealNameNamesNoOne()
    {
        Assert.True(SelfRecords.TryRead(Frame(0x3633, "07 00 00 00 00 36 05 41 6C 70 68 61 E9 03"), out var read));
        Assert.Equal((7, true, 0), (read.EntityId, read.Unnamed, read.ServerId));
        Assert.True(SelfRecords.TryRead(EntryListRecordWriter.SelfRecord(7, "$Kc03", 1001, 30, 0, 28, 383), out read));
        Assert.Equal(new SelfRecord(7, Unnamed: true), read); // no real name, so no server, class, level or gear score either
        Assert.True(SelfRecords.TryRead(EntryListRecordWriter.SelfRecord(7, "$Kc0", 0, 0, 0), out read));
        Assert.False(read.Unnamed); // too short for a placeholder
        Assert.True(SelfRecords.TryRead(EntryListRecordWriter.SelfRecord(7, "$Kc0-3", 0, 0, 0), out read));
        Assert.False(read.Unnamed);
        Assert.True(SelfRecords.TryRead(EntryListRecordWriter.SelfRecord(7, "Synthia", 2002, 0, 0), out read));
        Assert.Equal((false, 2002), (read.Unnamed, read.ServerId));
        Assert.True(SelfRecords.TryRead(Frame(0x3633, "07 00 00 00 00 37 05 41 6C 70 68 61 E9"), out read));
        Assert.Equal((false, 0), (read.Unnamed, read.ServerId)); // cut inside the server id
        Assert.True(SelfRecords.TryRead(Frame(0x3633, "07 00 00"), out read)); // cut before the name: upstream decides
        Assert.Equal((7, false), (read.EntityId, read.Unnamed));
        Assert.False(SelfRecords.TryRead(Frame(0x3633, "00 00 00 00 00 37"), out _));

        var tap = NewTap(new() { [7] = Alpha }, new FakeTimeProvider(Start));
        List<CharacterProgress> raised = [];
        tap.ProgressObserved += (_, progress) => raised.Add(progress);
        tap.Observe(EntryListRecordWriter.List([Odyle]));
        tap.Observe(EntryListRecordWriter.SelfRecord(7, "$Kc03nyeQHr4", 0, 0, 0));
        Assert.Empty(raised);
        tap.Observe(SelfRecord(7));
        Assert.Same(Alpha, Assert.Single(raised).Character);
    }

    // Through upstream: its PlayerInfoProcessor makes "Kc03nyeQHr4" of a tutorial character's "$Kc03nyeQHr4", so the tap
    // must not take it as a character.
    [Fact]
    public void UpstreamsNameForATutorialCharacterIsNotACharacter()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var engine = host.Services.GetRequiredService<ICombatEngine>();
        var stream = host.Services.GetRequiredService<TcpStreamBuffer>();
        List<CharacterProgress> raised = [];
        engine.ProgressObserved += (_, progress) => raised.Add(progress);
        Synchronise(stream);
        stream.AddData("synthetic", [.. EntryListRecordWriter.List([Odyle]), .. EntryListRecordWriter.SelfRecord(4321, "$Kc03nyeQHr4", ServerMap.Servers.First().Key, 0, 0)], 0);
        Assert.Equal("Kc03nyeQHr4", host.Services.GetRequiredService<EntityTracker>().GetPlayerEntity(4321)?.Name);
        Assert.Empty(raised);
        Assert.Null(engine.Character);
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

    // The Shugo Festival keys (entry 1) come with the counts, through upstream's real reassembly to the engine: shown at
    // once from a login's list (before the character is named, as the Odyle energy is), changed by an update, gone with a
    // list without them, and kept across a reset of the fight.
    [Fact]
    public void TheEngineReadsTheShugoKeysFromTheStream()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var engine = host.Services.GetRequiredService<ICombatEngine>();
        var stream = host.Services.GetRequiredService<TcpStreamBuffer>();
        Assert.Null(engine.ShugoKeys);

        Synchronise(stream);
        // Entry 1, written as the number A2Tools knows it by, not through the constant, so the constant cannot drift.
        stream.AddData("synthetic", EntryListRecordWriter.List([Odyle, new(1, 12, 3), new(8, 6, 9)]), 0);
        Assert.Equal(1u, EntryIds.ShugoFestivalKeys);
        Assert.Equal(new ShugoKeys(12, 3), engine.ShugoKeys);
        stream.AddData("synthetic", EntryListRecordWriter.Update([new(1, 11, 3)]), 0);
        Assert.Equal(new ShugoKeys(11, 3), engine.ShugoKeys);
        engine.Reset();
        Assert.Equal(new ShugoKeys(11, 3), engine.ShugoKeys);
        stream.AddData("synthetic", EntryListRecordWriter.List([Odyle]), 0); // a login's list without them
        Assert.Null(engine.ShugoKeys);
    }

    // Another character named with no list between drops the last one's counts, the keys with them; a login's list for the
    // next character brings its server's.
    [Fact]
    public void TheShugoKeysGoWithTheCounts()
    {
        var tap = NewTap(new() { [7] = Alpha, [9] = Beta }, new FakeTimeProvider(Start));
        tap.Observe(EntryListRecordWriter.List([Odyle, new(EntryIds.ShugoFestivalKeys, 21, 0)])); // a list is found by its Odyle entry
        Assert.Equal(new ShugoKeys(21, 0), tap.Shugo); // before the character is named
        tap.Observe(SelfRecord(7));
        Assert.Equal(new ShugoKeys(21, 0), tap.Shugo);
        tap.Observe(SelfRecord(9)); // another character, no list between
        Assert.Null(tap.Shugo);
        tap.Observe(EntryListRecordWriter.List([Odyle, new(EntryIds.ShugoFestivalKeys, 6, 1)]));
        Assert.Equal(new ShugoKeys(6, 1), tap.Shugo);
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
        Assert.Equal(new CharacterIdentity("Synthia", server.Value, 0, string.Empty, 1_234_567, server.Key), progress.Character);
        Assert.Equal(progress.Character, engine.Character);
        Assert.Equal(11, progress.Entries[EntryIds.Nightmare].First);
    }

    // Upstream's class 10 is a Spiritmaster's spirit, not a player class: upstream can give it to the player from a
    // six-digit skill code (deep audit on #55), and the record's class stands over it. Its other classes are kept.
    [Fact]
    public void TheRecordsClassStandsOverUpstreamsSpiritClass()
    {
        var spirit = new CharacterIdentity("Delta", "SIE", EntryListTap.UpstreamSpiritClassId, "Spiritmaster spirit", 30_000);
        var tap = NewTap(new() { [8] = spirit }, new FakeTimeProvider(Start));
        tap.Observe(EntryListRecordWriter.SelfRecord(8, "Delta", 1001, 22, 30_000));
        Assert.Equal(spirit with { ServerId = 1001, ClassId = 16, ClassName = "Spiritmaster" }, tap.Character);
        tap.Observe(EntryListRecordWriter.SelfRecord(8, "Delta", 1001, 37, 30_000)); // no class in the record: upstream's stays
        Assert.Equal(spirit with { ServerId = 1001 }, tap.Character);
        Assert.Equal("Elementalist spirit", GameDataName(EntryListTap.UpstreamSpiritClassId)); // upstream's data: 10 is the spirit
    }

    // Upstream's own name for a class id, before Global's names apply.
    private static string GameDataName(int classId) =>
        AionDpsMeter.Core.GameData.Services.GameDataProvider.Instance.Classes.GetById(classId)?.Name ?? string.Empty;

    // Through the real graph, before any fight: upstream names the character but knows no class, and the record's class
    // code (30, a Cleric's) gives the engine's character, and so the roster, its class and Global name from the login,
    // with the record's level and gear score; upstream finds the combat power in the same record as before. On a server
    // upstream's list names, and on a Global one, which it names none for (the case Harry's roster showed missing).
    [Theory]
    [InlineData(1001)] // a server upstream's list names
    [InlineData(1503)] // a Global server (made up: Asia's third slot), which upstream's list does not name
    public void TheCharacterHasItsClassFromTheLoginWithoutAFight(int serverId)
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var engine = host.Services.GetRequiredService<ICombatEngine>();
        var stream = host.Services.GetRequiredService<TcpStreamBuffer>();
        List<CharacterProgress> raised = [];
        engine.ProgressObserved += (_, progress) => raised.Add(progress);

        Synchronise(stream);
        stream.AddData("synthetic",
            [.. EntryListRecordWriter.List([Odyle]), .. EntryListRecordWriter.SelfRecord(4321, "Synthia", serverId, 30, 1_234_567, level: 45, gearScore: 2_040)], 0);

        Assert.Null(host.Services.GetRequiredService<EntityTracker>().GetPlayerEntity(4321)?.CharacterClass); // upstream's: none
        var progress = Assert.Single(raised);
        Assert.Equal(new CharacterIdentity("Synthia", ServerMap.GetName(serverId), 17, "Cleric", 1_234_567, serverId, 45, 2_040), progress.Character);
        Assert.Equal(progress.Character, engine.Character);
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

    // The demo hands the tap a login list (its Shugo keys among the entries) and the user's own record when it begins, and
    // an Odyle update as each later fight starts and spends some. They go to the tap directly, not through the stream (upstream's reassembly needs a
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
        Assert.Equal(new ShugoKeys(15, 3), engine.ShugoKeys); // the overlay's keys in --demo (audit on #57)
        var login = Assert.Single(raised);
        var user = DemoRoster.Party.Single(member => member.IsUser);
        Assert.Equal(user.Name, login.Character.Name);
        Assert.Equal(DemoRoster.ServerName, login.Character.Server);
        Assert.Equal(user.ClassId, login.Character.ClassId);
        Assert.Equal(new OdyleEnergy(420, 1200), login.Odyle);
        Assert.Equal(11, login.Entries[EntryIds.Nightmare].First);
        Assert.Equal(3, login.Entries[EntryIds.AscensionTrial].First);
        Assert.Equal(new KinaBalance(1_234_567, 33_940, login.Kina!.SeenAt), login.Kina);

        // Past the first fight (at most 90 seconds) and its 10-second break: the second fight spends 10.
        for (var end = clock.AddSeconds(105); clock < end; clock = clock.AddMilliseconds(100))
            feed.Step(clock, TestContext.Current.CancellationToken);
        Assert.Equal(new OdyleEnergy(410, 1200), engine.OdyleEnergy);
        Assert.Equal(new ShugoKeys(15, 3), engine.ShugoKeys); // an Odyle update leaves the keys
        Assert.Equal(new OdyleEnergy(410, 1200), raised[^1].Odyle);
        Assert.Equal(1_236_067, raised[^1].Kina?.Kina);
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

    // ---------- Kina ----------

    private static readonly KinaSlot KinaSlot0 = new(InventoryRecords.KinaItem, 0, 1_234_567);
    private static readonly KinaSlot BoundSlot1 = new(InventoryRecords.BoundKinaItem, 1, 33_940);

    // The inventory's listing at world entry and its changes, in SkeeveAN's layout: Kina and Kina (Bound) found by their
    // item ids with the kind ten bytes before, in the inventory's container; a gone slot holds none.
    [Fact]
    public void KinaIsReadFromTheInventory()
    {
        var listing = InventoryRecords.Read(EntryListRecordWriter.InventoryListing([KinaSlot0, BoundSlot1]));
        Assert.NotNull(listing);
        Assert.True(listing.IsListing);
        Assert.Equal([KinaSlot0, BoundSlot1], listing.Slots);

        var change = InventoryRecords.Read(EntryListRecordWriter.InventoryChange([(KinaSlot0 with { Quantity = 1_236_067 }, (byte)1), (BoundSlot1, InventoryRecords.GoneKind)]));
        Assert.False(change!.IsListing);
        Assert.Equal([KinaSlot0 with { Quantity = 1_236_067 }, BoundSlot1 with { Quantity = 0 }], change.Slots);

        Assert.Empty(InventoryRecords.Read(EntryListRecordWriter.InventoryListing([]))!.Slots); // a listing without Kina: none
        Assert.Null(InventoryRecords.Read(EntryListRecordWriter.InventoryChange([(new KinaSlot(510_116_006, 4, 3), (byte)1)]))); // no Kina in it
    }

    // Only an entry that sits on the anchor is Kina: in the inventory's container, a kind of 0 to 2, the quantity's top
    // bytes zero; a listing too small to be one is not read; nothing throws on cut records.
    [Fact]
    public void OnlyAnEntryOnTheAnchorIsKina()
    {
        var entry = EntryListRecordWriter.InventoryChange([(KinaSlot0, (byte)1)]);
        // The entry starts after the length (1 byte), the opcode (2) and the count (1): kind at 4, item at 14, container at 26.
        Assert.NotNull(InventoryRecords.Read(entry));
        foreach (var (at, value) in new[] { (26, (byte)15), (4, (byte)3), (25, (byte)1), (24, (byte)1), (23, (byte)1) })
        {
            var broken = (byte[])entry.Clone();
            broken[at] = value;
            Assert.Null(InventoryRecords.Read(broken));
        }
        var small = EntryListRecordWriter.Frame(InventoryRecords.ListingOpcode, [.. EntryListRecordWriter.InventoryChange([(KinaSlot0, (byte)1)]).Skip(3)]);
        Assert.Null(InventoryRecords.Read(small));
        var listing = EntryListRecordWriter.InventoryListing([KinaSlot0]);
        for (var length = 0; length <= 80; length++) _ = InventoryRecords.Read(listing.AsSpan(0, length));
    }

    // The Kina goes with the counts: the login's listing waits for the record naming the character, then later changes
    // are theirs; a change of one item leaves the other as it was.
    [Fact]
    public void KinaGoesToTheCharacterNamed()
    {
        var clock = new FakeTimeProvider(Start);
        var tap = NewTap(new() { [7] = Alpha }, clock);
        List<CharacterProgress> raised = [];
        tap.ProgressObserved += (_, progress) => raised.Add(progress);
        tap.Observe(EntryListRecordWriter.List([Odyle]));
        tap.Observe(EntryListRecordWriter.InventoryListing([KinaSlot0, BoundSlot1]));
        Assert.Empty(raised);
        tap.Observe(SelfRecord(7));
        Assert.Equal(new KinaBalance(1_234_567, 33_940, Start), Assert.Single(raised).Kina);

        clock.Advance(TimeSpan.FromMinutes(5));
        tap.Observe(EntryListRecordWriter.InventoryChange([(KinaSlot0 with { Quantity = 1_000 }, (byte)1)]));
        Assert.Equal(new KinaBalance(1_000, 33_940, Start.AddMinutes(5)), raised[^1].Kina);
    }

    // Where the listing falls in a login is not documented: one read just before the login's list is that login's, one
    // read long before is the last character's and goes, as it goes when another character is named with no list between.
    [Fact]
    public void AListingBeforeTheLoginsListIsKeptOnlyWithinTheBurst()
    {
        var clock = new FakeTimeProvider(Start);
        var tap = NewTap(new() { [7] = Alpha, [9] = Beta }, clock);
        CharacterProgress? last = null;
        tap.ProgressObserved += (_, progress) => last = progress;

        tap.Observe(EntryListRecordWriter.InventoryListing([KinaSlot0]));
        clock.Advance(EntryListTap.LoginBurst);
        tap.Observe(EntryListRecordWriter.List([Odyle]));
        tap.Observe(SelfRecord(7));
        Assert.Equal(new KinaBalance(1_234_567, 0, Start), last!.Kina);

        clock.Advance(EntryListTap.LoginBurst + TimeSpan.FromSeconds(1));
        tap.Observe(EntryListRecordWriter.List([Odyle]));
        tap.Observe(SelfRecord(7));
        Assert.Null(last.Kina);

        tap.Observe(EntryListRecordWriter.InventoryListing([KinaSlot0]));
        tap.Observe(SelfRecord(9)); // another character with no list between
        Assert.Same(Beta, last.Character);
        Assert.Null(last.Kina);
    }

    // While whose the counts are is not sure (a login's list for a switch, a new connection), the inventory waits too: a
    // switch's Kina never lands on the character named before.
    [Fact]
    public void KinaWaitsWhileTheCharacterIsNotSure()
    {
        var tap = NewTap(new() { [7] = Alpha, [9] = Beta }, new FakeTimeProvider(Start));
        tap.Observe(EntryListRecordWriter.List([Odyle]));
        tap.Observe(SelfRecord(7));
        List<CharacterProgress> raised = [];
        tap.ProgressObserved += (_, progress) => raised.Add(progress);

        tap.Observe(EntryListRecordWriter.List([Odyle]));
        tap.Observe(EntryListRecordWriter.InventoryListing([KinaSlot0]));
        Assert.Empty(raised);
        tap.Observe(SelfRecord(9));
        var beta = Assert.Single(raised);
        Assert.Same(Beta, beta.Character);
        Assert.Equal(1_234_567, beta.Kina?.Kina);

        tap.ConnectionStarted();
        tap.Observe(EntryListRecordWriter.InventoryChange([(KinaSlot0 with { Quantity = 5 }, (byte)1)]));
        Assert.Single(raised);
        tap.Observe(SelfRecord(9));
        Assert.Equal(5, raised[^1].Kina?.Kina);
    }
    // A change of the last character's Kina just before a switch is not the next character's: only a listing read just
    // before a login's list is that login's (Codex and the deep audit on #50).
    [Fact]
    public void AChangeJustBeforeASwitchIsNotTheNextCharacters()
    {
        var clock = new FakeTimeProvider(Start);
        var tap = NewTap(new() { [7] = Alpha, [9] = Beta }, clock);
        CharacterProgress? last = null;
        tap.ProgressObserved += (_, progress) => last = progress;
        tap.Observe(EntryListRecordWriter.List([Odyle]));
        tap.Observe(EntryListRecordWriter.InventoryListing([KinaSlot0]));
        tap.Observe(SelfRecord(7));
        clock.Advance(TimeSpan.FromMinutes(30));
        tap.Observe(EntryListRecordWriter.InventoryChange([(KinaSlot0 with { Quantity = 999 }, (byte)1)]));
        Assert.Equal(999, last!.Kina?.Kina);
        clock.Advance(TimeSpan.FromSeconds(5));
        tap.Observe(EntryListRecordWriter.List([Odyle])); // the next login's, its listing missing
        tap.Observe(SelfRecord(9));
        Assert.Same(Beta, last.Character);
        Assert.Null(last.Kina);
    }

    // A listing read just before a move to a new connection is the last character's, not the next login's, however soon
    // the next login's list comes: only a listing on the login's own connection is kept by its time (Codex on #50). The
    // same character named again with no list between keeps it.
    [Fact]
    public void AListingFromTheConnectionLeftIsNotTheNextLogins()
    {
        var clock = new FakeTimeProvider(Start);
        var tap = NewTap(new() { [7] = Alpha, [9] = Beta }, clock);
        CharacterProgress? last = null;
        tap.ProgressObserved += (_, progress) => last = progress;
        tap.Observe(EntryListRecordWriter.List([Odyle]));
        tap.Observe(EntryListRecordWriter.InventoryListing([KinaSlot0]));
        tap.Observe(SelfRecord(7));
        Assert.Equal(1_234_567, last!.Kina?.Kina);

        clock.Advance(TimeSpan.FromSeconds(4));
        tap.ConnectionStarted(); // a switch, within the burst of Alpha's listing
        tap.Observe(EntryListRecordWriter.List([Odyle])); // Beta's login, its listing missing
        tap.Observe(SelfRecord(9));
        Assert.Same(Beta, last.Character);
        Assert.Null(last.Kina);

        tap.Observe(EntryListRecordWriter.InventoryListing([KinaSlot0 with { Quantity = 8 }]));
        clock.Advance(TimeSpan.FromSeconds(2));
        tap.ConnectionStarted(); // a server change on the same character
        tap.Observe(SelfRecord(9));
        Assert.Same(Beta, last.Character);
        Assert.Equal(8, last.Kina?.Kina);
    }

    // A listing held for a self record that never came is forgotten when its connection is left: a switch to B whose list
    // and self record were lost, then back to A on another connection whose list and listing were lost, must not give B's
    // Kina to A (Codex on #50).
    [Fact]
    public void AHeldListingIsForgottenWhenItsConnectionIsLeft()
    {
        var clock = new FakeTimeProvider(Start);
        var tap = NewTap(new() { [7] = Alpha, [9] = Beta }, clock);
        CharacterProgress? last = null;
        tap.ProgressObserved += (_, progress) => last = progress;
        tap.Observe(EntryListRecordWriter.List([Odyle]));
        tap.Observe(EntryListRecordWriter.InventoryListing([KinaSlot0]));
        tap.Observe(SelfRecord(7));
        Assert.Equal(1_234_567, last!.Kina?.Kina);

        clock.Advance(TimeSpan.FromMinutes(1));
        tap.ConnectionStarted(); // to Beta, its list and self record lost
        tap.Observe(EntryListRecordWriter.InventoryListing([KinaSlot0 with { Quantity = 50 }])); // Beta's, held
        clock.Advance(TimeSpan.FromMinutes(1));
        tap.ConnectionStarted(); // back to Alpha, its list and listing lost
        tap.Observe(SelfRecord(7));
        Assert.Same(Alpha, last.Character);
        Assert.Null(last.Kina); // not Beta's 50; the roster keeps Alpha's saved Kina
    }

    // A change read while no character is confirmed is forgotten when its connection is left too: a switch to B whose
    // first records were lost but for a change, then back to A whose list and listing were lost, must not give B's
    // quantity to A (Codex on #50). On the same character's server change, the change is confirmed by its self record.
    [Fact]
    public void AnUnconfirmedChangeIsForgottenWhenItsConnectionIsLeft()
    {
        var clock = new FakeTimeProvider(Start);
        var tap = NewTap(new() { [7] = Alpha, [9] = Beta }, clock);
        CharacterProgress? last = null;
        tap.ProgressObserved += (_, progress) => last = progress;
        tap.Observe(EntryListRecordWriter.List([Odyle]));
        tap.Observe(EntryListRecordWriter.InventoryListing([KinaSlot0]));
        tap.Observe(SelfRecord(7));

        clock.Advance(TimeSpan.FromMinutes(1));
        tap.ConnectionStarted(); // a server change on Alpha
        tap.Observe(EntryListRecordWriter.InventoryChange([(KinaSlot0 with { Quantity = 88 }, (byte)1)]));
        tap.Observe(SelfRecord(7));
        Assert.Equal(88, last!.Kina?.Kina); // confirmed by the self record
        tap.ConnectionStarted(); // another server change: a confirmed balance is kept
        tap.Observe(SelfRecord(7));
        Assert.Equal(88, last.Kina?.Kina);

        clock.Advance(TimeSpan.FromMinutes(1));
        tap.ConnectionStarted(); // to Beta, all but a change lost
        tap.Observe(EntryListRecordWriter.InventoryChange([(KinaSlot0 with { Quantity = 77 }, (byte)1)]));
        clock.Advance(TimeSpan.FromMinutes(1));
        tap.ConnectionStarted(); // back to Alpha, its list and listing lost
        tap.Observe(SelfRecord(7));
        Assert.Same(Alpha, last.Character);
        Assert.Null(last.Kina); // not Beta's 77; the roster keeps Alpha's saved Kina
    }

    // A listing while a character is named but with no self record just before it may be a switch's on the same
    // connection: it waits for the next self record, which takes it within the burst whoever it names, or for the same
    // character later (deep audit on #50).
    [Fact]
    public void AListingWithNoSelfRecordJustBeforeItWaits()
    {
        var clock = new FakeTimeProvider(Start);
        var tap = NewTap(new() { [7] = Alpha, [9] = Beta }, clock);
        List<CharacterProgress> raised = [];
        tap.ProgressObserved += (_, progress) => raised.Add(progress);
        tap.Observe(SelfRecord(7));
        clock.Advance(TimeSpan.FromMinutes(30));
        tap.Observe(EntryListRecordWriter.InventoryListing([KinaSlot0]));
        tap.Observe(EntryListRecordWriter.Update([Odyle]));
        Assert.Null(raised[^1].Kina); // Alpha's update does not carry the held listing
        clock.Advance(TimeSpan.FromSeconds(3));
        tap.Observe(SelfRecord(9));
        Assert.Same(Beta, raised[^1].Character);
        Assert.Equal(1_234_567, raised[^1].Kina?.Kina);

        clock.Advance(TimeSpan.FromMinutes(30));
        tap.Observe(EntryListRecordWriter.InventoryListing([KinaSlot0 with { Quantity = 4 }]));
        clock.Advance(TimeSpan.FromMinutes(5));
        tap.Observe(SelfRecord(9));
        Assert.Equal(4, raised[^1].Kina?.Kina); // the same character named again: theirs
    }

    // A switch on a new connection whose list was lost: the new character's listing, read after the move, waits for the
    // self record and is theirs (Codex on #50: it was dropped with the last character's counts).
    [Fact]
    public void AListingAfterAMoveGoesToTheCharacterNamedNext()
    {
        var clock = new FakeTimeProvider(Start);
        var tap = NewTap(new() { [7] = Alpha, [9] = Beta }, clock);
        CharacterProgress? last = null;
        tap.ProgressObserved += (_, progress) => last = progress;
        tap.Observe(SelfRecord(7));
        clock.Advance(TimeSpan.FromMinutes(10));
        tap.ConnectionStarted();
        tap.Observe(EntryListRecordWriter.InventoryListing([KinaSlot0 with { Quantity = 42 }]));
        clock.Advance(TimeSpan.FromSeconds(2));
        tap.Observe(SelfRecord(9));
        Assert.Same(Beta, last!.Character);
        Assert.Equal(42, last.Kina?.Kina);
    }
    // Kina read before any character was named, on a connection the game then left, is not given to whoever is named next.
    [Fact]
    public void KinaReadBeforeAnyoneIsNamedGoesWithItsConnection()
    {
        var tap = NewTap(new() { [9] = Beta }, new FakeTimeProvider(Start));
        CharacterProgress? last = null;
        tap.ProgressObserved += (_, progress) => last = progress;
        tap.Observe(EntryListRecordWriter.InventoryChange([(KinaSlot0 with { Quantity = 777 }, (byte)1)]));
        tap.ConnectionStarted();
        tap.Observe(SelfRecord(9));
        Assert.Null(last!.Kina);
    }

    // Kina is one slot per item: an entry in another slot is a move (a sort), not a second stack, and a gone entry clears it
    // only in the slot it is in (deep audit on #50: a move counted it twice).
    [Fact]
    public void KinaThatMovesSlotIsNotCountedTwice()
    {
        var tap = NewTap(new() { [7] = Alpha }, new FakeTimeProvider(Start));
        CharacterProgress? last = null;
        tap.ProgressObserved += (_, progress) => last = progress;
        tap.Observe(EntryListRecordWriter.List([Odyle]));
        tap.Observe(EntryListRecordWriter.InventoryListing([KinaSlot0]));
        tap.Observe(SelfRecord(7));
        tap.Observe(EntryListRecordWriter.InventoryChange([(KinaSlot0 with { Slot = 5 }, (byte)1), (KinaSlot0, InventoryRecords.GoneKind)]));
        Assert.Equal(1_234_567, last!.Kina?.Kina);
        tap.Observe(EntryListRecordWriter.InventoryChange([(KinaSlot0 with { Slot = 5 }, InventoryRecords.GoneKind)]));
        Assert.Equal(0, last.Kina?.Kina);
    }
    // A meter started mid-session sees changes before any listing: an item is known from its slots seen, the other not.
    [Fact]
    public void BeforeAnyListingOnlyTheItemSeenIsKnown()
    {
        var tap = NewTap(new() { [7] = Alpha }, new FakeTimeProvider(Start));
        CharacterProgress? last = null;
        tap.ProgressObserved += (_, progress) => last = progress;
        tap.Observe(SelfRecord(7));
        Assert.Null(last!.Kina);
        tap.Observe(EntryListRecordWriter.InventoryChange([(BoundSlot1, (byte)1)]));
        Assert.Equal(new KinaBalance(null, 33_940, Start), last.Kina);
    }

    // Through upstream's real reassembly: the listing, 5 KB and more, read from inside an LZ4 bundle.
    [Fact]
    public void TheEngineReadsKinaFromABundledListing()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var engine = host.Services.GetRequiredService<ICombatEngine>();
        var stream = host.Services.GetRequiredService<TcpStreamBuffer>();
        List<CharacterProgress> raised = [];
        engine.ProgressObserved += (_, progress) => raised.Add(progress);
        Synchronise(stream);
        stream.AddData("synthetic", Bundle([.. EntryListRecordWriter.List([Odyle]), .. EntryListRecordWriter.InventoryListing([KinaSlot0, BoundSlot1]),
            .. EntryListRecordWriter.SelfRecord(4321, "Synthia", ServerMap.Servers.First().Key, 0, 0)]), 0);
        var kina = Assert.Single(raised).Kina;
        Assert.Equal((1_234_567L, 33_940L), (kina?.Kina, kina?.Bound));
    }
}