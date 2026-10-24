namespace ScarletMeter.App.Alerts;

/// <summary>
/// When "Bring the meter back before an event" may bring a minimized meter back: at once when an event's notice
/// appears, or, while the notices stand aside for a boss fight, once they come back, so the meter does not pop up
/// mid-fight with nothing to show. A restore that waited happens only if a notice is still due by then. Not
/// thread-safe: the window layer calls it on the UI thread.
/// </summary>
public sealed class AlertRestoreGate
{
    private bool held;
    private bool pending;

    /// <summary>An event's notice appeared: true to bring the meter back now; while the notices are held, it waits.</summary>
    public bool Approaching()
    {
        if (!held) return true;
        pending = true;
        return false;
    }

    /// <summary>
    /// The notices are held back for a boss fight, or come back. True when a restore that waited should happen now,
    /// which is only on coming back with <paramref name="noticesDue"/>.
    /// </summary>
    public bool Hold(bool hold, bool noticesDue)
    {
        if (hold == held) return false;
        held = hold;
        if (hold || !pending) return false;
        pending = false;
        return noticesDue;
    }
}
