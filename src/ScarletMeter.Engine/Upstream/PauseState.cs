namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// Whether the meter is paused (<see cref="AbyssPause"/> sets it). Its own object so that the settings bridge upstream
/// reads on every hit can see it without depending on the pause, which depends on upstream's session manager, which
/// depends on the bridge.
/// </summary>
internal sealed class PauseState
{
    private volatile bool paused;

    /// <summary>True while the meter is paused. Read on the capture thread, set on whichever thread changed the map or the setting.</summary>
    public bool Paused
    {
        get => paused;
        set => paused = value;
    }
}
