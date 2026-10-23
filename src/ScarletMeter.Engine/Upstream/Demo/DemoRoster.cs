namespace ScarletMeter.Engine.Upstream.Demo;

/// <summary>
/// One way a demo combatant attacks: a raw skill code as the game would send it (upstream normalises it and
/// resolves the name and icon), the damage of a direct hit and how often it is picked. A skill with
/// <see cref="DotTicks"/> also applies damage over time, one tick every <see cref="DemoRoster.DotTickSeconds"/>;
/// one with <see cref="DirectHit"/> false only ticks. Damage-over-time codes must be in upstream's
/// dot_skill_ids.json, as its DoT decoder requires.
/// </summary>
internal sealed record DemoSkill(int Code, long Damage, double Weight, int DotTicks = 0, long DotTickDamage = 0, bool DirectHit = true);

/// <summary>How likely a direct hit is to carry each flag, 0 to 1. Damage-over-time ticks carry none.</summary>
internal sealed record DemoHitProfile(double Critical, double BackAttack, double FrontAttack, double Perfect, double DoubleDamage, double Parry);

/// <summary>
/// A party member (<see cref="OwnerId"/> null, named) or a summon fighting for one (<see cref="OwnerId"/> set,
/// unnamed: the game never names summons, so upstream knows them by a placeholder). It casts every
/// <see cref="CastSeconds"/>, give or take a fifth.
/// </summary>
internal sealed record DemoCombatant(
    int EntityId,
    string? Name,
    int ClassId,
    int CombatPower,
    bool IsUser,
    int? OwnerId,
    double CastSeconds,
    DemoHitProfile Hits,
    IReadOnlyList<DemoSkill> Skills);

/// <summary>A buff a party member casts every <see cref="PeriodSeconds"/>, on the whole party or on itself.</summary>
internal sealed record DemoBuff(int Code, int CasterId, bool OnParty, double PeriodSeconds, double DurationSeconds);

/// <summary>
/// The demo's cast: four clearly synthetic players of different classes (the user among them), a summon, their
/// buffs and the bosses they fight. Skill, class and mob codes are real entries of upstream's game data
/// (skills.json, classes.json, mobs.json), so names and icons resolve as they would in a real fight;
/// DemoRosterTests guards that against a game-data update. Entity ids are arbitrary but distinct.
/// </summary>
internal static class DemoRoster
{
    /// <summary>The server name every demo player carries.</summary>
    public const string ServerName = "Demo";

    /// <summary>Seconds between damage-over-time ticks.</summary>
    public const double DotTickSeconds = 2;

    /// <summary>Entity id of the first boss; each fight spawns the next id, as the game spawns a new entity.</summary>
    public const int FirstBossEntityId = 47_001;

    private const int GladiatorId = 41_001;
    private const int ClericId = 41_002;
    private const int RangerId = 41_003;
    private const int ElementalistId = 41_004;

    private static readonly DemoHitProfile Melee = new(Critical: 0.45, BackAttack: 0.45, FrontAttack: 0.30, Perfect: 0.06, DoubleDamage: 0.04, Parry: 0.03);
    private static readonly DemoHitProfile Ranged = new(Critical: 0.40, BackAttack: 0.15, FrontAttack: 0.25, Perfect: 0.05, DoubleDamage: 0.05, Parry: 0.02);
    private static readonly DemoHitProfile Caster = new(Critical: 0.35, BackAttack: 0.10, FrontAttack: 0.20, Perfect: 0.08, DoubleDamage: 0.03, Parry: 0.01);

    /// <summary>The party, the user first. Class ids are from upstream's classes.json.</summary>
    public static IReadOnlyList<DemoCombatant> Party { get; } =
    [
        new(GladiatorId, "Demo Gladiator", ClassId: 11, CombatPower: 3_180, IsUser: true, OwnerId: null, CastSeconds: 0.9, Melee,
        [
            new(11020130, 260_000, 3),   // Keen Strike, raw code with specialisation slots 1 and 3
            new(11010000, 320_000, 2),   // Rending Blow
            new(11030000, 380_000, 1.5), // Rupture Strike
            new(11040000, 520_000, 1),   // Wrathful Strike
            new(11090000, 680_000, 0.5), // Blade Dance
        ]),
        new(ClericId, "Demo Cleric", ClassId: 17, CombatPower: 2_940, IsUser: false, OwnerId: null, CastSeconds: 1.2, Caster,
        [
            new(17020000, 210_000, 3),   // Thunder and Lightning
            new(17060000, 170_000, 3),   // Bolt
            new(17040000, 300_000, 2),   // Judgment Thunder
            new(17010000, 260_000, 1),   // Earth's Retribution
            new(17070000, 0, 1, DotTicks: 6, DotTickDamage: 90_000, DirectHit: false), // Chain of Torment, ticks only
        ]),
        new(RangerId, "Demo Ranger", ClassId: 14, CombatPower: 3_050, IsUser: false, OwnerId: null, CastSeconds: 0.85, Ranged,
        [
            new(14020000, 280_000, 3),   // Snipe
            new(14030000, 240_000, 2),   // Rapid Fire
            new(14010000, 360_000, 2),   // Deadshot
            new(14040000, 420_000, 1),   // Spiral Arrow
            new(14050000, 200_000, 1, DotTicks: 5, DotTickDamage: 70_000), // Drill Dart, a hit and then ticks
        ]),
        new(ElementalistId, "Demo Elementalist", ClassId: 16, CombatPower: 3_010, IsUser: false, OwnerId: null, CastSeconds: 1.1, Caster,
        [
            new(16010000, 250_000, 3),   // Cold Shock
            new(16040000, 300_000, 2),   // Combustion
            new(16020000, 340_000, 2),   // Vacuum Explosion
            new(16300000, 600_000, 0.5), // Elemental Fusion
        ]),
    ];

    /// <summary>
    /// The Elementalist's fire spirit. Its hits use the owner's summoning skill (an Elementalist skill code, which
    /// upstream treats as the owner skill of a summon group) and the spirit's basic attack (a spirit-class code),
    /// so upstream folds both under "Summon: Fire Spirit" when summon grouping is on.
    /// </summary>
    public static DemoCombatant Summon { get; } =
        new(41_101, Name: null, ClassId: 10, CombatPower: 0, IsUser: false, OwnerId: ElementalistId, CastSeconds: 1.4, Caster,
        [
            new(10001, 120_000, 4),      // Fire Spirit: Basic Attack (upstream normalises 10001 to 100011)
            new(16100000, 260_000, 1),   // Summon: Fire Spirit
        ]);

    /// <summary>Every combatant that deals damage: the party, then the summon.</summary>
    public static IReadOnlyList<DemoCombatant> Combatants { get; } = [.. Party, Summon];

    /// <summary>Buffs, cast at the start of each fight and then every period while it lasts.</summary>
    public static IReadOnlyList<DemoBuff> Buffs { get; } =
    [
        new(17430000, ClericId, OnParty: true, PeriodSeconds: 30, DurationSeconds: 20),       // Prayer of Amplification
        new(11250000, GladiatorId, OnParty: false, PeriodSeconds: 25, DurationSeconds: 12),   // Zikel's Blessing
        new(14220000, RangerId, OnParty: false, PeriodSeconds: 40, DurationSeconds: 30),      // Bow of Blessing
        new(16370000, ElementalistId, OnParty: true, PeriodSeconds: 45, DurationSeconds: 15), // Flame Blessing
    ];

    /// <summary>Boss mob codes from upstream's mobs.json (Aultross, Fediv Wraith, Transcendent Bakarma), fought in turn.</summary>
    public static IReadOnlyList<int> BossMobCodes { get; } = [2_090_232, 2_090_175, 2_090_428];
}
