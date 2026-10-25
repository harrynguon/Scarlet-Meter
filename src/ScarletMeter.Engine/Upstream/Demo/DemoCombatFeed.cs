using AionDpsMeter.Core.GameData.Services;
using AionDpsMeter.Core.Models;
using AionDpsMeter.Services.Services.Entity;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Session.Persistence;
using Microsoft.Extensions.Logging;
using ScarletMeter.Engine.Odyle;

namespace ScarletMeter.Engine.Upstream.Demo;

/// <summary>
/// The synthetic fights of <see cref="CaptureMode.Demo"/>. It plays <see cref="DemoRoster"/> against one boss
/// after another through upstream's public API, as upstream's packet processors would: party identities into
/// EntityTracker, the summon's ownership, damage, damage-over-time ticks and buffs into CombatSessionManager,
/// the boss's HP as the RemainHp decoder sets it, and ping. Each boss has HP for 70 to 82 seconds of the party's
/// expected damage; the fight ends when its HP runs out or after <see cref="MaxFightLength"/>, through
/// CompleteAndPersistActiveSessions, the same path that saves open fights when the meter closes (upstream itself
/// only ends a fight on a 45-second idle timeout or a respawn), so each fight becomes one history entry. The next
/// starts <see cref="BreakLength"/> later. Odyle energy, which upstream does not decode, comes as the game's own
/// records would, synthetic ones read by the engine's <see cref="OdyleEnergyTap"/> (a login list when the feed begins,
/// an update as each later fight starts and spends some). They go to the tap directly rather than through upstream's
/// stream reassembly, which synchronises a new stream only on a keep-alive left from an earlier chunk.
/// </summary>
/// <remarks>
/// Not thread-safe: one thread drives it, through <see cref="Begin"/> and then <see cref="Step"/> with the
/// current time. Time is a parameter, so tests can run a whole fight in milliseconds. Events are handled at the
/// time they were due, not the time of the step, so the step rate does not change the fight; each event
/// schedules its successor before it touches upstream, so one that throws does not stop its schedule. When the
/// clock jumps (sleep and resume, a time change), the schedule moves with it instead of replaying or waiting
/// out the gap (see <see cref="MaxClockJump"/>).
/// </remarks>
internal sealed class DemoCombatFeed
{
    /// <summary>Longest a demo fight lasts; the user lands the last blow if the boss is still alive.</summary>
    public static readonly TimeSpan MaxFightLength = TimeSpan.FromSeconds(90);

    /// <summary>Time between one fight's end and the next one's first event.</summary>
    public static readonly TimeSpan BreakLength = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How far the earliest scheduled event may lag behind the step's time, or run ahead of it, before the clock
    /// counts as having jumped. The ping is always due within a second, so in normal running the earliest event
    /// is never more than a second ahead, and with steps a tenth of a second apart never far behind. Past this,
    /// every event moves by the same amount so the earliest falls due now: a jump forward does not replay the
    /// gap in one step (an hour late would be 3,600 pings and dozens of fights), and a jump back does not
    /// freeze the demo until the clock catches up. Upstream sees a pause in the hits, and after 45 seconds of
    /// one ends the fight in progress and starts another on the same boss, as it would in a real fight.
    /// </summary>
    public static readonly TimeSpan MaxClockJump = TimeSpan.FromSeconds(5);

    // A fight's damage strays a few percent from the expectation (a few hundred hits of varying size), so HP for
    // 70 to 82 seconds of it keeps kills well inside 60 to 90 seconds: 69 to 84 seconds over 60 simulated fights.
    private const double MinTargetSeconds = 70;
    private const double MaxTargetSeconds = 82;
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(1);

    private readonly EntityTracker entities;
    private readonly CombatSessionManager sessions;
    private readonly ICombatHistoryStore history;
    private readonly ILogger logger;
    private readonly Random random;
    private readonly GameDataProvider gameData = GameDataProvider.Instance;
    private readonly PriorityQueue<DemoEvent, DateTime> schedule = new();
    private readonly HashSet<EventKind> failing = [];
    private readonly double expectedPartyDps;
    private readonly OdyleEnergyTap? odyleTap;
    private Fight? fight;
    private int fightsStarted;
    private bool partyRegistered;
    private OdyleEnergy odyle = new(StartingOdyle.Basic, StartingOdyle.Charged);

    // The demo's Odyle energy: where it starts, what a fight spends and what a recharge moves from charged to basic
    // once basic runs low. The entry key is made up.
    private static readonly OdyleEnergy StartingOdyle = new(420, 1200);
    private const int OdylePerFight = 10;
    private const int OdyleRecharge = 200;
    private const uint OdyleEntryKey = 4242;

    /// <summary>
    /// Checks the roster against upstream's game data; throws InvalidOperationException naming what does not resolve.
    /// Without <paramref name="odyleTap"/> no Odyle energy is sent.
    /// </summary>
    public DemoCombatFeed(
        EntityTracker entities, CombatSessionManager sessions, ICombatHistoryStore history, ILogger logger, Random random,
        OdyleEnergyTap? odyleTap = null)
    {
        this.entities = entities;
        this.sessions = sessions;
        this.history = history;
        this.logger = logger;
        this.random = random;
        this.odyleTap = odyleTap;
        var problems = DemoRosterCheck.Problems(gameData).ToList();
        if (problems.Count > 0)
            throw new InvalidOperationException("The demo roster does not match upstream's game data: " + string.Join("; ", problems));
        expectedPartyDps = DemoRoster.Combatants.Sum(ExpectedDps);
    }

    /// <summary>The entity id of the boss being fought, or null during a break.</summary>
    public int? CurrentBossEntityId => fight?.EntityId;

    /// <summary>
    /// Starts (or, after a stop, restarts) the feed at <paramref name="now"/>: registers the party and summon
    /// the first time, abandons a fight a stop interrupted (upstream completes it on its idle timeout, as it
    /// would after a real capture stopped) and starts a fresh fight and the ping at once. The Odyle login list goes
    /// to the tap here rather than through the schedule, which keeps the schedule's order (and so a seeded demo's
    /// fights) as it was before the demo had Odyle energy.
    /// </summary>
    public void Begin(DateTime now)
    {
        schedule.Clear();
        fight = null;
        Schedule(new DemoEvent(EventKind.StartFight), now);
        Schedule(new DemoEvent(EventKind.Ping), now);
        if (!partyRegistered)
        {
            RegisterParty();
            partyRegistered = true;
        }
        odyleTap?.Observe(OdyleEnergyRecordWriter.List(odyle, OdyleEntryKey));
    }

    /// <summary>
    /// Handles every event due at or before <paramref name="now"/>, in time order. An event that throws (a
    /// ping subscriber's fault, say) is logged and the next one handled, as upstream's packet dispatch logs a
    /// failing processor and goes on; a failure that repeats is logged again only after that kind of event has
    /// succeeded once more, so a persistent fault cannot flood the log. A clock jump moves the schedule first
    /// (<see cref="MaxClockJump"/>), and <paramref name="cancellation"/> is checked between events, so a step
    /// never holds up a stop.
    /// </summary>
    public void Step(DateTime now, CancellationToken cancellation = default)
    {
        Realign(now);
        while (!cancellation.IsCancellationRequested && schedule.TryPeek(out var next, out var due) && due <= now)
        {
            schedule.Dequeue();
            // Events of a fight that has ended are dropped here rather than searched out of the queue.
            if (next.Fight is not null && !ReferenceEquals(next.Fight, fight)) continue;
            try
            {
                Handle(next, due);
                failing.Remove(next.Kind);
            }
            catch (Exception ex)
            {
                if (failing.Add(next.Kind))
                    logger.LogError(ex, "A demo {Event} event failed; the demo carries on.", next.Kind);
            }
        }
    }

    // Moves every event by the same amount when the earliest is more than MaxClockJump away from now, so the
    // earliest falls due now. The fight in progress keeps its HP and its order of events; only its times move.
    private void Realign(DateTime now)
    {
        if (!schedule.TryPeek(out _, out var earliest)) return;
        var drift = now - earliest;
        if (drift.Duration() <= MaxClockJump) return;

        var events = schedule.UnorderedItems.ToList();
        schedule.Clear();
        foreach (var (pending, due) in events) schedule.Enqueue(pending, due + drift);
        if (fight is not null) fight.Start += drift;
        logger.LogInformation("The clock jumped by {Seconds:0} s; the demo carries on from now.", drift.TotalSeconds);
    }

    // Damage per second one combatant deals on average, from its skills, cast rate and hit profile.
    internal static double ExpectedDps(DemoCombatant combatant)
    {
        var p = combatant.Hits;
        double multiplier = (1 + 0.9 * p.Critical) * (1 + 0.15 * p.BackAttack) * (1 + 0.25 * p.Perfect)
            * (1 + p.DoubleDamage) * (1 - 0.5 * p.Parry);
        double totalWeight = combatant.Skills.Sum(s => s.Weight);
        double perCast = combatant.Skills.Sum(s =>
            s.Weight / totalWeight * ((s.DirectHit ? s.Damage * multiplier : 0) + s.DotTicks * (double)s.DotTickDamage));
        return perCast / combatant.CastSeconds;
    }

    private void RegisterParty()
    {
        // As the player-info decoders do: identity per session id, the user flagged by its own packet.
        foreach (var member in DemoRoster.Party)
        {
            entities.GetOrCreateSessionPlayer(member.EntityId, gameData.Classes.GetById(member.ClassId));
            entities.SetSessionPlayerName(member.EntityId, member.Name!, member.CombatPower, DemoRoster.ServerName, member.IsUser);
        }

        // As the summon decoder does: ownership only; the summon's entity appears with its first hit.
        var summon = DemoRoster.Summon;
        entities.RegisterSummon(summon.EntityId, summon.OwnerId!.Value);
    }

    private void Handle(DemoEvent next, DateTime at)
    {
        switch (next.Kind)
        {
            case EventKind.StartFight:
                StartFight(at);
                break;
            case EventKind.Cast:
                Cast(next.Fight!, next.Index, at);
                break;
            case EventKind.DotTick:
                if (next.TicksLeft > 1) Schedule(next with { TicksLeft = next.TicksLeft - 1 }, at.AddSeconds(DemoRoster.DotTickSeconds));
                Hit(DemoRoster.Combatants[next.Index], next.Skill!.Code, (long)Vary(next.Skill.DotTickDamage), HitFlagsRoll.None, isDot: true, at);
                break;
            case EventKind.Buff:
                var buff = DemoRoster.Buffs[next.Index];
                Schedule(next, at.AddSeconds(buff.PeriodSeconds));
                ApplyBuff(buff, at);
                break;
            case EventKind.Deadline:
                FinalBlow(at);
                break;
            case EventKind.Ping:
                Schedule(next, at + PingInterval);
                sessions.FirePingUpdate(random.Next(32, 58));
                break;
        }
    }

    // A fight starts: it spends Odyle energy, sent as the game's update record.
    private void SpendOdyle()
    {
        odyle = AfterFight(odyle);
        odyleTap?.Observe(OdyleEnergyRecordWriter.Update(odyle, OdyleEntryKey));
    }

    /// <summary>
    /// The demo's Odyle energy after a fight: a raid costs <see cref="OdylePerFight"/> basic, and once basic runs below
    /// that a recharge moves up to <see cref="OdyleRecharge"/> from charged to basic, so the numbers keep moving; basic
    /// never goes below 0. Once both would be 0 (after about 160 fights) it starts again from
    /// <see cref="StartingOdyle"/>, so it is never 0 and 0, which <see cref="OdyleEnergyRecordWriter"/> cannot write
    /// (the game's form for it is not documented) and which would freeze the display and make a later Begin throw
    /// (audit on PR #36).
    /// </summary>
    internal static OdyleEnergy AfterFight(OdyleEnergy energy)
    {
        var basic = energy.Basic - OdylePerFight;
        var charged = energy.Charged;
        if (basic < OdylePerFight)
        {
            var recharge = Math.Min(OdyleRecharge, charged);
            basic += recharge;
            charged -= recharge;
        }
        basic = Math.Max(0, basic);
        return basic == 0 && charged == 0 ? StartingOdyle : new OdyleEnergy(basic, charged);
    }

    private void StartFight(DateTime at)
    {
        fightsStarted++;
        var mobCode = DemoRoster.BossMobCodes[(fightsStarted - 1) % DemoRoster.BossMobCodes.Count];
        var targetSeconds = MinTargetSeconds + random.NextDouble() * (MaxTargetSeconds - MinTargetSeconds);
        var hpTotal = (long)Math.Round(expectedPartyDps * targetSeconds / 1_000) * 1_000;
        var current = new Fight(fightsStarted, DemoRoster.FirstBossEntityId + fightsStarted - 1, at, hpTotal);
        fight = current;

        Schedule(new DemoEvent(EventKind.Deadline, current), at + MaxFightLength);
        for (var i = 0; i < DemoRoster.Combatants.Count; i++)
            Schedule(new DemoEvent(EventKind.Cast, current, i), at.AddSeconds(0.3 + random.NextDouble() * 0.7));
        // Buffs go up before the pull; upstream replays those still running when the fight's first hit lands.
        for (var i = 0; i < DemoRoster.Buffs.Count; i++)
            Schedule(new DemoEvent(EventKind.Buff, current, i), at);

        // As the mob-spawn and RemainHp decoders do.
        entities.CreateOrUpdateTargetEntity(current.EntityId, mobCode, hpTotal);
        entities.UpdateTargetEntityHpCurrent(current.EntityId, hpTotal);
        logger.LogInformation("Demo fight {Number} against {Boss} started, {Hp} HP.",
            current.Number, entities.GetTargetMob(current.EntityId)?.Name, hpTotal);
        if (fightsStarted > 1) SpendOdyle();
    }

    private void Cast(Fight current, int index, DateTime at)
    {
        var combatant = DemoRoster.Combatants[index];
        Schedule(new DemoEvent(EventKind.Cast, current, index), at.AddSeconds(combatant.CastSeconds * (0.8 + random.NextDouble() * 0.4)));
        var skill = Pick(combatant.Skills);
        if (skill.DotTicks > 0)
            Schedule(new DemoEvent(EventKind.DotTick, current, index, skill, skill.DotTicks), at.AddSeconds(DemoRoster.DotTickSeconds));
        if (!skill.DirectHit) return;

        var flags = RollFlags(combatant.Hits);
        Hit(combatant, skill.Code, (long)(Vary(skill.Damage) * flags.Multiplier), flags, isDot: false, at);
    }

    // The user finishes a boss that outlived the fight's deadline, so every demo fight ends in a kill.
    private void FinalBlow(DateTime at)
    {
        if (fight is not { Hp: > 0 }) return;
        var user = DemoRoster.Party.First(member => member.IsUser);
        var strongest = user.Skills.Where(s => s.DirectHit).MaxBy(s => s.Damage)!;
        Hit(user, strongest.Code, fight.Hp, new HitFlagsRoll(true, false, false, false, false, false, 1), isDot: false, at);
    }

    // One hit, built the way upstream's damage decoders build it, then the boss's HP and, at 0, the fight's end.
    private void Hit(DemoCombatant source, int code, long damage, HitFlagsRoll flags, bool isDot, DateTime at)
    {
        if (fight is null) return;
        var current = fight;
        damage = Math.Clamp(damage, 1, current.Hp);
        var characterClass = gameData.GetClassBySkillCode(code)!;
        sessions.ProcessDamageEvent(new PlayerDamage
        {
            DateTime = at,
            SourceEntity = entities.GetOrCreateSessionPlayer(source.EntityId, characterClass),
            TargetEntity = entities.GetOrCreateTargetEntity(current.EntityId),
            Skill = gameData.GetSkillOrDefault(code),
            CharacterClass = characterClass,
            Damage = damage,
            IsCritical = flags.Critical,
            IsBackAttack = flags.BackAttack,
            IsFrontAttack = flags.FrontAttack,
            IsPerfect = flags.Perfect,
            IsDoubleDamage = flags.DoubleDamage,
            IsParry = flags.Parry,
            IsDot = isDot,
        });

        current.Hp -= damage;
        entities.UpdateTargetEntityHpCurrent(current.EntityId, current.Hp);
        if (current.Hp <= 0) EndFight(current, at);
    }

    private void EndFight(Fight ended, DateTime at)
    {
        // The break is scheduled first, so a failure to save cannot stop the demo.
        fight = null;
        Schedule(new DemoEvent(EventKind.StartFight), at + BreakLength);
        sessions.CompleteAndPersistActiveSessions(at);
        // Upstream saves in the background; flushing here puts the fight in history before the next step.
        history.FlushPendingSaves();
        logger.LogInformation("Demo fight {Number} ended after {Seconds:0} s and was saved to history.",
            ended.Number, (at - ended.Start).TotalSeconds);
    }

    // As the buff decoder does: one event per recipient, named from the skill table, with an empty description.
    private void ApplyBuff(DemoBuff buff, DateTime at)
    {
        var skill = gameData.GetSkillOrDefault(buff.Code);
        IEnumerable<int> recipients = buff.OnParty ? DemoRoster.Party.Select(member => member.EntityId) : [buff.CasterId];
        foreach (var recipient in recipients)
        {
            sessions.ProcessBuffEvent(new BuffEvent
            {
                EntityId = recipient,
                BuffId = skill.Id,
                BuffName = skill.Name,
                BuffIcon = skill.Icon,
                Description = string.Empty,
                DurationMs = (uint)(buff.DurationSeconds * 1_000),
                AppliedAt = at,
                CasterId = buff.CasterId,
            });
        }
    }

    private DemoSkill Pick(IReadOnlyList<DemoSkill> skills)
    {
        var roll = random.NextDouble() * skills.Sum(s => s.Weight);
        foreach (var skill in skills)
        {
            roll -= skill.Weight;
            if (roll < 0) return skill;
        }
        return skills[^1];
    }

    // The multipliers here are the ones ExpectedDps assumes; change both together.
    private HitFlagsRoll RollFlags(DemoHitProfile p)
    {
        bool critical = random.NextDouble() < p.Critical;
        bool back = random.NextDouble() < p.BackAttack;
        bool front = !back && random.NextDouble() < p.FrontAttack;
        bool perfect = random.NextDouble() < p.Perfect;
        bool doubled = random.NextDouble() < p.DoubleDamage;
        bool parry = random.NextDouble() < p.Parry;
        double multiplier = (critical ? 1.9 : 1) * (back ? 1.15 : 1) * (perfect ? 1.25 : 1) * (doubled ? 2 : 1) * (parry ? 0.5 : 1);
        return new HitFlagsRoll(critical, back, front, perfect, doubled, parry, multiplier);
    }

    // Damage varies by up to 15 percent either way, as real hits do.
    private double Vary(long damage) => damage * (0.85 + random.NextDouble() * 0.3);

    private void Schedule(DemoEvent next, DateTime due) => schedule.Enqueue(next, due);

    private enum EventKind
    {
        StartFight,
        Cast,
        DotTick,
        Buff,
        Deadline,
        Ping,
    }

    /// <summary>
    /// A scheduled event. <see cref="Fight"/> ties fight events to their fight, so they lapse when it ends;
    /// <see cref="Index"/> is a combatant (Cast, DotTick) or a buff (Buff).
    /// </summary>
    private sealed record DemoEvent(EventKind Kind, Fight? Fight = null, int Index = 0, DemoSkill? Skill = null, int TicksLeft = 0);

    private sealed record HitFlagsRoll(bool Critical, bool BackAttack, bool FrontAttack, bool Perfect, bool DoubleDamage, bool Parry, double Multiplier)
    {
        public static HitFlagsRoll None { get; } = new(false, false, false, false, false, false, 1);
    }

    /// <summary>One boss: its entity, when the fight began (moved with the schedule after a clock jump) and the HP it has left.</summary>
    private sealed class Fight(int number, int entityId, DateTime start, long hpTotal)
    {
        public int Number { get; } = number;
        public int EntityId { get; } = entityId;
        public DateTime Start { get; set; } = start;
        public long Hp { get; set; } = hpTotal;
    }
}
