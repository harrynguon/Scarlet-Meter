using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ScarletMeter.App.Pets;
using ScarletMeter.App.Settings;
using ScarletMeter.App.Storage;
using ScarletMeter.Engine;

namespace ScarletMeter.Tests;

/// <summary>
/// The pet counts while the meter runs (PetService) and on disk (PetStore, PetNormalizer): a record put on the server of the
/// character being played; held, in order, while that is not sure, put on the server of the character named next, dropped on
/// a move to a new connection or when held too long; not kept when the character's server is not known; each server's
/// collection apart; saved a moment after the last change, each kind of change on its own; the overlay's notices and this
/// run's tally; and the file's checks. Every count is made up.
/// </summary>
public sealed class PetServiceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 8, 5, 0, 0, TimeSpan.Zero);
    private const int Sparkie = 1006;
    private const int Potcrab = 1007;
    private const int ServerA = 1503;
    private const int ServerB = 2012;

    private readonly TestDataDirectory directory = new();
    private readonly FakeTimeProvider clock = new(Start);
    private readonly RosterTests.FakeEngine engine = new();

    public void Dispose() => directory.Dispose();

    private static CharacterIdentity Character(int serverId) => new("Alpha", string.Empty, 16, "Spiritmaster", 41_000, serverId);

    private static CharacterProgress Progress(int serverId) => new(Character(serverId), new Dictionary<uint, EntryCount>());

    private PetCollectionRead Collection(params (int Pet, int Level, int Souls)[] pets) =>
        new([.. pets.Select(p => new PetLevel(p.Pet, p.Level))], [.. pets.Where(p => p.Souls > 0).Select(p => new PetSouls(p.Pet, p.Souls))], clock.GetUtcNow());

    private PetSoulsGained Souls(int pet, int amount = 1) => new([new PetSouls(pet, amount)], clock.GetUtcNow());

    private PetStore NewStore(bool demo = false)
    {
        Directory.CreateDirectory(directory.FullPath);
        return new PetStore(directory.FullPath, demo, NullLogger<PetStore>.Instance);
    }

    private SettingsService NewSettings() =>
        new(new SettingsStore(directory.FullPath, NullLogger<SettingsStore>.Instance), new EngineSettings(), NullLogger<SettingsService>.Instance);

    private PetService NewService(PetStore store, SettingsService settings, RecordingLogger<PetService>? log = null) =>
        new(engine, store, settings, clock, (Microsoft.Extensions.Logging.ILogger<PetService>?)log ?? NullLogger<PetService>.Instance);

    private static PetProgress CountOf(PetView view, int pet) => view.Pets.Single(row => row.Info.Id == pet).Progress;

    private static PetServer? Saved(PetStore store, int server) => store.Load(Start.AddDays(1)).File.Servers.SingleOrDefault(s => s.ServerId == server);

    // ---------- Whose server ----------

    // With the character known, records go on its server: the collection, then a soul on top of it.
    [Fact]
    public void ARecordGoesOnTheServerOfTheCharacterBeingPlayed()
    {
        using var settings = NewSettings();
        using var service = NewService(NewStore(), settings);
        service.Start();
        service.Start();
        Assert.Equal((1, 1, 1), (engine.PetSubscribers, engine.Subscribers, engine.ConnectionSubscribers));
        engine.Character = Character(ServerA);
        engine.Raise(Progress(ServerA));

        engine.RaisePet(Collection((Sparkie, 2, 40)));
        engine.RaisePet(Souls(Sparkie));

        var view = service.View();
        Assert.Equal(ServerA, view.ServerId);
        Assert.Equal(new PetProgress(2, 41), CountOf(view, Sparkie));
        Assert.Equal(Start, view.SyncedAt);
        Assert.Equal(1, view.SessionSouls);
    }

    // While the character is not sure (a login, whose collection comes before the record naming them), records are held in
    // order and go on the server of the character the game names next.
    [Fact]
    public void RecordsReadWhileTheCharacterIsNotSureWaitForTheGameToNameThem()
    {
        using var settings = NewSettings();
        using var service = NewService(NewStore(), settings);
        service.Start();
        engine.RaisePet(Collection((Sparkie, 2, 40)));
        engine.RaisePet(Souls(Sparkie));
        Assert.Null(service.View().ServerId);

        engine.Character = Character(ServerB);
        engine.Raise(Progress(ServerB));
        var view = service.View();
        Assert.Equal(ServerB, view.ServerId);
        Assert.Equal(new PetProgress(2, 41), CountOf(view, Sparkie));

        // Released once: the next naming applies nothing again.
        engine.Raise(Progress(ServerB));
        Assert.Equal(new PetProgress(2, 41), CountOf(service.View(), Sparkie));
    }

    // A move to a new connection drops what is held: it was read on the connection left, and a character named on another
    // does not say whose server it was.
    [Fact]
    public void HeldRecordsAreDroppedWhenTheGameMovesToANewConnection()
    {
        using var settings = NewSettings();
        using var service = NewService(NewStore(), settings);
        service.Start();
        engine.RaisePet(Collection((Sparkie, 2, 40)));
        engine.RaiseConnection();
        engine.Character = Character(ServerA);
        engine.Raise(Progress(ServerA));
        Assert.False(service.View().Pets.Single(row => row.Info.Id == Sparkie).Counted);
    }

    // A record held longer than HoldFor is not applied when the character is finally named; one within it is.
    [Fact]
    public void ARecordHeldTooLongIsDropped()
    {
        using var settings = NewSettings();
        using var service = NewService(NewStore(), settings);
        service.Start();
        engine.RaisePet(Souls(Potcrab));
        clock.Advance(PetService.HoldFor - TimeSpan.FromSeconds(1));
        engine.RaisePet(Souls(Sparkie));
        clock.Advance(TimeSpan.FromSeconds(2));
        engine.Raise(Progress(ServerA));
        var view = service.View();
        Assert.Equal(new PetProgress(0, 1), CountOf(view, Sparkie));
        Assert.False(view.Pets.Single(row => row.Info.Id == Potcrab).Counted);
    }

    // At most MaxHeld records wait; past it the oldest go.
    [Fact]
    public void TheOldestHeldRecordsGoPastTheBound()
    {
        using var settings = NewSettings();
        using var service = NewService(NewStore(), settings);
        service.Start();
        engine.RaisePet(Souls(Potcrab));
        for (var i = 0; i < PetService.MaxHeld; i++) engine.RaisePet(Souls(Sparkie));
        engine.Raise(Progress(ServerA));
        var view = service.View();
        Assert.Equal(new PetProgress(3, PetService.MaxHeld - 105), CountOf(view, Sparkie));
        Assert.False(view.Pets.Single(row => row.Info.Id == Potcrab).Counted);
    }

    // A character known without a server: nothing is kept or held, and it is logged once, not at every kill.
    [Fact]
    public void ARecordWithNoServerToPutItOnIsNotKept()
    {
        using var settings = NewSettings();
        var log = new RecordingLogger<PetService>();
        using var service = NewService(NewStore(), settings, log);
        service.Start();
        engine.Character = Character(0);
        engine.RaisePet(Souls(Sparkie));
        engine.RaisePet(Souls(Sparkie));
        Assert.Single(log.Entries, entry => entry.Message.Contains("not known; not kept", StringComparison.Ordinal));
        engine.Character = Character(ServerA);
        engine.Raise(Progress(ServerA));
        Assert.False(service.View().Pets.Single(row => row.Info.Id == Sparkie).Counted);
    }

    // Each server keeps its own collection; the window follows the character's server, a server chosen in it stays until
    // the game names a character on another, and a server with no counts cannot be chosen.
    [Fact]
    public void EachServerKeepsItsOwnPets()
    {
        using var settings = NewSettings();
        using var service = NewService(NewStore(), settings);
        service.Start();
        engine.Character = Character(ServerA);
        engine.Raise(Progress(ServerA));
        engine.RaisePet(Collection((Sparkie, 2, 40)));
        engine.Character = Character(ServerB);
        engine.Raise(Progress(ServerB));
        engine.RaisePet(Collection((Sparkie, 1, 3)));

        Assert.Equal([ServerA, ServerB], service.View().Servers);
        Assert.Equal((ServerB, new PetProgress(1, 3)), (service.View().ServerId, CountOf(service.View(), Sparkie)));
        service.ChooseServer(ServerA);
        Assert.Equal((ServerA, new PetProgress(2, 40)), (service.View().ServerId, CountOf(service.View(), Sparkie)));
        service.ChooseServer(1001);
        Assert.Equal(ServerA, service.View().ServerId);
        engine.Raise(Progress(ServerB));
        Assert.Equal(ServerA, service.View().ServerId);
        engine.Character = Character(ServerA);
        engine.Raise(Progress(ServerA));
        engine.Character = Character(ServerB);
        engine.Raise(Progress(ServerB));
        Assert.Equal(ServerB, service.View().ServerId);
    }

    // Before any character this run, the window shows the server whose counts changed last; before any, none.
    [Fact]
    public void WithNoCharacterTheWindowShowsTheLastServerChanged()
    {
        var store = NewStore();
        store.Save(new PetFile
        {
            Servers =
            [
                new PetServer { ServerId = ServerA, Pets = [new PetEntry(Sparkie, 2, 40)], UpdatedAt = Start.AddHours(-1) },
                new PetServer { ServerId = ServerB, Pets = [new PetEntry(Sparkie, 1, 3)], UpdatedAt = Start.AddHours(-2) },
            ],
        });
        using var settings = NewSettings();
        using var service = NewService(store, settings);
        Assert.Equal(ServerA, service.View().ServerId);
        using var empty = NewService(new PetStore(Path.Combine(directory.FullPath, "none"), false, NullLogger<PetStore>.Instance), settings);
        var none = empty.View();
        Assert.Null(none.ServerId);
        Assert.Empty(none.Servers);
        Assert.Empty(none.Pets);
    }

    // The window lists every pet the catalog knows and any other the game named; a pet without a count is at level 0, not counted.
    [Fact]
    public void TheViewHasARowForEveryPet()
    {
        using var settings = NewSettings();
        using var service = NewService(NewStore(), settings);
        engine.Character = Character(ServerA);
        service.TakeRecord(Collection((Sparkie, 2, 40), (1500, 1, 0)));
        var view = service.View();
        Assert.Equal(PetCatalog.All.Count + 1, view.Pets.Count);
        Assert.Equal(new PetRow(new PetInfo(1500, "Pet 1500", PetGenus.Unknown, PetKind.Farmed), new PetProgress(1, 0), true, 0), view.Pets.Single(row => row.Info.Id == 1500));
        Assert.Equal(new PetRow(PetCatalog.Describe(Potcrab), default, false, 0), view.Pets.Single(row => row.Info.Id == Potcrab));
    }

    // ---------- Saving ----------

    // The counts are saved SaveDelay after the last change, not on each.
    [Fact]
    public void TheCountsAreSavedAMomentAfterTheLastChange()
    {
        var store = NewStore();
        using var settings = NewSettings();
        using var service = NewService(store, settings);
        engine.Character = Character(ServerA);
        service.TakeRecord(Collection((Sparkie, 2, 40)));
        clock.Advance(PetService.SaveDelay - TimeSpan.FromMilliseconds(1));
        service.TakeRecord(Souls(Sparkie));
        clock.Advance(PetService.SaveDelay - TimeSpan.FromMilliseconds(1));
        Assert.False(File.Exists(store.FilePath));
        clock.Advance(TimeSpan.FromMilliseconds(1));
        var saved = Assert.IsType<PetServer>(Saved(store, ServerA));
        Assert.Equal([new PetEntry(Sparkie, 2, 41)], saved.Pets);
        Assert.Equal(Start, saved.SyncedAt);
        Assert.Equal(clock.GetUtcNow() - PetService.SaveDelay, saved.UpdatedAt);
    }

    // Each kind of record, as the only change since the last save, is saved (a save test that passed with one removed proved
    // nothing: LESSONS.md).
    [Fact]
    public void EachKindOfRecordAloneIsSaved()
    {
        var store = NewStore();
        using var settings = NewSettings();
        using var service = NewService(store, settings);
        engine.Character = Character(ServerA);
        PetRecord[] records =
        [
            Collection((Sparkie, 1, 3)),
            Souls(Sparkie),
            new PetLevelReached(Sparkie, 2, false, Start),
            new PetLevelReached(Potcrab, 1, true, Start),
        ];
        PetEntry[][] expected =
        [
            [new PetEntry(Sparkie, 1, 3)],
            [new PetEntry(Sparkie, 1, 4)],
            [new PetEntry(Sparkie, 2, 0)],
            [new PetEntry(Sparkie, 2, 0), new PetEntry(Potcrab, 1, 0)],
        ];
        for (var i = 0; i < records.Length; i++)
        {
            service.TakeRecord(records[i]);
            clock.Advance(PetService.SaveDelay);
            Assert.Equal(expected[i], Saved(store, ServerA)!.Pets);
        }
    }

    // Disposal saves what changed and stops following the engine.
    [Fact]
    public void DisposalSavesAndLetsGo()
    {
        var store = NewStore();
        using var settings = NewSettings();
        var service = NewService(store, settings);
        service.Start();
        engine.Character = Character(ServerA);
        engine.RaisePet(Souls(Sparkie));
        service.Dispose();
        service.Dispose();
        Assert.Equal([new PetEntry(Sparkie, 0, 1)], Saved(store, ServerA)!.Pets);
        Assert.Equal((0, 0, 0), (engine.PetSubscribers, engine.Subscribers, engine.ConnectionSubscribers));
    }

    // A save that fails (the file held by a virus scan, a backup or a sync) is logged once and leaves the counts to save:
    // with no change after it, the save at exit writes them (the audit on #76: with the retry's mark removed every test
    // passed, and the last counts would have been lost). It says when saving works again.
    [Fact]
    public void AFailedSaveIsTriedAgainAtExit()
    {
        var store = NewStore();
        using var settings = NewSettings();
        var log = new RecordingLogger<PetService>();
        var service = NewService(store, settings, log);
        engine.Character = Character(ServerA);
        using (new FileStream(store.FilePath + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            service.TakeRecord(Souls(Sparkie));
            clock.Advance(PetService.SaveDelay);
            service.TakeRecord(Souls(Sparkie));
            clock.Advance(PetService.SaveDelay);
            Assert.False(File.Exists(store.FilePath));
        }
        Assert.Single(log.Entries, entry => entry.Message.Contains("could not be saved", StringComparison.Ordinal));
        service.Dispose();
        Assert.Equal([new PetEntry(Sparkie, 0, 2)], Saved(store, ServerA)!.Pets);
        Assert.Single(log.Entries, entry => entry.Message.Contains("works again", StringComparison.Ordinal));
    }

    // Counts saved by an earlier run are where the next run starts.
    [Fact]
    public void ANewRunStartsFromTheSavedCounts()
    {
        var store = NewStore();
        using var settings = NewSettings();
        using (var first = NewService(store, settings))
        {
            engine.Character = Character(ServerA);
            first.TakeRecord(Collection((Sparkie, 2, 40)));
        }
        using var second = NewService(store, settings);
        second.TakeRecord(Souls(Sparkie));
        Assert.Equal(new PetProgress(2, 41), CountOf(second.View(), Sparkie));
        Assert.Equal(Start, second.View().SyncedAt);
    }

    // A file from a newer build is shown and never overwritten.
    [Fact]
    public void AFileFromANewerBuildIsNeverOverwritten()
    {
        var store = NewStore();
        var newer = "{ \"version\": 99, \"servers\": [ { \"serverId\": 1503, \"pets\": [ { \"id\": 1006, \"level\": 2, \"souls\": 40 } ], \"updatedAt\": \"2026-11-08T04:00:00+00:00\" } ] }";
        File.WriteAllText(store.FilePath, newer);
        using var settings = NewSettings();
        using var service = NewService(store, settings);
        Assert.False(service.CanSave);
        Assert.Contains("newer version", service.Notice, StringComparison.Ordinal);
        engine.Character = Character(ServerA);
        service.TakeRecord(Souls(Sparkie));
        Assert.Equal(new PetProgress(2, 41), CountOf(service.View(), Sparkie));
        clock.Advance(PetService.SaveDelay);
        service.SaveNow();
        Assert.Equal(newer, File.ReadAllText(store.FilePath));
    }

    // ---------- Notices and the tally ----------

    // A soul for a pet below level 3 makes a notice with its souls and its progress, which gathers the souls that come while
    // it shows and goes NoticeFor after the last; at most two show, the latest first.
    [Fact]
    public void ASoulMakesANoticeThatGathersTheSoulsWhileItShows()
    {
        using var settings = NewSettings();
        using var service = NewService(NewStore(), settings);
        engine.Character = Character(ServerA);
        service.TakeRecord(Collection((Sparkie, 2, 40), (Potcrab, 1, 0), (1008, 0, 0)));
        Assert.Empty(service.Notices(clock.GetUtcNow()));

        service.TakeRecord(Souls(Sparkie));
        clock.Advance(TimeSpan.FromSeconds(5));
        service.TakeRecord(Souls(Sparkie));
        var notice = Assert.Single(service.Notices(clock.GetUtcNow()));
        Assert.Equal((Sparkie, 2, new PetProgress(2, 42)), (notice.Pet.Id, notice.Amount, notice.Progress));

        clock.Advance(TimeSpan.FromSeconds(1));
        service.TakeRecord(Souls(Potcrab));
        clock.Advance(TimeSpan.FromSeconds(1));
        service.TakeRecord(Souls(1008));
        Assert.Equal([1008, Potcrab], service.Notices(clock.GetUtcNow()).Select(n => n.Pet.Id));

        clock.Advance(PetService.NoticeFor + TimeSpan.FromSeconds(1));
        Assert.Empty(service.Notices(clock.GetUtcNow()));
        // A notice that has gone starts afresh.
        service.TakeRecord(Souls(Sparkie));
        Assert.Equal(1, Assert.Single(service.Notices(clock.GetUtcNow())).Amount);
        Assert.Equal(5, service.View().SessionSouls);
        Assert.Equal(3, service.View().Pets.Single(row => row.Info.Id == Sparkie).Session);
    }

    // The row keys its buttons by pet, so a pet given souls on two servers within the notice's time (around a switch) shows
    // once, its latest; a notice dated far ahead (the PC's clock set back since) goes rather than stay for hours.
    [Fact]
    public void ANoticeIsOnePerPetAndNeverStuckAhead()
    {
        using var settings = NewSettings();
        using var service = NewService(NewStore(), settings);
        engine.Character = Character(ServerA);
        service.TakeRecord(Souls(Sparkie));
        clock.Advance(TimeSpan.FromSeconds(2));
        engine.Character = Character(ServerB);
        service.TakeRecord(Souls(Sparkie, 2));
        var notice = Assert.Single(service.Notices(clock.GetUtcNow()));
        Assert.Equal(2, notice.Amount);

        service.TakeRecord(new PetSoulsGained([new PetSouls(Potcrab, 1)], clock.GetUtcNow().AddHours(1)));
        Assert.Equal([Sparkie], service.Notices(clock.GetUtcNow()).Select(n => n.Pet.Id));
    }

    // A pet already at level 3 makes no notice and no tally; the soul that brings one there does, with its progress at max.
    [Fact]
    public void APetAtLevel3MakesNoNotice()
    {
        using var settings = NewSettings();
        using var service = NewService(NewStore(), settings);
        engine.Character = Character(ServerA);
        service.TakeRecord(Collection((Sparkie, 3, 0), (Potcrab, 2, 74)));
        service.TakeRecord(Souls(Sparkie));
        Assert.Empty(service.Notices(clock.GetUtcNow()));
        service.TakeRecord(Souls(Potcrab));
        Assert.Equal(new PetProgress(3, 0), Assert.Single(service.Notices(clock.GetUtcNow())).Progress);
        Assert.Equal(1, service.View().SessionSouls);
    }

    // With notices off in the settings, none show; the counts and the tally go on.
    [Fact]
    public void NoNoticeShowsWhileTheyAreOff()
    {
        using var settings = NewSettings();
        settings.Update(s => s with { Pets = s.Pets with { Notices = false } });
        using var service = NewService(NewStore(), settings);
        engine.Character = Character(ServerA);
        service.TakeRecord(Souls(Sparkie));
        Assert.Empty(service.Notices(clock.GetUtcNow()));
        Assert.Equal(1, service.View().SessionSouls);
    }

    // ---------- The file ----------

    // A server that cannot be one is dropped, a server listed twice keeps its newest, a count out of range or listed twice is
    // dropped, and a date out of range is reset; the repairs name no pet and no count.
    [Fact]
    public void TheFileIsCheckedOnLoad()
    {
        var file = new PetFile
        {
            Servers =
            [
                new PetServer { ServerId = 3001, Pets = [new PetEntry(Sparkie, 1, 1)], UpdatedAt = Start },
                new PetServer { ServerId = ServerA, Pets = [new PetEntry(Sparkie, 1, 1)], UpdatedAt = Start.AddHours(-2) },
                new PetServer
                {
                    ServerId = ServerA,
                    Pets = [new PetEntry(Potcrab, 2, 5), new PetEntry(Sparkie, 4, 0), new PetEntry(0, 1, 1), new PetEntry(1008, 1, -1),
                        new PetEntry(1009, 1, PetRules.MaxSouls + 1), new PetEntry(Potcrab, 1, 1)],
                    SyncedAt = Start.AddDays(3),
                    UpdatedAt = Start.AddHours(-1),
                },
                new PetServer { ServerId = ServerB, Pets = [new PetEntry(Sparkie, 3, 0)], UpdatedAt = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero) },
            ],
        };
        var (normalized, repairs) = PetNormalizer.Normalize(file, Start);
        Assert.Equal([ServerA, ServerB], normalized.Servers.Select(s => s.ServerId));
        var a = normalized.Servers[0];
        Assert.Equal([new PetEntry(Potcrab, 2, 5)], a.Pets);
        Assert.Equal((null, Start.AddHours(-1)), (a.SyncedAt, a.UpdatedAt));
        Assert.Equal(App.Roster.RosterNormalizer.Earliest, normalized.Servers[1].UpdatedAt);
        Assert.Equal(4, repairs.Count);
        Assert.DoesNotContain(repairs, repair => repair.Contains("1006", StringComparison.Ordinal) || repair.Contains("1007", StringComparison.Ordinal));
        Assert.Equal(normalized.Servers, PetNormalizer.Normalize(normalized, Start).File.Servers);
        Assert.Empty(PetNormalizer.Normalize(normalized, Start).Repairs);
        Assert.Empty(PetNormalizer.Normalize(null, Start).File.Servers);
    }

    // Past MaxServers, the servers changed longest ago go; past MaxPets on a server, the last listed.
    [Fact]
    public void TheFileKeepsItsBounds()
    {
        var servers = Enumerable.Range(0, PetNormalizer.MaxServers + 1)
            .Select(i => new PetServer { ServerId = 1001 + i, UpdatedAt = Start.AddMinutes(-i) }).ToList();
        servers[0] = servers[0] with { Pets = [.. Enumerable.Range(1, PetNormalizer.MaxPets + 1).Select(id => new PetEntry(id, 0, 0))] };
        var (normalized, repairs) = PetNormalizer.Normalize(new PetFile { Servers = servers }, Start);
        Assert.Equal(PetNormalizer.MaxServers, normalized.Servers.Count);
        Assert.DoesNotContain(normalized.Servers, s => s.ServerId == 1001 + PetNormalizer.MaxServers);
        Assert.Equal(PetNormalizer.MaxPets, normalized.Servers[0].Pets.Count);
        Assert.Equal(PetNormalizer.MaxPets, normalized.Servers[0].Pets[^1].Id);
        Assert.Equal(2, repairs.Count);
    }

    // The demo's counts are a file of their own, and the data directory must be absolute.
    [Fact]
    public void TheDemoHasItsOwnFile()
    {
        Assert.Equal("pets.json", Path.GetFileName(NewStore().FilePath));
        Assert.Equal("demo-pets.json", Path.GetFileName(NewStore(demo: true).FilePath));
        Assert.Throws<ArgumentException>(() => new PetStore("relative", false, NullLogger<PetStore>.Instance));
    }

    // A damaged file is kept aside and the counts start afresh, said in the shared words with the pets' own.
    [Fact]
    public void ADamagedFileIsKeptAside()
    {
        var store = NewStore();
        File.WriteAllText(store.FilePath, "{ not json");
        var loaded = store.Load(Start);
        Assert.Empty(loaded.File.Servers);
        Assert.True(loaded.CanSave);
        Assert.Equal(StoredFileStatus.ResetToDefaults, loaded.Status);
        Assert.Contains("Your pet list file could not be read", loaded.Notice, StringComparison.Ordinal);
        Assert.Contains("the counts start again from your next login", loaded.Notice, StringComparison.Ordinal);
    }
}
