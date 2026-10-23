using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using ScarletMeter.App.Alerts;
using ScarletMeter.App.Formatting;
using ScarletMeter.App.Settings;
using ScarletMeter.App.Windows;
using ScarletMeter.Engine;

namespace ScarletMeter.App.Pages;

/// <summary>
/// The overlay. Polls the engine ten times a second in a loop that resumes on the renderer's dispatcher
/// (the UI thread) and renders the active fight with the player's appearance settings, which apply live;
/// ping arrives by event from a capture thread. A failed refresh is logged and retried on the next tick, so
/// the meter never freezes silently. Everything stops in <see cref="DisposeAsync"/>, which the window's
/// deferred close awaits before the WebView goes away.
/// </summary>
public partial class MeterPage : IAsyncDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(100);

    private readonly CancellationTokenSource stopping = new();
    private CombatSnapshot snapshot = CombatSnapshot.Empty;
    private AppearanceSettings appearance = new();
    private int? ping;
    private Task? refreshLoop;
    private bool refreshFailing;

    [Inject]
    private ICombatEngine Engine { get; set; } = default!;

    [Inject]
    private SettingsService Settings { get; set; } = default!;

    [Inject]
    private IMeterWindows Windows { get; set; } = default!;

    [Inject]
    private ILogger<MeterPage> Logger { get; set; } = default!;

    // World-event notices; their countdowns tick with the refresh loop's renders.
    [Inject]
    private AlertMonitor Alerts { get; set; } = default!;

    [Inject]
    private TimeProvider Clock { get; set; } = default!;

    // Upstream's rule: hide players the game has not named unless they already matter. Highest damage first.
    private IEnumerable<PlayerRow> Visible => snapshot.Players
        .Where(p => p.IsIdentified || p.DamageShare > 1)
        .OrderByDescending(p => p.TotalDamage);

    private string StatusText => Engine.Capture switch
    {
        { IsCapturing: false } => "Capture is off",
        { AdapterName: null } => "Waiting for the game's connection...",
        _ => "No active target",
    };

    // Background opacity, picture and row size as CSS variables on the panel (see app.css, .overlay).
    private string PanelStyle
    {
        get
        {
            var style = string.Create(CultureInfo.InvariantCulture,
                $"--panel-alpha:{appearance.BackgroundOpacity:0.###};--row-scale:{appearance.RowScale:0.###};");
            return WebAssetHosts.BackgroundUrl(appearance.BackgroundImage) is { } url ? style + $"--panel-image:url('{url}');" : style;
        }
    }

    protected override void OnInitialized()
    {
        appearance = Settings.Current.Appearance;
        Settings.Changed += OnSettingsChanged;
        Engine.PingUpdated += OnPingUpdated;
        Alerts.Changed += OnAlertsChanged;
        refreshLoop = RefreshLoopAsync();
    }

    private void OnAlertsChanged(object? sender, EventArgs e) => _ = RenderAsync();

    private string DisplayName(PlayerRow player) => appearance.HideNicknames ? NicknameMask.Mask(player.Name) : player.Name;

    private double BarPercent(PlayerRow player)
    {
        if (!appearance.RelativeBars) return player.DamageShare;
        var top = snapshot.Players.Max(p => p.TotalDamage);
        return top > 0 ? player.TotalDamage * 100.0 / top : 0;
    }

    // Class gradients (app.css .fill.class-N) when enabled; otherwise the accent, with the user in gold.
    private string FillClass(PlayerRow player) => appearance.ClassColors
        ? $"class-{(player.ClassId is >= 10 and <= 19 ? player.ClassId : 0)}{(player.IsUser ? " me-mark" : "")}"
        : player.IsUser ? "self" : string.Empty;

    private void OnHeaderMouseDown(MouseEventArgs e)
    {
        if (e.Button == 0) Windows.BeginOverlayDrag();
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

    private void OnSettingsChanged(object? sender, MeterSettings changed)
    {
        appearance = changed.Appearance;
        _ = RenderAsync();
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
        Settings.Changed -= OnSettingsChanged;
        Engine.PingUpdated -= OnPingUpdated;
        Alerts.Changed -= OnAlertsChanged;
        await stopping.CancelAsync();
        if (refreshLoop is not null) await refreshLoop;
        stopping.Dispose();
    }
}
