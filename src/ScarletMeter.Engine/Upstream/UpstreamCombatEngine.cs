using AionDpsMeter.Services.Models;
using AionDpsMeter.Services.PacketCapture;
using AionDpsMeter.Services.Services.Session;
using AionDpsMeter.Services.Services.Session.Persistence;
using Microsoft.Extensions.Logging;

namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// <see cref="ICombatEngine"/> over upstream RATmeter's pipeline. Upstream's IPacketService drives capture,
/// TCP reassembly, frame decoding and the opcode processors that feed CombatSessionManager; snapshots are
/// read back from that manager. This class and <see cref="UpstreamSettingsBridge"/> are the only code that
/// touches upstream types, so an upstream API change is contained here.
/// </summary>
internal sealed class UpstreamCombatEngine : ICombatEngine
{
    private readonly IPacketService pipeline;
    private readonly IPacketCaptureDevice capture;
    private readonly CombatSessionManager sessions;
    private readonly ICombatHistoryStore history;
    private readonly ILogger<UpstreamCombatEngine> logger;
    private bool disposed;

    public UpstreamCombatEngine(
        IPacketService pipeline,
        IPacketCaptureDevice capture,
        CombatSessionManager sessions,
        ICombatHistoryStore history,
        ILogger<UpstreamCombatEngine> logger)
    {
        this.pipeline = pipeline;
        this.capture = capture;
        this.sessions = sessions;
        this.history = history;
        this.logger = logger;
    }

    public event EventHandler<int>? PingUpdated
    {
        add => sessions.PingUpdated += value;
        remove => sessions.PingUpdated -= value;
    }

    public CaptureState Capture => new(capture.IsCapturing, capture.DeviceName);

    public void Start()
    {
        try
        {
            pipeline.Start();
        }
        catch (InvalidOperationException ex)
        {
            // Upstream's CaptureDevice.StartCapture: Npcap loaded, but listed no adapter, or only kinds upstream
            // skips (Hyper-V, VMware, VirtualBox, TAP, WAN Miniport). Upstream logs nothing about which.
            throw new CaptureStartException(
                "Packet capture could not start: no eligible network adapter was found. Virtual, TAP and WAN " +
                "Miniport adapters are skipped. If Npcap was installed with access restricted to administrators, " +
                "start the meter as administrator.", ex);
        }
        catch (Exception ex)
        {
            // SharpPcap fails (DllNotFoundException, often inside a TypeInitializationException) when wpcap.dll,
            // Npcap's WinPcap-compatible library, is absent.
            throw new CaptureStartException(
                "Packet capture could not start. Install Npcap (https://npcap.com) with \"Install Npcap in " +
                "WinPcap API-compatible Mode\" ticked, then start the meter again.", ex);
        }
    }

    public void Stop() => pipeline.Stop();

    // Fight state only, under the session manager's lock. Upstream's IPacketService.Reset would also clear the
    // summon-to-owner map (a pet already out would show as an unidentified player) and the TCP reassembly
    // buffer (frames are dropped until the stream re-synchronises), both without a lock while the capture
    // thread writes them. Upstream's own UI never calls it.
    public void Reset() => sessions.Reset();

    public CombatSnapshot GetSnapshot()
    {
        // Each upstream getter takes the session manager's lock on its own, so a snapshot is a composition of
        // three consistent reads taken a few microseconds apart, which is what upstream's own UI does.
        var mob = sessions.GetActiveTargetInfo();
        var duration = sessions.GetCombatDuration();
        var stats = sessions.PlayerStats;
        if (mob is null && stats.Count == 0) return CombatSnapshot.Empty;

        var players = stats
            .Select(s => new PlayerRow(
                s.PlayerId, s.PlayerName, s.IsUser, s.IsIdentified, s.ClassId, s.ClassName, s.ServerName,
                s.CombatPower, s.TotalDamage, s.DamagePerSecond, s.DamagePercentage, s.CriticalRate, s.PlayerDeaths))
            .ToList();
        long totalDamage = players.Sum(p => p.TotalDamage);
        double partyDps = duration.TotalSeconds > 0 ? totalDamage / duration.TotalSeconds : 0;
        var target = mob is null ? null : new TargetInfo(mob.Id, mob.Name, mob.HpCurrent, mob.HpTotal, mob.IsBoss);
        return new CombatSnapshot(target, duration, partyDps, players);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            pipeline.Stop();
            // Upstream persists completed boss fights; close the open ones so a normal exit loses nothing.
            sessions.CompleteAndPersistActiveSessions();
            history.FlushPendingSaves();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while shutting down the combat engine.");
        }
        (pipeline as IDisposable)?.Dispose();
    }
}
