using Microsoft.AspNetCore.Components;
using ScarletMeter.Engine;

namespace ScarletMeter.App.Pages;

/// <summary>
/// Live meter view. Polls the engine ten times a second on a background loop and renders the active fight;
/// ping arrives by event from a capture thread. Everything stops in <see cref="DisposeAsync"/>, which the
/// window's deferred close awaits before the WebView goes away.
/// </summary>
public partial class MeterPage : IAsyncDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(100);

    private readonly CancellationTokenSource stopping = new();
    private CombatSnapshot snapshot = CombatSnapshot.Empty;
    private int? ping;
    private Task? refreshLoop;

    [Inject]
    private ICombatEngine Engine { get; set; } = default!;

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
                snapshot = Engine.GetSnapshot();
                await RenderAsync();
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
