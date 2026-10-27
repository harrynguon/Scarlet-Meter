namespace ScarletMeter.Engine.Capture;

/// <summary>The connection capture is locked onto: the adapter and the server's port it reads.</summary>
internal readonly record struct CaptureLock(string Adapter, int Port);

/// <summary>
/// Decides when a capture lock is not the game's and detection should start again (the rule of
/// <see cref="Upstream.CaptureLockGuard"/>, pure and single-threaded: the guard calls it from its timer alone).
/// </summary>
/// <remarks>
/// <para>Upstream's detection locks onto the first port whose traffic holds its keep-alive's three bytes
/// (<c>0e 00 36</c>) five times, counted from the start of detection, with no time window: random encrypted traffic
/// holds them about once per 16 MB, so with the game closed a long stretch of web traffic is enough (in Harry's game
/// on 2026-10-27 it locked onto port 443 while the game was closed and missed the next login). Once locked, its
/// watchdog counts any packet from the port as a heartbeat, so such a lock lasts as long as the traffic does.</para>
/// <para>The game's own sign is its server-time record (<c>03 36</c>, upstream's ServerTime, from which it reads the
/// ping): the server's reply to the ping request the client sends every ten seconds (A2Tools' ping tracker, 5c6f02c).
/// Every such record upstream reads counts, whatever ping it makes of it, so a clock upstream cannot read does not
/// count against the game; random traffic yields one about once in hundreds of gigabytes. A lock that has carried one
/// since it was first seen is the game's, and is left to upstream's watchdog for as long as it lasts: a wait at
/// character select, a loading screen or a quiet spell never drops it. A lock that has carried none is dropped after
/// <see cref="QuietLimit"/>, counted from when it was first seen: <see cref="WebPortQuietLimit"/> on the web's ports,
/// 80 and 443, which the game has never used (A2Tools leaves them out of its detection; a relay that tunnels the game
/// over them sends three of the records in that time, and is then the game's), and <see cref="OtherPortQuietLimit"/>
/// elsewhere, long enough that the game's own lock, made at character select where the record may not come (not
/// known), is rarely reached before the player enters the world (docs/HANDOFF.md), and longer than upstream's watchdog
/// can take to drop a port that goes silent (up to 120 s).</para>
/// </remarks>
internal sealed class CaptureLockWatch
{
    /// <summary>How long a lock on the web's ports may go without the game's server-time record.</summary>
    internal static readonly TimeSpan WebPortQuietLimit = TimeSpan.FromSeconds(30);

    /// <summary>How long a lock on any other port may go without the game's server-time record.</summary>
    internal static readonly TimeSpan OtherPortQuietLimit = TimeSpan.FromMinutes(5);

    private CaptureLock? watched;
    private long watchedSince; // the timestamp the watched lock was first seen at
    private bool proven; // the watched lock has carried a server-time record since then: it is the game's

    /// <summary>Whether a port is one of the web's (HTTP, HTTPS), which the game has never used.</summary>
    internal static bool IsWebPort(int port) => port is 80 or 443;

    /// <summary>How long a lock on this port may go without the game's server-time record.</summary>
    internal static TimeSpan QuietLimit(int port) => IsWebPort(port) ? WebPortQuietLimit : OtherPortQuietLimit;

    /// <summary>
    /// Takes the lock as it stands now (null while detecting) and the timestamp of the last server-time record (null
    /// for none), both on <paramref name="clock"/>'s monotonic timestamps, and says whether to drop the lock; when it
    /// does, <paramref name="quiet"/> is how long the lock went without one, and the lock is forgotten, so the next one
    /// (even on the same port) is timed afresh. A lock that has carried a record is never dropped.
    /// </summary>
    public bool ShouldRestart(CaptureLock? now, long? lastGameRecord, long timestamp, TimeProvider clock, out TimeSpan quiet)
    {
        ArgumentNullException.ThrowIfNull(clock);
        quiet = TimeSpan.Zero;
        if (now is not { } current)
        {
            watched = null;
            return false;
        }
        if (watched != current)
        {
            watched = current;
            watchedSince = timestamp;
            proven = false;
        }
        if (lastGameRecord is { } last && last > watchedSince) proven = true;
        if (proven) return false;
        quiet = clock.GetElapsedTime(watchedSince, timestamp);
        if (quiet < QuietLimit(current.Port)) return false;
        watched = null;
        return true;
    }
}
