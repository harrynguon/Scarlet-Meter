using AionDpsMeter.Core.GameData.Services;
using AionDpsMeter.Core.Models;
using AionDpsMeter.Services.Models;
using AionDpsMeter.Services.PacketCapture;
using AionDpsMeter.Services.PacketProcessing.Routing;
using AionDpsMeter.Services.Services.Entity;
using AionDpsMeter.Services.Services.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScarletMeter.Engine;
using ScarletMeter.Engine.Upstream;
using static ScarletMeter.Tests.SyntheticRecords;

namespace ScarletMeter.Tests;

/// <summary>
/// A player's class follows their own skills (<see cref="ClassSkills"/>): upstream keeps the class of the first skill it
/// saw from a player id, so a player handed an id another class had used showed that class, live and in saved fights.
/// Live, <see cref="ClassSkillTap"/> moves the class once most of the player's uses of class skills since their current
/// name say another, a run of hits with one skill counting once; details and saved fights take the class whose skills
/// did most of the player's damage. Skill codes are real ones from upstream's skills.json (two Cleric skills and a Cleric
/// heal, two Ranger, Chanter and Gladiator skills each, a Spiritmaster's); players, names and numbers are made up.
/// </summary>
public sealed class ClassSkillsTests
{
    private const int Cleric = 17;
    private const int Ranger = 14;
    private const int Chanter = 18;
    private const int ClericSkill = 17_010_000;   // Earth's Retribution
    private const int ClericSkill2 = 17_020_000;  // Thunder and Lightning
    private const int ClericHeal = 17_120_000;    // in healing_skill_ids.json
    private const int ClericHeal2 = 17_100_000;   // in healing_skill_ids.json too
    private const int RangerSkill = 14_010_000;   // Deadshot
    private const int RangerSkill2 = 14_020_000;  // Snipe
    private const int ChanterSkill = 18_010_000;  // Onslaught
    private const int ChanterSkill2 = 18_020_000; // Resonance Crush
    private const int GladiatorSkill = 11_010_000;  // Rending Blow
    private const int GladiatorSkill2 = 11_020_000; // Keen Strike
    private const int SpiritmasterSkill = 16_010_000;
    private const int TargetId = 900;
    private const int PlayerId = 4321;

    [Theory]
    [InlineData(17_010_000, 17)]
    [InlineData(14_010_000, 14)]
    [InlineData(11_000_000, 11)]
    [InlineData(19_999_999, 19)]
    [InlineData(10_999_999, 0)]  // the spirit's, and effects any class has
    [InlineData(20_000_000, 0)]
    [InlineData(3_000_123, 0)]   // a theostone, which takes the player's class
    [InlineData(1101, 0)]
    [InlineData(0, 0)]
    [InlineData(-17_010_000, 0)]
    public void ASkillsClassIsItsFirstTwoDigitsInUpstreamsRangeOfPlayerSkills(long skillId, int classId) =>
        Assert.Equal(classId, ClassSkills.ClassOf(skillId));

    [Fact]
    public void TheClassWhoseSkillsDidMostOfTheDamageIsTheClass()
    {
        Assert.Equal(Cleric, ClassSkills.ClassFor(Ranger, [Skill(ClericSkill, 9_000), Skill(ClericSkill + 10_000, 1_000)]));
        Assert.Equal(Cleric, ClassSkills.ClassFor(0, [Skill(ClericSkill, 9_000)]));            // upstream had none
        Assert.Equal(16, ClassSkills.ClassFor(10, [Skill(SpiritmasterSkill, 500)]));             // upstream had the spirit's
        // A few hits with another class's skills, as players make, change nothing.
        Assert.Equal(18, ClassSkills.ClassFor(18, [Skill(ChanterSkill, 97_000), Skill(GladiatorSkill, 3_000)]));
        Assert.Equal(18, ClassSkills.ClassFor(0, [Skill(GladiatorSkill, 3_000), Skill(ChanterSkill, 97_000)]));
    }

    [Fact]
    public void UpstreamsClassStandsWhenTheSkillsDoNotSayOtherwise()
    {
        Assert.Equal(Ranger, ClassSkills.ClassFor(Ranger, null));
        Assert.Equal(Ranger, ClassSkills.ClassFor(Ranger, []));
        Assert.Equal(Ranger, ClassSkills.ClassFor(Ranger, [Skill(1101, 5_000), Skill(3_000_123, 5_000), Skill(10_000_001, 5_000)]));
        Assert.Equal(16, ClassSkills.ClassFor(16, [Skill(10_000_001, 90_000), Skill(SpiritmasterSkill, 1)])); // the spirit's never counts
        Assert.Equal(Ranger, ClassSkills.ClassFor(Ranger, [Skill(ClericSkill, 4_000), Skill(RangerSkill, 4_000)])); // ties
        Assert.Equal(Cleric, ClassSkills.ClassFor(Cleric, [Skill(RangerSkill, 4_000), Skill(ClericSkill, 4_000)]));
        Assert.Equal(0, ClassSkills.ClassFor(0, [Skill(GladiatorSkill, 0)]));
    }

    [Fact]
    public void DamagedSkillRowsCountForNothingAndSumsSaturate()
    {
        Assert.Equal(Ranger, ClassSkills.ClassFor(Ranger, [null, Skill(ClericSkill, -9_000), Skill(ClericSkill, 0)]));
        Assert.Equal(Cleric, ClassSkills.ClassFor(Ranger,
            [Skill(ClericSkill, long.MaxValue), Skill(ClericSkill + 10_000, long.MaxValue), Skill(RangerSkill, long.MaxValue - 1)]));
    }

    [Theory]
    [InlineData(4u)]
    [InlineData(5u)]
    [InlineData(0x17u)]
    public void ADamageRecordGivesItsActorAndSkill(uint switchValue)
    {
        Assert.True(ClassSkillTap.TryReadHit(Damage(TargetId, PlayerId, ClericSkill, switchValue: switchValue), out var actor, out var skill));
        Assert.Equal((PlayerId, ClericSkill), (actor, skill));
    }

    [Theory]
    [InlineData(3u)]    // upstream's checks: the low four bits 4 to 7, at most 255
    [InlineData(8u)]
    [InlineData(0x104u)]
    public void ARecordUpstreamTurnsAwayGivesNoHit(uint switchValue)
    {
        Assert.False(ClassSkillTap.TryReadHit(Damage(TargetId, PlayerId, ClericSkill, switchValue: switchValue), out _, out _));
        Assert.False(ClassSkillTap.TryReadHit(Damage(PlayerId, PlayerId, ClericSkill), out _, out _)); // the actor is the target
        Assert.False(ClassSkillTap.TryReadHit(MapLoad(20), out _, out _));
        Assert.False(ClassSkillTap.TryReadHit([], out _, out _));

        // A record of another kind shaped like a damage record (its length varint is one byte here).
        var whole = Damage(TargetId, PlayerId, ClericSkill);
        Assert.Equal((byte)0x04, whole[1]);
        Assert.False(ClassSkillTap.TryReadHit([whole[0], 0x05, .. whole[2..]], out _, out _));
    }

    // Upstream drops a record that does not read whole (DamagePacketProcessor throws), so it counts for nothing here.
    [Fact]
    public void ARecordCountsOnlyWhenItReadsWholeAsUpstreamReadsIt()
    {
        var whole = Damage(TargetId, PlayerId, ClericSkill); // after the skill: a byte, the type, 8, a varint, the damage (2)
        Assert.True(ClassSkillTap.TryReadHit(whole, out _, out _));
        Assert.False(ClassSkillTap.TryReadHit(whole[..^1], out _, out _));  // the damage cut
        Assert.False(ClassSkillTap.TryReadHit(whole[..^3], out _, out _));  // no damage, no varint before it
        Assert.False(ClassSkillTap.TryReadHit(whole[..^13], out _, out _)); // nothing after the skill
        Assert.False(ClassSkillTap.TryReadHit(whole[..^14], out _, out _)); // the skill's last byte missing
        // Switch value 4's block is eight bytes; 5 to 7's needs twelve bytes left, of which upstream reads eleven.
        Assert.False(ClassSkillTap.TryReadHit(Damage(TargetId, PlayerId, ClericSkill, specialLength: 7), out _, out _));
        Assert.False(ClassSkillTap.TryReadHit(Damage(TargetId, PlayerId, ClericSkill, switchValue: 5, specialLength: 8), out _, out _));
        Assert.True(ClassSkillTap.TryReadHit(Damage(TargetId, PlayerId, ClericSkill, switchValue: 5, specialLength: 11), out _, out _));
        // The damage as upstream reads it, five bytes whatever they hold: one past 32 bits, which upstream wraps, still reads.
        Assert.True(ClassSkillTap.TryReadHit([.. whole[..^2], 0xFF, 0xFF, 0xFF, 0xFF, 0x7F], out _, out _));
        Assert.False(ClassSkillTap.TryReadHit([.. whole[..^2], 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01], out _, out _));
    }

    [Fact]
    public void AReusedIdTakesItsNewPlayersClassAtTheirSecondClassSkill()
    {
        var (entities, _, tap) = Tap();
        var player = entities.GetOrCreateSessionPlayer(PlayerId, ClassById(Ranger)); // upstream, at a ranger's first hit
        Read(tap, RangerSkill, RangerSkill2, RangerSkill);
        entities.SetSessionPlayerName(PlayerId, "Synthia", 3_000); // the id's new player, named; upstream keeps the class

        Read(tap, ClericSkill, ClericSkill, ClericSkill); // one skill on three targets: one use, as many as the class held
        Assert.Equal(Ranger, ClassOf(player));
        Read(tap, ClericSkill2);
        Assert.Equal(Cleric, ClassOf(player));
        Assert.Same(GameDataProvider.Instance.Classes.GetById(Cleric), player.CharacterClass); // upstream's own class object
    }

    [Fact]
    public void OneSkillOfAnotherClassOnAnyNumberOfTargetsChangesNothing()
    {
        var (entities, _, tap) = Tap();
        var player = entities.GetOrCreateSessionPlayer(PlayerId, ClassById(Chanter));
        Read(tap, ChanterSkill, ChanterSkill2, ChanterSkill, GladiatorSkill, GladiatorSkill);
        Assert.Equal(Chanter, ClassOf(player));

        entities.SetSessionPlayerName(PlayerId, "Synthia", 3_000); // just named: the count starts again
        Read(tap, GladiatorSkill, GladiatorSkill, GladiatorSkill, GladiatorSkill, GladiatorSkill);
        Assert.Equal(Chanter, ClassOf(player));
        Read(tap, ChanterSkill, GladiatorSkill);
        Assert.Equal(Chanter, ClassOf(player));
    }

    // The least that moves a class wrongly, as documented: two other skills of one class in a row right after a name
    // change; two of the player's own then move it back.
    [Fact]
    public void TwoSkillsOfAnotherClassRightAfterANameMoveItAndTwoOfItsOwnMoveItBack()
    {
        var (entities, _, tap) = Tap();
        var player = entities.GetOrCreateSessionPlayer(PlayerId, ClassById(Chanter));
        Read(tap, ChanterSkill, ChanterSkill2);
        entities.SetSessionPlayerName(PlayerId, "Synthia", 3_000);
        Read(tap, GladiatorSkill, GladiatorSkill2);
        Assert.Equal(11, ClassOf(player));
        Read(tap, ChanterSkill);
        Assert.Equal(11, ClassOf(player));
        Read(tap, ChanterSkill2);
        Assert.Equal(Chanter, ClassOf(player));
    }

    // A name ends the run of hits too: the first skill after it is a use even when it was the last one before.
    [Fact]
    public void TheFirstSkillAfterANameCountsEvenWhenItWasTheLastBefore()
    {
        var (entities, _, tap) = Tap();
        var player = entities.GetOrCreateSessionPlayer(PlayerId, ClassById(Chanter));
        Read(tap, ChanterSkill2, ChanterSkill);
        entities.SetSessionPlayerName(PlayerId, "Synthia", 3_000);
        Read(tap, ChanterSkill, GladiatorSkill, GladiatorSkill2);
        Assert.Equal(Chanter, ClassOf(player));
    }

    [Fact]
    public void WithoutANewNameTheOtherClassNeedsMoreUsesThanTheClassHeld()
    {
        var (entities, _, tap) = Tap();
        var player = entities.GetOrCreateSessionPlayer(PlayerId, ClassById(Ranger));
        Read(tap, RangerSkill, RangerSkill2, RangerSkill); // 4 with the class held's head start
        Read(tap, ClericSkill, ClericSkill2, ClericSkill, ClericSkill2);
        Assert.Equal(Ranger, ClassOf(player));
        Read(tap, ClericSkill);
        Assert.Equal(Cleric, ClassOf(player));
    }

    [Fact]
    public void ASpiritsClassGivesWayAtTheFirstSkillOfAClass()
    {
        var (entities, _, tap) = Tap();
        var player = entities.GetOrCreateSessionPlayer(PlayerId, ClassById(10));
        Read(tap, 10_000_001, 10_000_002, 10_000_001);
        Assert.Equal(10, ClassOf(player));
        Read(tap, SpiritmasterSkill);
        Assert.Equal(16, ClassOf(player));
    }

    [Fact]
    public void HealsTheostonesAndPlayersUpstreamDoesNotTrackAreLeftAlone()
    {
        var (entities, _, tap) = Tap();
        var player = entities.GetOrCreateSessionPlayer(PlayerId, ClassById(Ranger));
        entities.SetSessionPlayerName(PlayerId, "Synthia", 3_000);
        Read(tap, ClericHeal, ClericHeal2, ClericHeal);
        Read(tap, 3_000_123, 3_000_124, 3_000_123);
        Assert.Equal(Ranger, ClassOf(player));

        tap.Read(Damage(TargetId, 77, ClericSkill), PacketOpcodes.Damage);
        Assert.Null(entities.GetPlayerEntity(77));
    }

    [Fact]
    public void NothingIsReadWhileTheMeterIsPausedInTheAbyss()
    {
        var (entities, pause, tap) = Tap();
        var player = entities.GetOrCreateSessionPlayer(PlayerId, ClassById(Ranger));
        entities.SetSessionPlayerName(PlayerId, "Synthia", 3_000);
        pause.Paused = true;
        Read(tap, ClericSkill, ClericSkill2, ClericSkill, ClericSkill2);
        Assert.Equal(Ranger, ClassOf(player));

        pause.Paused = false; // what was not read is not counted
        Read(tap, ClericSkill);
        Assert.Equal(Ranger, ClassOf(player));
        Read(tap, ClericSkill2);
        Assert.Equal(Cleric, ClassOf(player));
    }

    [Fact]
    public void AChangeIsLoggedWithTheIdAndClassesNeverTheName()
    {
        var logger = new RecordingLogger<ClassSkillTap>();
        var (entities, _, tap) = Tap(logger);
        entities.GetOrCreateSessionPlayer(PlayerId, ClassById(Ranger));
        entities.SetSessionPlayerName(PlayerId, "Synthia", 3_000);
        Read(tap, ClericSkill, ClericSkill2, ClericSkill, ClericSkill2);
        var (level, message) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, level);
        Assert.Equal($"Player {PlayerId} is now class {Cleric}, as most of their skills say; upstream had {Ranger}.", message);
    }

    // Through upstream's real pipeline: a ranger's hit makes upstream track the id as a Ranger, the id's new player is
    // named, and their Cleric skills move the class, so the overlay's row shows a Cleric.
    [Fact]
    public void TheOverlayShowsAReusedIdsNewPlayerWithTheirOwnClass()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var combat = new SyntheticCombat(host);
        var stream = host.Services.GetRequiredService<TcpStreamBuffer>();
        combat.Target(TargetId, SyntheticCombat.AultrossCode, hp: 1_000_000_000);
        Synchronise(stream);

        stream.AddData("synthetic", Damage(TargetId, PlayerId, RangerSkill), 0);
        Assert.Equal(Ranger, ClassOf(combat.Entities.GetPlayerEntity(PlayerId)));
        combat.Entities.SetSessionPlayerName(PlayerId, "Synthia", 3_000);
        stream.AddData("synthetic",
            [.. Damage(TargetId, PlayerId, ClericSkill), .. Damage(TargetId, PlayerId, ClericSkill), .. Damage(TargetId, PlayerId, ClericSkill2)], 0);

        var row = Assert.Single(combat.Engine.GetSnapshot().Players);
        Assert.Equal(((long)PlayerId, "Synthia", Cleric, "Cleric"), (row.Id, row.Name, row.ClassId, row.ClassName));
        Assert.Equal(4_000, row.TotalDamage); // upstream counted all four hits
    }

    // A fight saved with upstream's stale class reads with the class the player's saved skills say, in the rows and the
    // details; a player whose skills name no class keeps upstream's. The live details say the same before the save.
    [Fact]
    public void ASavedFightReadsWithTheClassThePlayersSkillsSay()
    {
        using var host = new TestEngineHost(CaptureMode.None);
        var combat = new SyntheticCombat(host);
        var alpha = combat.Player(7, "Alpha", SyntheticCombat.Class(11, "Gladiator"), isUser: true);
        var beta = combat.Player(8, "Beta", SyntheticCombat.Class(Ranger, "Ranger"));
        var mob = combat.Target(TargetId, SyntheticCombat.AultrossCode, hp: 1_000_000_000);
        var start = new DateTime(2026, 11, 2, 18, 0, 0, DateTimeKind.Local);
        for (var s = 0; s < 10; s++)
        {
            combat.Hit(alpha, mob, start.AddSeconds(s), 1_000, SyntheticCombat.Skill(1101, "Strike"));
            combat.Hit(beta, mob, start.AddSeconds(s).AddMilliseconds(500), 800, SyntheticCombat.Skill(ClericSkill, "Earth's Retribution"));
        }

        Assert.Equal((Cleric, "Cleric"), (combat.Engine.GetPlayerDetails(8)!.Player.ClassId, combat.Engine.GetPlayerDetails(8)!.Player.ClassName));
        combat.CompleteAndSave(start.AddSeconds(11));

        var entry = Assert.Single(combat.Engine.GetHistory(new HistoryQuery()).Entries);
        var fight = combat.Engine.GetHistoryFight(entry.FightId)!;
        Assert.Equal([(7L, 11, "Gladiator"), (8L, Cleric, "Cleric")],
            fight.Players.OrderBy(p => p.Id).Select(p => (p.Id, p.ClassId, p.ClassName)));
        Assert.Equal((Cleric, "Cleric"), (fight.Details[8].Player.ClassId, fight.Details[8].Player.ClassName));
    }

    // A saved fight whose start or end is damaged opens with its rows only (no details), and those rows still take the
    // class from the players' saved skills.
    [Fact]
    public void AFightWithoutDetailsStillReadsItsPlayersClassesFromTheirSkills()
    {
        var snapshot = new HistorySessionSnapshot
        {
            SessionId = Guid.NewGuid(),
            PlayerStats = [new PlayerStats { PlayerId = 8, PlayerName = "Beta", IsIdentified = true, ClassId = Ranger, ClassName = "Ranger", TotalDamage = 8_000 }],
            SkillStatsByPlayer = new Dictionary<long, IReadOnlyCollection<SkillStats>> { [8] = [Skill(ClericSkill, 8_000)] },
        };
        var fight = UpstreamMapping.ToHistoryFight(snapshot, includeDetails: false);
        Assert.Empty(fight.Details);
        var row = Assert.Single(fight.Players);
        Assert.Equal((Cleric, "Cleric"), (row.ClassId, row.ClassName));
    }

    private static (EntityTracker Entities, PauseState Pause, ClassSkillTap Tap) Tap(ILogger<ClassSkillTap>? logger = null)
    {
        var entities = new EntityTracker();
        var pause = new PauseState();
        return (entities, pause, new ClassSkillTap(entities, pause, logger ?? new RecordingLogger<ClassSkillTap>()));
    }

    // One damage record from the player for each skill code, in order.
    private static void Read(ClassSkillTap tap, params int[] skillCodes)
    {
        foreach (var skillCode in skillCodes) tap.Read(Damage(TargetId, PlayerId, skillCode), PacketOpcodes.Damage);
    }

    private static CharacterClass ClassById(int id) => GameDataProvider.Instance.Classes.GetById(id)!;

    private static int ClassOf(Player? player) => (int)(player?.CharacterClass?.Id ?? 0);

    private static SkillStats Skill(long id, long damage) => new() { SkillId = id, TotalDamage = damage };
}
