using AionDpsMeter.Core.Models;
using AionDpsMeter.Services.Models;
using AionDpsMeter.Services.Services.Entity;
using AionDpsMeter.Services.Services.Session;

namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// Applies <see cref="DummyFightRule"/> to upstream's fights, reading the facts from upstream: whether the target is a
/// training dummy (<see cref="Mob.IsDummy"/>, from upstream's game data), whether the user is among the fight's
/// players (<see cref="PlayerStats.IsUser"/>) and whether upstream knows the user at all (a tracked player marked as
/// the user, which upstream sets from the character's info at login). Used by <see cref="DummyFightHistoryStore"/>
/// for saves and by <see cref="UpstreamCombatEngine"/> for the overlay, the details and the history list.
/// </summary>
internal sealed class DummyFightFilter(EntityTracker entities)
{
    // Once known, the user stays known: upstream never forgets the user's name or unmarks a player.
    private volatile bool userKnown;

    /// <summary>A live or running fight: its target (null when there is none) and its players.</summary>
    public bool Keeps(Mob? target, IEnumerable<PlayerStats?>? players) =>
        DummyFightRule.Keeps(target?.IsDummy, players?.Any(p => p is { IsUser: true }) == true, IsUserKnown);

    /// <summary>
    /// A finished or running fight as upstream snapshots it. Its target is looked up by entity id: upstream's fights
    /// hold the tracker's own target, which the tracker never drops, so this is the fight's target.
    /// </summary>
    public bool Keeps(HistorySessionSnapshot fight)
    {
        ArgumentNullException.ThrowIfNull(fight);
        return Keeps(entities.GetTargetMob(fight.TargetId), fight.PlayerStats);
    }

    private bool IsUserKnown()
    {
        if (userKnown) return true;
        try
        {
            userKnown = entities.PlayerEntities.Any(p => p.IsUser);
        }
        catch (Exception)
        {
            // The capture thread writes the tracker's dictionaries without a lock; a read from another thread (the
            // overlay's poll) can fail mid-write, with any of several exceptions. "Not known yet" keeps the fight,
            // upstream's behaviour, for this one call.
            return false;
        }
        return userKnown;
    }
}
