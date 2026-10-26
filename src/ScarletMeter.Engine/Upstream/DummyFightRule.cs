namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// Whether the meter keeps a fight (shows it, lists it, saves it) when it may be a stranger's fight on a training
/// dummy. Upstream counts every dummy as a boss, so without this a fight between other players and a dummy nearby
/// is saved to History and, once the player's own last hit is five seconds old, becomes the overlay's fight. The
/// meter keeps:
/// <list type="bullet">
/// <item>every fight that is not on a dummy (including one whose target is no longer known: nothing to go by);</item>
/// <item>a dummy fight the player has hit, with every other player in it (the player's choice, 2026-10-26);</item>
/// <item>every dummy fight while the meter does not yet know which player is the user (it learns that from the
/// character's info, sent at login): it cannot tell the player's fight from a stranger's then, and hiding them all
/// would hide the player's own. Upstream's behaviour stands until then.</item>
/// </list>
/// Pure: the caller reads the facts from upstream (<see cref="DummyFightFilter"/>).
/// </summary>
internal static class DummyFightRule
{
    /// <param name="isDummy">Whether the fight's target is a training dummy; null when the target is not known.</param>
    /// <param name="userInFight">Whether the player who is the user dealt damage in the fight.</param>
    /// <param name="isUserKnown">Whether the meter knows which player is the user. Called only when the answer
    /// decides, so a caller can make it a read of state another thread writes.</param>
    public static bool Keeps(bool? isDummy, bool userInFight, Func<bool> isUserKnown)
    {
        ArgumentNullException.ThrowIfNull(isUserKnown);
        return isDummy != true || userInFight || !isUserKnown();
    }
}
