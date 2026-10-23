using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using ScarletMeter.Engine;

namespace ScarletMeter.App.Pages;

/// <summary>
/// Live meter view. Polls the engine ten times a second in a loop that resumes on the renderer's dispatcher
/// (the UI thread) and renders the active fight; ping arrives by event from a capture thread. A failed refresh is logged and retried on the next tick, so
/// the meter never freezes silently. Everything stops in <see cref="DisposeAsync"/>, which the window's
/// deferred close awaits before the WebView goes away.
/// </summary>
public partial class MeterPage : IAsyncDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(100);

    private readonly CancellationTokenSource stopping = new();
    private CombatSnapshot snapshot = CombatSnapshot.Empty;
    private int? ping;
    private Task? refreshLoop;
    private bool refreshFailing;

    [Inject]
    private ICombatEngine Engine { get; set; } = default!;

    [Inject]
    private ILogger<MeterPage> Logger { get; set; } = default!;

    // Upstream's rule: hide players the game has not named unless they already matter.
    private IEnumerable<PlayerRow> Visible => snapshot.Players.Where(p => p.IsIdentified || p.DamageShare > 1);

    private string StatusText => Engine.Capture switch
    {
        { IsCapturing: false } => "Capture is off",
        { AdapterName: null } => "Waiting for the game's connection...",
        _ => "No active target",
    };

    protected override void OnInitialized()
    {
        Engine.PingUpdated += OnPingUpdated;
        refreshLoop = RefreshLoopAsync();
    }

    private async Task RefreshLoopAsync()
    {
        using var timer = new PeriodicTimer(RefreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stopping.Token))
            {
                try
                {
                    snapshot = Engine.GetSnapshot();
                    await RenderAsync();
                    if (refreshFailing) Logger.LogInformation("Refreshing the meter works again.");
                    refreshFailing = false;
                }
                // Only cancellation from disposal ends the loop; any other failure, an unrelated cancellation
                // included, is logged and retried.
                catch (Exception ex) when (ex is not OperationCanceledException || !stopping.IsCancellationRequested)
                {
                    // Upstream's EntityTracker dictionaries are read here while the capture thread writes them,
                    // as in upstream's own UI, so a read can fail now and then. Keep the last snapshot and log
                    // the first failure of a run only, so a persistent fault cannot flood the log.
                    if (!refreshFailing)
                        Logger.LogWarning(ex, "Refreshing the meter failed; retrying on every tick.");
                    refreshFailing = true;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Disposing.
        }
    }

    private void OnPingUpdated(object? sender, int milliseconds)
    {
        ping = milliseconds;
        _ = RenderAsync();
    }

    // A render requested after disposal would reach a dead renderer; skip it rather than let it surface.
    private async Task RenderAsync()
    {
        if (stopping.IsCancellationRequested) return;
        try
        {
            await InvokeAsync(StateHasChanged);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void Reset()
    {
        Engine.Reset();
        snapshot = CombatSnapshot.Empty;
    }

    public async ValueTask DisposeAsync()
    {
        Engine.PingUpdated -= OnPingUpdated;
        await stopping.CancelAsync();
        if (refreshLoop is not null) await refreshLoop;
        stopping.Dispose();
    }
}
