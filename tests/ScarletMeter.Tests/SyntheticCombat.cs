using AionDpsMeter.Core.Models;
using AionDpsMeter.Services.Services.Entity;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Session.Persistence;
using Microsoft.Extensions.DependencyInjection;
using ScarletMeter.Engine;

namespace ScarletMeter.Tests;

/// <summary>
/// Synthetic fight data fed straight to upstream's session manager, as its packet processors would feed it.
/// Names, ids, icons and numbers are invented; mob codes are real bosses from upstream's mobs.json where a
/// test needs a fight saved to history (upstream saves boss fights only).
/// </summary>
internal sealed class SyntheticCombat(TestEngineHost host)
{
    /// <summary>Aultross and Fediv Wraith in upstream's mobs.json.</summary>
    public const int AultrossCode = 2_090_232;
    public const int FedivWraithCode = 2_090_175;

    public ICombatEngine Engine { get; } = host.Services.GetRequiredService<ICombatEngine>();

    public EntityTracker Entities { get; } = host.Services.GetRequiredService<EntityTracker>();

    public CombatSessionManager Sessions { get; } = host.Services.GetRequiredService<CombatSessionManager>();

    public ICombatHistoryStore History { get; } = host.Services.GetRequiredService<ICombatHistoryStore>();

    // Upstream models are mutable, so each call builds a fresh instance.
    public static CharacterClass Class(int id, string name) => new() { Id = id, Name = name, Icon = $"synthetic/class-{id}.png" };

    public static Skill Skill(int id, string name, bool[]? specialization = null) =>
        new() { Id = id, Name = name, Icon = $"synthetic/skill-{id}.png", SpecializationFlags = specialization ?? [] };

    /// <summary>A named party member, as the player-info decoders register one.</summary>
    public Player Player(int id, string name, CharacterClass characterClass, bool isUser = false)
    {
        var player = Entities.GetOrCreateSessionPlayer(id, characterClass);
        Entities.SetSessionPlayerName(id, name, combatPower: 3_000, serverName: "Synthetic", isUser: isUser);
        return player;
    }

    /// <summary>A target at full HP, as the mob-spawn and RemainHp decoders set one up.</summary>
    public Mob Target(int entityId, int mobCode, long hp)
    {
        var mob = Entities.CreateOrUpdateTargetEntity(entityId, mobCode, hp);
        Entities.UpdateTargetEntityHpCurrent(entityId, hp);
        return mob;
    }

    public void Hit(Player source, Mob target, DateTime at, long damage, Skill skill, HitFlags flags = HitFlags.None,
        bool dot = false, CharacterClass? characterClass = null) =>
        Sessions.ProcessDamageEvent(new PlayerDamage
        {
            DateTime = at,
            SourceEntity = source,
            TargetEntity = target,
            Damage = damage,
            Skill = skill,
            CharacterClass = characterClass ?? source.CharacterClass!,
            IsCritical = flags.HasFlag(HitFlags.Critical),
            IsBackAttack = flags.HasFlag(HitFlags.BackAttack),
            IsFrontAttack = flags.HasFlag(HitFlags.FrontAttack),
            IsPerfect = flags.HasFlag(HitFlags.Perfect),
            IsDoubleDamage = flags.HasFlag(HitFlags.DoubleDamage),
            IsParry = flags.HasFlag(HitFlags.Parry),
            IsDot = dot,
        });

    public void Buff(int entityId, int buffId, string name, DateTime at, TimeSpan duration) =>
        Sessions.ProcessBuffEvent(new BuffEvent
        {
            EntityId = entityId,
            BuffId = buffId,
            BuffName = name,
            BuffIcon = $"synthetic/buff-{buffId}.png",
            DurationMs = (uint)duration.TotalMilliseconds,
            AppliedAt = at,
            CasterId = entityId,
        });

    /// <summary>Ends every open fight and saves the boss fights, the path the demo and the engine's shutdown use.</summary>
    public void CompleteAndSave(DateTime at)
    {
        Sessions.CompleteAndPersistActiveSessions(at);
        History.FlushPendingSaves();
    }
}
