namespace ScarletMeter.Engine;

/// <summary>
/// Everything the app needs from combat: capture control, a snapshot of the current fight and ping
/// updates. The only implementation wraps upstream RATmeter's pipeline (Upstream/UpstreamCombatEngine).
/// Callers never see an upstream type, so an upstream change is contained in the engine.
/// </summary>
public interface ICombatEngine : IDisposable
{
    /// <summary>
    /// Discovers network adapters and starts listening for the game's traffic. An adapter that is found but
    /// fails to open is only logged by upstream, so Start can return with nothing listening;
    /// <see cref="CaptureState.AdapterName"/> then stays null.
    /// </summary>
    /// <exception cref="CaptureStartException">Npcap could not be loaded, or no eligible adapter was found.</exception>
    void Start();

    /// <summary>Stops capture. The current fight's numbers stay readable.</summary>
    void Stop();

    /// <summary>
    /// Drops the current fight: every target's session, the active target and buffered buffs. Known players,
    /// summon ownership and the captured stream are kept, so the next hit starts a fresh fight.
    /// </summary>
    void Reset();

    /// <summary>Whether capture runs and, once the game's connection is found, which adapter carries it.</summary>
    CaptureState Capture { get; }

    /// <summary>An immutable view of the active fight, cheap enough to poll several times a second.</summary>
    CombatSnapshot GetSnapshot();

    /// <summary>Latest ping in milliseconds. Raised on a capture thread, never on the UI thread.</summary>
    event EventHandler<int>? PingUpdated;
}
