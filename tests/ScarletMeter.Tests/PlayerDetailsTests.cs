using AionDpsMeter.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using ScarletMeter.Engine;

namespace ScarletMeter.Tests;

/// <summary>
/// ICombatEngine.GetPlayerDetails over synthetic hits fed to upstream's real session manager: every figure
/// below is worked out by hand from the hits, so a mapping slip (a swapped rate, a lost DoT rule, the wrong
/// time origin) shows as a wrong number.
/// </summary>
public sealed class PlayerDetailsTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Local);
    private const int AlphaId = 7;
    private const int BetaId = 8;

    private readonly TestEngineHost host = new(CaptureMode.None);
    private readonly SyntheticCombat combat;

    public PlayerDetailsTests()
    {
        combat = new SyntheticCombat(host);
    }

    // The fight: Beta opens at T0; Alpha hits from T0+1 s to T0+6 s with skills A and B, one of B's hits a DoT tick.
    //   Alpha, direct hits (5): every one critical, 4 back, 3 perfect, 2 double damage, 1 front, none parried,
    //   so each rate is distinct (100/80/60/40/20/0). The DoT tick is flagged parried and must not count.
    //   Skill A: 1000 + 3000 + 2000 = 6000 in 3 hits; skill B: 2000 + 500 (tick) + 1500 = 4000 in 3 hits.
    //   Buffs on Alpha: X twice for 1 s (T0..T0+1, T0+3..T0+4) = 2 s over 2 applications; Y once from T0+1 for
    //   10 s, cut at the fight's last hit (T0+6) = 5 s. Upstream orders buffs by applications (X first); by
    //   uptime Y comes first.
    private void PlayFight()
    {
        var gladiator = SyntheticCombat.Class(11, "Gladiator");
        var alpha = combat.Player(AlphaId, "Alpha", gladiator, isUser: true);
        var beta = combat.Player(BetaId, "Beta", SyntheticCombat.Class(17, "Cleric"));
        var mob = combat.Target(900, mobCode: 0, hp: 10_000_000);
        var a = SyntheticCombat.Skill(1101, "Skill A", [true, false, true, false, false]);
        var b = SyntheticCombat.Skill(1102, "Skill B");

        combat.Hit(beta, mob, T0, 2500, SyntheticCombat.Skill(1701, "Skill C"));
        // Buffs once the fight exists; upstream replays only buffs applied before a fight's first hit.
        combat.Buff(AlphaId, 500, "Buff X", T0, TimeSpan.FromSeconds(1));
        combat.Buff(AlphaId, 600, "Buff Y", T0.AddSeconds(1), TimeSpan.FromSeconds(10));
        combat.Buff(AlphaId, 500, "Buff X", T0.AddSeconds(3), TimeSpan.FromSeconds(1));
        combat.Buff(BetaId, 700, "Beta's buff", T0, TimeSpan.FromSeconds(5));

        const HitFlags crit = HitFlags.Critical;
        combat.Hit(alpha, mob, T0.AddSeconds(1), 1000, a, crit | HitFlags.BackAttack | HitFlags.Perfect | HitFlags.DoubleDamage | HitFlags.FrontAttack);
        combat.Hit(alpha, mob, T0.AddSeconds(2), 3000, a, crit | HitFlags.BackAttack | HitFlags.Perfect | HitFlags.DoubleDamage);
        combat.Hit(alpha, mob, T0.AddSeconds(3), 2000, b, crit | HitFlags.BackAttack | HitFlags.Perfect);
        combat.Hit(alpha, mob, T0.AddSeconds(4), 500, b, HitFlags.Parry, dot: true);
        combat.Hit(alpha, mob, T0.AddSeconds(5), 2000, a, crit | HitFlags.BackAttack);
        combat.Hit(alpha, mob, T0.AddSeconds(6), 1500, b, crit);
    }

    [Fact]
    public void PlayerTotalsRatesAndDurationsMapUpstreamsFigures()
    {
        PlayFight();
        var details = Assert.IsType<PlayerDetails>(combat.Engine.GetPlayerDetails(AlphaId));

        Assert.Equal("Alpha", details.Player.Name);
        Assert.True(details.Player.IsUser);
        Assert.Equal(10_000, details.Player.TotalDamage);
        Assert.Equal(80, details.Player.DamageShare, 6);        // of 12,500
        Assert.Equal(2_000, details.Player.Dps, 6);              // over Alpha's own 5 s
        Assert.Equal("synthetic/class-11.png", details.Player.ClassIcon); // upstream's string, unchanged
        Assert.Equal(TimeSpan.FromSeconds(6), details.Duration); // the fight's, from Beta's opening hit

        var rates = details.Rates;
        Assert.Equal(5, rates.Hits);
        Assert.Equal(
            [100.0, 80, 20, 60, 40, 0],
            new[] { rates.Critical, rates.BackAttack, rates.FrontAttack, rates.Perfect, rates.DoubleDamage, rates.Parry }.Select(r => Math.Round(r, 9)));
    }

    [Fact]
    public void SkillsCarryTotalsSharesHitRangeAndRates()
    {
        PlayFight();
        var details = combat.Engine.GetPlayerDetails(AlphaId)!;

        Assert.Equal(["Skill A", "Skill B"], details.Skills.Select(s => s.Name)); // by damage
        var a = details.Skills[0];
        Assert.Equal(1101, a.SkillId);
        Assert.Equal("synthetic/skill-1101.png", a.Icon);
        Assert.Equal([true, false, true, false, false], a.SpecializationFlags);
        Assert.False(a.IsDot);
        Assert.False(a.IsSummonGroup);
        Assert.Empty(a.SummonChildren);
        Assert.Equal(6_000, a.TotalDamage);
        Assert.Equal(60, a.DamageShare, 6);
        Assert.Equal(1_200, a.Dps, 6);
        Assert.Equal(3, a.Hits);
        Assert.Equal(3_000, a.MaxHit);
        Assert.Equal(1_000, a.MinHit);
        Assert.Equal(2_000, a.AverageHit, 6);
        Assert.Equal(3, a.Rates.Hits);
        Assert.Equal(100, a.Rates.Critical, 6);
        Assert.Equal(100, a.Rates.BackAttack, 6);
        Assert.Equal(200.0 / 3, a.Rates.Perfect, 6);
        Assert.Equal(200.0 / 3, a.Rates.DoubleDamage, 6);
        Assert.Equal(100.0 / 3, a.Rates.FrontAttack, 6);

        // Upstream counts the DoT tick in a skill's hits but not in its flags.
        var b = details.Skills[1];
        Assert.Equal(4_000, b.TotalDamage);
        Assert.Equal(3, b.Hits);
        Assert.Equal(500, b.MinHit);
        Assert.False(b.IsDot); // not every hit was a tick
        Assert.Equal(200.0 / 3, b.Rates.Critical, 6);
        Assert.Equal(0, b.Rates.Parry);
        Assert.Empty(b.SpecializationFlags);
    }

    [Fact]
    public void BuffsAreTheReceiversOwnByUptimeWithinTheFight()
    {
        PlayFight();
        var details = combat.Engine.GetPlayerDetails(AlphaId)!;

        Assert.Equal(["Buff Y", "Buff X"], details.Buffs.Select(b => b.Name)); // Beta's buff is not Alpha's
        var y = details.Buffs[0];
        Assert.Equal(600, y.BuffId);
        Assert.Equal("synthetic/buff-600.png", y.Icon);
        Assert.Equal(1, y.Applications);
        Assert.Equal(TimeSpan.FromSeconds(5), y.Uptime);
        Assert.Equal(500.0 / 6, y.UptimePercent, 6);
        var x = details.Buffs[1];
        Assert.Equal(2, x.Applications);
        Assert.Equal(TimeSpan.FromSeconds(2), x.Uptime);
        Assert.Equal(200.0 / 6, x.UptimePercent, 6);
    }

    [Fact]
    public void GraphCountsSecondsFromThePlayersFirstHit()
    {
        PlayFight();
        var graph = combat.Engine.GetPlayerDetails(AlphaId)!.Graph;

        Assert.Equal(5, graph.TotalSeconds, 6);
        Assert.Equal([0.0, 1, 2, 3, 4, 5], graph.Points.Select(p => p.Second));
        // Per-second damage 1000, 3000, 2000, 500, 2000, 1500, smoothed over two seconds either side.
        Assert.Equal(2_000, graph.Points[0].Damage);  // (1000 + 3000 + 2000) / 3
        Assert.Equal(1_700, graph.Points[2].Damage);  // (1000 + 3000 + 2000 + 500 + 2000) / 5
        Assert.Equal(1_000, graph.Points[0].CumulativeDps, 6);
        Assert.Equal(2_000, graph.Points[^1].CumulativeDps, 6);
        // Buff lanes on the same axis: X's first application ended at Alpha's first hit, so only its second shows.
        Assert.Equal(
            [(600, 0.0, 5.0, 1), (500, 2.0, 3.0, 1)],
            graph.Buffs.Select(span => (span.BuffId, span.StartSecond, span.EndSecond, span.Count)));
    }

    [Fact]
    public void CombatLogIsNewestFirstAndTimedFromTheFightsStart()
    {
        PlayFight();
        var log = combat.Engine.GetPlayerDetails(AlphaId)!.CombatLog;

        Assert.Equal([1500L, 2000, 500, 2000, 3000, 1000], log.Select(entry => entry.Damage));
        Assert.Equal([6.0, 5, 4, 3, 2, 1], log.Select(entry => entry.Second));
        var tick = log[2];
        Assert.True(tick.IsDot);
        Assert.Equal(HitFlags.Parry, tick.Flags);
        Assert.Equal(1102, tick.SkillId);
        Assert.Equal("Skill B", tick.SkillName);
        Assert.Equal("synthetic/skill-1102.png", tick.Icon);
        Assert.Null(tick.SummonName);
        Assert.Equal(HitFlags.Critical | HitFlags.BackAttack | HitFlags.Perfect | HitFlags.DoubleDamage | HitFlags.FrontAttack, log[^1].Flags);
        Assert.Equal(T0.AddSeconds(1), log[^1].Time);
        Assert.Equal(DateTimeKind.Local, log[^1].Time.Kind);
    }

    [Fact]
    public void CombatLogKeepsTheNewestThousandHitsWhileSkillsCountThemAll()
    {
        var alpha = combat.Player(AlphaId, "Alpha", SyntheticCombat.Class(11, "Gladiator"), isUser: true);
        var mob = combat.Target(900, mobCode: 0, hp: 10_000_000);
        var skill = SyntheticCombat.Skill(1101, "Skill A");
        const int hits = PlayerDetails.CombatLogLimit + 5;
        for (var i = 0; i < hits; i++)
            combat.Hit(alpha, mob, T0.AddMilliseconds(100 * i), damage: i + 1, skill);

        var details = combat.Engine.GetPlayerDetails(AlphaId)!;
        Assert.Equal(PlayerDetails.CombatLogLimit, details.CombatLog.Count);
        Assert.Equal(hits, details.CombatLog[0].Damage);     // the newest hit
        Assert.Equal(6, details.CombatLog[^1].Damage);       // the five oldest are dropped
        Assert.Equal(hits, Assert.Single(details.Skills).Hits);
        Assert.Equal(hits, details.Rates.Hits);
    }

    [Fact]
    public void SummonDamageIsGroupedUnderItsOwnerSkill()
    {
        // The owner's own skill, and a summon whose hits carry the owner's summoning skill (an owner-class
        // code, which upstream takes as the group's owner skill) and the summon's basic attack.
        var elementalist = SyntheticCombat.Class(16, "Elementalist");
        var spirit = SyntheticCombat.Class(10, "Elementalist spirit");
        var owner = combat.Player(AlphaId, "Alpha", elementalist, isUser: true);
        combat.Entities.RegisterSummon(55, AlphaId);
        var pet = combat.Entities.GetOrCreateSessionPlayer(55, spirit);
        pet.Name = "Spirit 55";
        var mob = combat.Target(900, mobCode: 0, hp: 10_000_000);
        var summoning = SyntheticCombat.Skill(1610, "Summon: Spirit");
        var basic = SyntheticCombat.Skill(1001, "Spirit: Basic Attack");

        // A second summon with one skill only: upstream folds it as a group of one, which is not a group.
        combat.Entities.RegisterSummon(56, AlphaId);
        var lone = combat.Entities.GetOrCreateSessionPlayer(56, spirit);
        var loneSkill = SyntheticCombat.Skill(1620, "Summon: Lone Spirit");

        combat.Hit(owner, mob, T0, 5_000, SyntheticCombat.Skill(1601, "Owner's Skill"));
        combat.Hit(pet, mob, T0.AddSeconds(1), 1_000, basic, characterClass: spirit);
        combat.Hit(pet, mob, T0.AddSeconds(2), 3_000, summoning, characterClass: elementalist);
        combat.Hit(pet, mob, T0.AddSeconds(3), 1_000, basic, HitFlags.Critical, characterClass: spirit);
        combat.Hit(lone, mob, T0.AddSeconds(4), 400, loneSkill, characterClass: elementalist);

        var details = combat.Engine.GetPlayerDetails(AlphaId)!;
        Assert.Equal(10_400, details.Player.TotalDamage); // upstream credits the summons' damage to their owner
        var single = Assert.Single(details.Skills, s => s.Name == "Summon: Lone Spirit");
        Assert.False(single.IsSummonGroup);
        Assert.Empty(single.SummonChildren); // children appear only with a group, as in upstream's own view
        var group = Assert.Single(details.Skills, s => s.IsSummonGroup);
        Assert.Equal("Summon: Spirit", group.Name);
        Assert.Equal(5_000, group.TotalDamage);
        Assert.Equal(3, group.Hits);
        Assert.Equal(["Summon: Spirit", "Spirit: Basic Attack"], group.SummonChildren.Select(c => c.Name)); // by damage
        Assert.Equal(2_000, group.SummonChildren[1].TotalDamage);
        Assert.Equal(50, group.SummonChildren[1].Rates.Critical, 6);
        Assert.All(group.SummonChildren, child => Assert.Empty(child.SummonChildren));
        Assert.Equal(["Player_56", "Spirit 55", "Spirit 55", "Spirit 55", null], details.CombatLog.Select(entry => entry.SummonName));

        // Grouping off: the summons' skills stand on their own.
        host.Services.GetRequiredService<EngineSettings>().GroupSummonDamage = false;
        var ungrouped = combat.Engine.GetPlayerDetails(AlphaId)!;
        Assert.DoesNotContain(ungrouped.Skills, s => s.IsSummonGroup || s.SummonChildren.Count > 0);
        Assert.Equal(4, ungrouped.Skills.Count);
    }

    [Fact]
    public void NoDetailsWithoutDamageInTheActiveFight()
    {
        Assert.Null(combat.Engine.GetPlayerDetails(AlphaId)); // no fight at all

        PlayFight();
        Assert.Null(combat.Engine.GetPlayerDetails(99));       // not in this fight
        Assert.NotNull(combat.Engine.GetPlayerDetails(BetaId));

        combat.Engine.Reset();
        Assert.Null(combat.Engine.GetPlayerDetails(AlphaId));
    }

    public void Dispose() => host.Dispose();
}
