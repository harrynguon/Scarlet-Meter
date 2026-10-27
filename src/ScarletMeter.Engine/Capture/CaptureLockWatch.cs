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
/// on 2026-10-27 it locked onto port 443 at 16:23, an hour and a half after the game closed, and missed the next
/// login). Once locked, its watchdog counts any packet from the port as a heartbeat, so such a lock lasts as long as
/// the traffic does.</para>
/// <para>The game's own sign is its server-time record (<c>03 36</c>, upstream's ServerTime, from which it reads the
/// ping): the server's reply to the ping request the client sends every ten seconds (A2Tools' ping tracker, 5c6f02c).
/// Every such record counts, whatever ping upstream makes of it, so a clock upstream cannot read does not count against
/// the game; random traffic yields one about once in hundreds of gigabytes. A lock that has had none for
/// <see cref="QuietLimit"/>, counted from the later of the lock (as first seen here) and the last record, is dropped:
/// <see cref="WebPortQuietLimit"/> on the web's ports, 80 and 443, which the game has never used (A2Tools leaves them
/// out of its detection; a relay that tunnels the game over them still sends the records, three in that time), and
/// <see cref="OtherPortQuietLimit"/> elsewhere. That is longer than upstream's watchdog can take to drop a port that goes
/// silent (up to 120 s: its 60-second check needs a whole silent period), so a game that closes is left to upstream, and
/// this drops only a lock whose port keeps sending without the game's records. Whether the game sends the record at
/// character select is not known; if it does not, a lock made there is dropped and made again every
/// <see cref="OtherPortQuietLimit"/> while the player stays (docs/HANDOFF.md).</para>
/// </remarks>
internal sealed class CaptureLockWatch
{
    /// <summary>How long a lock on the web's ports may go without the game's server-time record.</summary>
    internal static readonly TimeSpan WebPortQuietLimit = TimeSpan.FromSeconds(30);

    /// <summary>How long a lock on any other port may go without the game's server-time record.</summary>
    internal static readonly TimeSpan OtherPortQuietLimit = TimeSpan.FromSeconds(150);

    private CaptureLock? watched;
    private long watchedSince; // the timestamp the watched lock was first seen at

    /// <summary>Whether a port is one of the web's (HTTP, HTTPS), which the game has never used.</summary>
    internal static bool IsWebPort(int port) => port is 80 or 443;

    /// <summary>How long a lock on this port may go without the game's server-time record.</summary>
    internal static TimeSpan QuietLimit(int port) => IsWebPort(port) ? WebPortQuietLimit : OtherPortQuietLimit;

    /// <summary>
    /// Takes the lock as it stands now (null while detecting) and the timestamp of the last server-time record (null
    /// for none), both on <paramref name="clock"/>'s monotonic timestamps, and says whether to drop the lock; when it
    /// does, <paramref name="quiet"/> is how long the lock went without one, and the lock is forgotten, so the next one
    /// (even on the same port) is timed afresh.
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
        }
        var quietSince = lastGameRecord is { } last && last > watchedSince ? last : watchedSince;
        quiet = clock.GetElapsedTime(quietSince, timestamp);
        if (quiet < QuietLimit(current.Port)) return false;
        watched = null;
        return true;
    }
}
