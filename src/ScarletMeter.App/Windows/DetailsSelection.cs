namespace ScarletMeter.App.Windows;

/// <summary>Which player the details window shows: from the live fight (<see cref="FightId"/> null) or a saved one.</summary>
public sealed record DetailsSource(long PlayerId, Guid? FightId)
{
    public bool IsLive => FightId is null;
}

/// <summary>
/// The details window's current subject. The window is one of a kind; clicking another player (in the
/// overlay or the history) re-targets it, and its page follows <see cref="Changed"/>. UI thread only.
/// </summary>
public sealed class DetailsSelection
{
    public DetailsSource? Current { get; private set; }

    public event EventHandler? Changed;

    public void Select(DetailsSource source)
    {
        if (source == Current) return;
        Current = source;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
