using AionDpsMeter.Services.PacketCapture;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ScarletMeter.Engine;
using ScarletMeter.Engine.Pets;
using ScarletMeter.Engine.Upstream;
using static ScarletMeter.Tests.SyntheticRecords;

namespace ScarletMeter.Tests;

/// <summary>
/// The player's pet records (PetRecords and PetRecordTap) in A2Tools-Plugins' documented shapes (pet-souls' manifest.json
/// and the decoder tests of A2Tools' meter): the collection with its two lists, a list past 127 pets, one pickup for two
/// pets, a level reached and a pet unlocked, trailing bytes, and what does not read. Every record is made up here
/// (SyntheticRecords.PetCollection, PetSouls, PetLevel), never captured bytes.
/// </summary>
public sealed class PetRecordsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 5, 0, 0, TimeSpan.Zero);

    // The opcodes are the bytes after the length, written out here rather than through the constants they pin.
    [Theory]
    [InlineData(0x00, 0x90)]
    [InlineData(0x09, 0x90)]
    [InlineData(0x0D, 0x90)]
    [InlineData(0x14, 0x90)]
    public void TheFourRecordsAreTheirBytes(byte first, byte second)
    {
        var opcode = (ushort)(first | second << 8);
        Assert.True(PetRecords.IsPetRecord(opcode));
        var record = first switch
        {
            0x00 => PetCollection([(1006, 2)], []),
            0x0D => PetSouls([(1006, 1)]),
            _ => PetLevel(opcode, 1006, 2),
        };
        Assert.Equal((first, second), (record[1], record[2]));
        Assert.NotNull(PetRecords.Read(record, Now));
    }

    // Other opcodes beside them are not pet records.
    [Theory]
    [InlineData(0x9001)]
    [InlineData(0x900E)]
    [InlineData(0x9101)]
    [InlineData(0x0090)]
    public void NeighbouringOpcodesAreNotPetRecords(ushort opcode) => Assert.False(PetRecords.IsPetRecord(opcode));

    // The collection: each pet with its level, then each with its points, in order; bytes after the points are not read.
    [Fact]
    public void TheCollectionListsEachPetsLevelThenItsPoints()
    {
        var record = PetCollection([(1060, 2), (1006, 3), (1020, 0)], [(1041, 17), (1060, 9)], trailing: [0x09, 0x09, 0x09]);
        var read = Assert.IsType<PetCollectionRead>(PetRecords.Read(record, Now));
        Assert.Equal([new PetLevel(1060, 2), new PetLevel(1006, 3), new PetLevel(1020, 0)], read.Levels);
        Assert.Equal([new PetSouls(1041, 17), new PetSouls(1060, 9)], read.Points);
        Assert.Equal(Now, read.SeenAt);
    }

    // A player with more than 127 pets: the count is a varint, two bytes from 128 on (A2Tools read it as one byte, which put
    // the whole list one byte off, 2026-10-13). 140 pets read, and the points after them.
    [Fact]
    public void ACollectionOfMoreThan127PetsReadsWhole()
    {
        var levels = Enumerable.Range(0, 140).Select(i => (1001 + i, 1 + i % 3)).ToArray();
        var record = PetCollection(levels, [(1001, 4)]);
        var read = Assert.IsType<PetCollectionRead>(PetRecords.Read(record, Now));
        Assert.Equal(140, read.Levels.Count);
        Assert.Equal(new PetLevel(1140, 1 + 139 % 3), read.Levels[^1]);
        Assert.Equal([new PetSouls(1001, 4)], read.Points);
    }

    // Empty lists read as an empty collection: a new character's.
    [Fact]
    public void AnEmptyCollectionReads()
    {
        var read = Assert.IsType<PetCollectionRead>(PetRecords.Read(PetCollection([], []), Now));
        Assert.Empty(read.Levels);
        Assert.Empty(read.Points);
    }

    // One kill can give souls to several pets: each is read (A2Tools read the first only until 2026-10-19).
    [Fact]
    public void APickupForTwoPetsReadsBoth()
    {
        var read = Assert.IsType<PetSoulsGained>(PetRecords.Read(PetSouls([(1022, 1), (1150, 1)]), Now));
        Assert.Equal([new PetSouls(1022, 1), new PetSouls(1150, 1)], read.Souls);
    }

    // A chest's souls come as one entry with more than one soul.
    [Fact]
    public void APickupMayGiveSeveralSouls()
    {
        var read = Assert.IsType<PetSoulsGained>(PetRecords.Read(PetSouls([(1006, 5)]), Now));
        Assert.Equal([new PetSouls(1006, 5)], read.Souls);
    }

    // An entry that gave no souls is left out; a pickup with none left gives nothing, as does one with no entries.
    [Fact]
    public void EntriesWithNoSoulsAreLeftOut()
    {
        var read = Assert.IsType<PetSoulsGained>(PetRecords.Read(PetSouls([(1006, 0), (1007, 1)]), Now));
        Assert.Equal([new PetSouls(1007, 1)], read.Souls);
        Assert.Null(PetRecords.Read(PetSouls([(1006, 0)]), Now));
        Assert.Null(PetRecords.Read(PetSouls([]), Now));
    }

    // A level reached and a pet unlocked carry the pet and its level after two bytes; only an unlock may be at level 0.
    [Fact]
    public void ALevelReachedAndAPetUnlockedReadTheirPetAndLevel()
    {
        Assert.Equal(new PetLevelReached(1006, 2, false, Now), PetRecords.Read(PetLevel(PetRecords.LevelUpOpcode, 1006, 2), Now));
        Assert.Equal(new PetLevelReached(1050, 2, true, Now), PetRecords.Read(PetLevel(PetRecords.UnlockedOpcode, 1050, 2), Now));
        Assert.Equal(new PetLevelReached(1050, 0, true, Now), PetRecords.Read(PetLevel(PetRecords.UnlockedOpcode, 1050, 0), Now));
        Assert.Null(PetRecords.Read(PetLevel(PetRecords.LevelUpOpcode, 1006, 0), Now));
    }

    // The bounds a misread breaks: a level past 3, a pet id of 0 or past an int, more points or souls than any pet holds or
    // a pickup gives. The bounds themselves still read.
    [Fact]
    public void ValuesNoPetCanHaveGiveNothing()
    {
        Assert.Null(PetRecords.Read(PetCollection([(1006, 4)], []), Now));
        Assert.Null(PetRecords.Read(PetCollection([(0, 1)], []), Now));
        Assert.Null(PetRecords.Read(PetCollection([(1006, 1)], [(1006, PetRecords.MaxPoints + 1)]), Now));
        Assert.NotNull(PetRecords.Read(PetCollection([(1006, 3)], [(1006, PetRecords.MaxPoints)]), Now));

        Assert.Null(PetRecords.Read(PetSouls([(0, 1)]), Now));
        Assert.Null(PetRecords.Read(PetSouls([(0x8000_0000, 1)]), Now));
        Assert.NotNull(PetRecords.Read(PetSouls([(0x7FFF_FFFF, 1)]), Now));
        Assert.Null(PetRecords.Read(PetSouls([(1006, PetRecords.MaxSoulsAtOnce + 1)]), Now));
        Assert.NotNull(PetRecords.Read(PetSouls([(1006, PetRecords.MaxSoulsAtOnce)]), Now));
        // One bad entry turns the whole pickup away, the good one before it too: the layout is not the one known.
        Assert.Null(PetRecords.Read(PetSouls([(1006, 1), (0, 1)]), Now));

        Assert.Null(PetRecords.Read(PetLevel(PetRecords.LevelUpOpcode, 1006, 4), Now));
        Assert.Null(PetRecords.Read(PetLevel(PetRecords.UnlockedOpcode, 0, 1), Now));
    }

    // Each record cut by a byte gives nothing, as does a count promising more entries than the bytes hold, or more than the
    // bound; nothing is sized from the count before its bytes are known to be there.
    [Fact]
    public void ARecordThatDoesNotReadWholeGivesNothing()
    {
        static byte[] Cut(byte[] record) => record[..^1];
        Assert.Null(PetRecords.Read(Cut(PetCollection([(1006, 2)], [(1006, 1)])), Now));
        Assert.Null(PetRecords.Read(Cut(PetSouls([(1006, 1)])), Now));
        Assert.Null(PetRecords.Read(Cut(PetLevel(PetRecords.LevelUpOpcode, 1006, 2)), Now));
        Assert.Null(PetRecords.Read(Cut(PetLevel(PetRecords.UnlockedOpcode, 1006, 2)), Now));

        Assert.Null(PetRecords.Read(PetCollection([(1006, 2)], [], levelCount: 2), Now));
        Assert.Null(PetRecords.Read(PetCollection([], [], levelCount: PetRecords.MaxEntries + 1), Now));
        Assert.Null(PetRecords.Read(PetCollection([], [], levelCount: uint.MaxValue), Now));
        Assert.Null(PetRecords.Read(PetSouls([(1006, 1)], count: 2), Now));
        // The collection's points list must be there.
        Assert.Null(PetRecords.Read(Frame(PetRecords.CollectionOpcode, "31020000 00"), Now));
        Assert.Null(PetRecords.Read(Frame(PetRecords.SoulsOpcode, string.Empty), Now));
        Assert.Null(PetRecords.Read([0x05], Now));
    }

    // The tap raises each record read, and nothing for one that does not read.
    [Fact]
    public void TheTapRaisesEachRecordRead()
    {
        var tap = new PetRecordTap(new FakeTimeProvider(Now), NullLogger<PetRecordTap>.Instance);
        var raised = new List<PetRecord>();
        tap.Observed += (_, record) => raised.Add(record);
        tap.Observe(PetCollection([(1006, 2)], [(1006, 13)]));
        tap.Observe(PetSouls([(1006, 1)]));
        tap.Observe(PetSouls([(1006, 1)], count: 3));
        tap.Observe(PetLevel(PetRecords.LevelUpOpcode, 1006, 3));
        tap.Observe(PetLevel(PetRecords.UnlockedOpcode, 1050, 1));
        tap.Observe(FieldBossMap(0, 1010, []));
        Assert.Equal([typeof(PetCollectionRead), typeof(PetSoulsGained), typeof(PetLevelReached), typeof(PetLevelReached)], raised.Select(r => r.GetType()));
        Assert.All(raised, record => Assert.Equal(Now, record.SeenAt));
    }

    // A kind of record that stops reading is logged once, not at every kill, until one reads again; the log never carries a
    // pet or a count.
    [Fact]
    public void ARecordThatDoesNotReadIsLoggedOnceUntilOneReads()
    {
        var log = new RecordingLogger<PetRecordTap>();
        var tap = new PetRecordTap(new FakeTimeProvider(Now), log);
        for (var i = 0; i < 3; i++) tap.Observe(PetSouls([(1006, 1)], count: 2));
        Assert.Single(log.Entries, entry => entry.Message.Contains("did not read", StringComparison.Ordinal));
        Assert.Contains("0D 90", log.Entries[0].Message, StringComparison.Ordinal);
        tap.Observe(PetSouls([(1006, 1)]));
        tap.Observe(PetSouls([(1006, 1)], count: 2));
        Assert.Equal(2, log.Entries.Count(entry => entry.Message.Contains("did not read", StringComparison.Ordinal)));
        tap.Observe(PetCollection([(1006, 2)], [(1006, 13)]));
        Assert.DoesNotContain(log.Entries, entry => entry.Message.Contains("1006", StringComparison.Ordinal) || entry.Message.Contains("13)", StringComparison.Ordinal));
    }

    // A subscriber that throws is logged, and the next record is still raised.
    [Fact]
    public void AFailingSubscriberDoesNotStopTheNextRecord()
    {
        var tap = new PetRecordTap(new FakeTimeProvider(Now), new RecordingLogger<PetRecordTap>());
        var calls = 0;
        tap.Observed += (_, _) => { calls++; throw new InvalidOperationException("subscriber"); };
        tap.Observe(PetSouls([(1006, 1)]));
        tap.Observe(PetSouls([(1006, 1)]));
        Assert.Equal(2, calls);
    }

    // Through upstream's real reassembly, plain and inside an LZ4 bundle: the records reach ICombatEngine.PetRecordObserved.
    [Fact]
    public void TheEngineReadsPetRecordsFromTheStreamPlainAndInABundle()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var engine = host.Services.GetRequiredService<ICombatEngine>();
        var stream = host.Services.GetRequiredService<TcpStreamBuffer>();
        var raised = new List<PetRecord>();
        engine.PetRecordObserved += (_, record) => raised.Add(record);

        Synchronise(stream);
        stream.AddData("synthetic", PetCollection([(1006, 2)], [(1006, 13)]), 0);
        stream.AddData("synthetic", Bundle([.. PetSouls([(1006, 1)]), .. PetLevel(PetRecords.LevelUpOpcode, 1006, 3)]), 0);

        Assert.Equal([typeof(PetCollectionRead), typeof(PetSoulsGained), typeof(PetLevelReached)], raised.Select(r => r.GetType()));
        Assert.Equal([new PetSouls(1006, 1)], ((PetSoulsGained)raised[1]).Souls);
    }
}
