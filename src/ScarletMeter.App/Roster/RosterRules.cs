using ScarletMeter.Engine;
using ScarletMeter.WorldEvents;

namespace ScarletMeter.App.Roster;

/// <summary>
/// When the game's recharges come: the daily and weekly resets from the world-event schedule (<see cref="EventCatalog"/>,
/// its Global preset: 07:00 UTC, Wednesdays for the weekly; not verified in the game) and basic Odyle energy's refill
/// every three hours on the hours that meet the daily reset (01:00, 04:00, 07:00 … UTC for Global), as A2Tools' Roster
/// plugin has it (taengu/A2Tools-Plugins, plugins/alt-roster/logic.js at 7a4720b; one refill seen at 16:00 UTC, the rest
/// of the rule from another app, not checked). Pure: instants are parameters, never the system clock.
/// </summary>
public sealed class RechargeCalendar
{
    /// <summary>Basic energy refills this often.</summary>
    public static readonly TimeSpan OdyleRefillEvery = TimeSpan.FromHours(3);

    // A fixed instant to find a daily reset from; any reset will do, since the refills meet every one of them.
    private static readonly DateTimeOffset GridOrigin = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly EventRule daily;
    private readonly EventRule weekly;
    private readonly EventRule siege;
    private readonly TimeSpan serverOffset;
    private readonly DateTimeOffset refillAnchor;

    /// <param name="daily">The daily reset's rule: one reset a day, so the three-hour refills meet each one.</param>
    /// <param name="weekly">The weekly reset's rule.</param>
    /// <param name="siege">The Abyss Siege's rule: the corridors a side may run are handed out anew at each.</param>
    /// <param name="serverOffset">The server clock, for rules on it (the settings' server offset).</param>
    public RechargeCalendar(EventRule daily, EventRule weekly, EventRule siege, TimeSpan serverOffset)
    {
        this.daily = daily;
        this.weekly = weekly;
        this.siege = siege;
        this.serverOffset = serverOffset;
        refillAnchor = EventSchedule.Occurrence(daily, GridOrigin, serverOffset).Next;
    }

    /// <summary>The calendar of the schedule the overlay's notices use (<see cref="EventCatalog.Default"/>).</summary>
    public static RechargeCalendar Default(TimeSpan serverOffset) => new(
        EventCatalog.Default.Single(rule => rule.Id == EventIds.Daily),
        EventCatalog.Default.Single(rule => rule.Id == EventIds.Weekly),
        EventCatalog.Default.Single(rule => rule.Id == EventIds.Siege),
        serverOffset);

    /// <summary>The first basic-energy refill strictly after <paramref name="instant"/>.</summary>
    public DateTimeOffset NextOdyleRefill(DateTimeOffset instant) => refillAnchor + OdyleRefillEvery * (RefillTick(instant) + 1);

    /// <summary>How many refills came after <paramref name="from"/>, up to and with <paramref name="to"/>; 0 when <paramref name="to"/> is not later.</summary>
    public long OdyleRefillsBetween(DateTimeOffset from, DateTimeOffset to) => to > from ? RefillTick(to) - RefillTick(from) : 0;

    /// <summary>How many daily resets came after <paramref name="from"/>, up to and with <paramref name="to"/>, counting at most <paramref name="enough"/>.</summary>
    public int DailyResetsBetween(DateTimeOffset from, DateTimeOffset to, int enough) => ResetsBetween(daily, from, to, enough);

    /// <summary>How many weekly resets came after <paramref name="from"/>, up to and with <paramref name="to"/>, counting at most <paramref name="enough"/>.</summary>
    public int WeeklyResetsBetween(DateTimeOffset from, DateTimeOffset to, int enough) => ResetsBetween(weekly, from, to, enough);

    /// <summary>The next daily reset strictly after <paramref name="instant"/>.</summary>
    public DateTimeOffset NextDailyReset(DateTimeOffset instant) => EventSchedule.Occurrence(daily, instant, serverOffset).Next;

    /// <summary>The next weekly reset strictly after <paramref name="instant"/>.</summary>
    public DateTimeOffset NextWeeklyReset(DateTimeOffset instant) => EventSchedule.Occurrence(weekly, instant, serverOffset).Next;

    /// <summary>The next Abyss Siege strictly after <paramref name="instant"/>.</summary>
    public DateTimeOffset NextSiege(DateTimeOffset instant) => EventSchedule.Occurrence(siege, instant, serverOffset).Next;

    /// <summary>
    /// The last Abyss Siege at or before <paramref name="instant"/>: found by walking forward from eight days before,
    /// which holds one for any schedule with a siege at least weekly (the catalog's are three a week).
    /// </summary>
    public DateTimeOffset LastSiege(DateTimeOffset instant)
    {
        var at = instant - TimeSpan.FromDays(8);
        var last = at;
        while (EventSchedule.Occurrence(siege, at, serverOffset).Next is var next && next <= instant)
        {
            last = next;
            at = next;
        }
        return last;
    }

    // Which three-hour slot of the refill grid an instant falls in (a refill at the slot's start belongs to it).
    private long RefillTick(DateTimeOffset instant)
    {
        var since = (instant - refillAnchor).Ticks;
        var tick = since / OdyleRefillEvery.Ticks;
        return since % OdyleRefillEvery.Ticks < 0 ? tick - 1 : tick;
    }

    // Counts a rule's starts in (from, to], stopping at `enough`: past that the count changes nothing (a cap is reached),
    // and a count seen years ago does not walk every day since.
    private int ResetsBetween(EventRule rule, DateTimeOffset from, DateTimeOffset to, int enough)
    {
        var count = 0;
        var at = from;
        while (count < enough)
        {
            var next = EventSchedule.Occurrence(rule, at, serverOffset).Next;
            if (next > to) break;
            count++;
            at = next;
        }
        return count;
    }
}

/// <summary>What a count is likely to be now: the value, how much of it recharges added since the game said, and whether it is at its cap.</summary>
public readonly record struct Estimate(int Value, int Gained, bool Full);

/// <summary>How an entry recharges: by how much, up to what, at which reset.</summary>
/// <param name="Key">Its name in the page and the file.</param>
/// <param name="Name">What the game calls it.</param>
/// <param name="Cap">The most it recharges to; the extra from recharge items comes on top and is not counted.</param>
/// <param name="Add">How much each reset adds.</param>
/// <param name="Weekly">True for the weekly reset, false for the daily.</param>
public sealed record ContentRule(string Key, string Name, int Cap, int Add, bool Weekly);

/// <summary>
/// The roster's estimates, pure: Odyle energy refills and the content entries' recharges, from a count the game sent and
/// when. Odyle energy: +15 basic every three hours up to the cap (840, a membership's, or 560 without); charged never refills.
/// Contents, from the game's own tooltips as A2Tools' Roster plugin quotes them (plugin.js at 7a4720b): Nightmare +2 a
/// day up to 14, Ascension Trial +3 a week up to 3. None of the recharge times is verified in the game.
/// </summary>
public static class RosterRules
{
    /// <summary>Basic energy each refill adds.</summary>
    public const int OdyleStep = 15;

    /// <summary>The basic energy cap without a membership.</summary>
    public const int StandardCap = 560;

    /// <summary>The basic energy cap with a membership.</summary>
    public const int MembershipCap = 840;

    /// <summary>Nightmare (entry 4): +2 at each daily reset, up to 14.</summary>
    public static readonly ContentRule Nightmare = new("nightmare", "Nightmare", 14, 2, Weekly: false);

    /// <summary>Ascension Trial (entry 80000001): +3 at each weekly reset, up to 3.</summary>
    public static readonly ContentRule Ascension = new("ascension", "Ascension Trial", 3, 3, Weekly: true);

    /// <summary>How many of a character's Artifact Corridor entries are kept: more than a siege's worth.</summary>
    public const int CorridorRunsKept = 12;

    /// <summary>
    /// The most corridors a side runs between two sieges, one per lower-layer artifact it holds (A2Tools' Roster plugin:
    /// "up to 3"); how many it holds now is <see cref="CorridorsOpen"/>.
    /// </summary>
    public const int CorridorsPerSiege = AbyssArtifacts.Corridors;

    /// <summary>
    /// The longest a holding the game reports is taken to last, from when it was read to its reset: sieges come three
    /// times a week, so a reset further off is not one the meter can use, and the schedule stands in.
    /// </summary>
    public static readonly TimeSpan LongestHolding = TimeSpan.FromDays(7);

    /// <summary>The Artifact Corridor entries since the last Abyss Siege at <paramref name="now"/>.</summary>
    public static int CorridorRunsSinceSiege(IReadOnlyList<DateTimeOffset> runs, DateTimeOffset now, RechargeCalendar calendar)
    {
        var since = calendar.LastSiege(now);
        return runs.Count(run => run > since);
    }

    /// <summary>
    /// How many Artifact Corridors the side of a character on <paramref name="serverId"/> can enter until the next siege:
    /// the lower-layer artifacts its side holds, as A2Tools' Roster plugin counts them (plugin.js at 7a4720b): one nobody
    /// holds closes its corridor to both sides, one the game did not name counts as open. Null while that is not known:
    /// the side not known (no server id), no holders read on that server, or the holders read are past: their reset has
    /// come, or, without a reset the meter can use (<see cref="LongestHolding"/>), a siege on the schedule since they were read.
    /// </summary>
    public static int? CorridorsOpen(int serverId, RosterArtifacts? artifacts, DateTimeOffset now, RechargeCalendar calendar)
    {
        var side = AbyssArtifacts.SideOf(serverId);
        if (side == ArtifactHolder.Unknown || artifacts is null || artifacts.ServerId != serverId) return null;
        var current = artifacts.Until is { } until && until > artifacts.SeenAt && until - artifacts.SeenAt <= LongestHolding
            ? now < until
            : artifacts.SeenAt >= calendar.LastSiege(now);
        return current ? artifacts.Holders.Count(holder => holder == side || holder == ArtifactHolder.Unknown) : null;
    }

    /// <summary>
    /// The cap the character's basic energy refills to: 560 when the player chose it (no membership), else 840, a
    /// membership's, the default (Harry, 2026-10-27; it was 560 until more than 560 had been seen).
    /// </summary>
    public static int OdyleCap(RosterCharacter character) => character.OdyleCap == OdyleCapChoice.Standard ? StandardCap : MembershipCap;

    /// <summary>
    /// Basic energy as it likely is at <paramref name="now"/>: what the game said, plus a step for each refill since, up
    /// to the cap. A value already at or past the cap stays as it was (spending, not refills, brings it down).
    /// </summary>
    public static Estimate OdyleNow(RosterCount seen, int cap, DateTimeOffset now, RechargeCalendar calendar)
    {
        var refills = calendar.OdyleRefillsBetween(seen.SeenAt, now);
        var grown = (int)Math.Min(cap, seen.First + Math.Min(refills, cap) * (long)OdyleStep);
        var value = Math.Max(seen.First, grown);
        return new Estimate(value, value - seen.First, value >= cap);
    }

    /// <summary>When basic energy reaches the cap if nothing is spent, or null when it already has.</summary>
    public static DateTimeOffset? OdyleFullAt(RosterCount seen, int cap, RechargeCalendar calendar)
    {
        if (seen.First >= cap) return null;
        var needed = (cap - seen.First + OdyleStep - 1) / OdyleStep;
        return calendar.NextOdyleRefill(seen.SeenAt) + RechargeCalendar.OdyleRefillEvery * (needed - 1);
    }

    /// <summary>
    /// A content's entries left as they likely are at <paramref name="now"/>: what the game said plus each reset's
    /// recharge since, up to the cap (the extra from items, <see cref="RosterCount.Second"/>, is apart and unchanged).
    /// </summary>
    public static Estimate ContentNow(ContentRule rule, RosterCount seen, DateTimeOffset now, RechargeCalendar calendar)
    {
        var enough = (rule.Cap + rule.Add - 1) / rule.Add;
        var resets = rule.Weekly ? calendar.WeeklyResetsBetween(seen.SeenAt, now, enough) : calendar.DailyResetsBetween(seen.SeenAt, now, enough);
        var value = Math.Max(seen.First, Math.Min(rule.Cap, seen.First + resets * rule.Add));
        return new Estimate(value, value - seen.First, value >= rule.Cap);
    }
}
