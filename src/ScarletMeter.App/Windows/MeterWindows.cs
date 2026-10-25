using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScarletMeter.App.Alerts;
using ScarletMeter.App.Pages;
using ScarletMeter.App.Settings;
using ScarletMeter.Engine;

namespace ScarletMeter.App.Windows;

/// <summary>What the pages may ask of the windows. Every member must be called on the UI thread.</summary>
public interface IMeterWindows
{
    /// <summary>Opens the settings window, or brings it forward.</summary>
    void OpenSettings();

    /// <summary>Opens the history window, or brings it forward.</summary>
    void OpenHistory();

    /// <summary>Shows a player in the details window: from the live fight, or from a saved fight when <paramref name="fightId"/> is given.</summary>
    void OpenPlayerDetails(long playerId, Guid? fightId = null);

    /// <summary>Opens the boss-timers window (aion2timers.com, <see cref="BossTimersWindow"/>), or brings it forward.</summary>
    void OpenBossTimers();

    /// <summary>Puts text on the clipboard.</summary>
    void CopyText(string text);

    /// <summary>Starts dragging the overlay; call from a left-button press on its header.</summary>
    void BeginOverlayDrag();

    /// <summary>Minimizes the overlay and, with it, every tool window.</summary>
    void MinimizeOverlay();

    /// <summary>Minimizes everything, or restores everything when the overlay is minimized (the hotkey's action).</summary>
    void ToggleOverlay();

    /// <summary>Closes the meter: tool windows first, then the overlay, which ends the app.</summary>
    void CloseMeter();

    /// <summary>Unregisters the hotkey while the settings page records a new one, so pressing the old one does not fire.</summary>
    void SuspendHotkey(bool suspended);

    /// <summary>Asks for a picture file; returns its absolute path, or null when cancelled.</summary>
    Task<string?> PickPictureAsync();

    /// <summary>Opens the data directory in Explorer.</summary>
    void OpenDataFolder();

    /// <summary>Opens a RATmeter release page in the default browser; any other address is refused.</summary>
    void OpenReleasePage(Uri page);

    /// <summary>Why the configured hotkey is not active, or null when it is (or none is set).</summary>
    string? HotkeyError { get; }

    /// <summary>Raised when <see cref="HotkeyError"/> changes.</summary>
    event EventHandler? HotkeyStatusChanged;

    /// <summary>
    /// The overlay's world-event notices stand aside for a boss fight, or come back; bringing a minimized meter back
    /// for an event waits until they do. Cheap to call on every refresh.
    /// </summary>
    void HoldAlerts(bool held);

    /// <summary>
    /// The overlay page's heights (CSS pixels): <paramref name="contentHeight"/>, which the window is as tall as;
    /// <paramref name="emptyHeight"/>, its content with no rows; and <paramref name="rowStep"/>, a row with its gap,
    /// by which a resize of the bar area counts rows (<see cref="OverlayFit"/>).
    /// </summary>
    void FitOverlayToContent(double contentHeight, double emptyHeight, double rowStep);

    /// <summary>
    /// Whether the overlay window is shorter than content <paramref name="contentHeight"/> tall: it is as tall as the
    /// screen allows. The page's rows then give way and scroll (app.css, .rows.scrolls), never the notices. Cheap to
    /// call on every render.
    /// </summary>
    bool OverlayCutsContent(double contentHeight);

    /// <summary>Whether the player is dragging an edge of the overlay: the page lets its bars fill the window meanwhile.</summary>
    bool OverlayResizing { get; }

    /// <summary>
    /// Raised on the UI thread after the overlay window moved or resized itself to fit its content, or a resize of an
    /// edge started or ended.
    /// </summary>
    event EventHandler? OverlayFitted;
}

/// <summary>
/// Creates and tracks the overlay and the tool windows (one of each kind), and owns the global hotkey,
/// which needs the overlay's window handle and follows the hotkey setting.
/// </summary>
public sealed class MeterWindows : IMeterWindows
{
    /// <summary>The tool windows this build has.</summary>
    public static readonly ToolWindowSpec Settings = new(WindowKeys.Settings, typeof(SettingsPage), "Scarlet Meter · Settings", 540, 660, 420, 360);
    public static readonly ToolWindowSpec History = new(WindowKeys.History, typeof(FightHistoryPage), "Scarlet Meter · History", 860, 620, 560, 360);
    public static readonly ToolWindowSpec PlayerDetails = new(WindowKeys.PlayerDetails, typeof(PlayerDetailsPage), "Scarlet Meter · Player details", 980, 600, 620, 360);

    private readonly IServiceProvider services;
    private readonly SettingsService settings;
    private readonly ILogger<MeterWindows> logger;
    private readonly Dictionary<string, ToolWindow> toolWindows = new(StringComparer.Ordinal);
    private OverlayWindow? overlay;
    private BossTimersWindow? bossTimers;
    private GlobalHotkey? hotkey;
    private HotkeyBinding? hotkeyBinding;
    private bool hotkeySuspended;
    private bool modalOpen;
    private bool closing;
    // "Bring the meter back before an event": the notices it follows, and whether a boss fight holds it back.
    private readonly AlertRestoreGate restoreGate = new();
    private AlertMonitor? alertMonitor;

    public MeterWindows(IServiceProvider services, SettingsService settings, ILogger<MeterWindows> logger)
    {
        this.services = services;
        this.settings = settings;
        this.logger = logger;
    }

    public string? HotkeyError => hotkeyBinding?.Error;

    public event EventHandler? HotkeyStatusChanged;

    public event EventHandler? OverlayFitted;

    /// <summary>Creates and shows the overlay. The app shuts down when it closes.</summary>
    public OverlayWindow ShowOverlay()
    {
        overlay = new OverlayWindow(services, CloseToolWindowsAsync) { CloseBlocked = () => modalOpen };
        overlay.Fitted += (_, _) => OverlayFitted?.Invoke(this, EventArgs.Empty);
        overlay.SourceInitialized += (_, _) =>
        {
            hotkey = new GlobalHotkey(overlay);
            hotkey.Pressed += (_, _) => ToggleOverlay();
            hotkeyBinding = new HotkeyBinding(hotkey);
            hotkeyBinding.ErrorChanged += (_, _) =>
            {
                if (hotkeyBinding.Error is { } error) logger.LogWarning("Hotkey not registered: {Error}", error);
                HotkeyStatusChanged?.Invoke(this, EventArgs.Empty);
            };
            hotkeyBinding.Suspend(hotkeySuspended);
            hotkeyBinding.Apply(settings.Current.Overlay.ToggleHotkey);
        };
        settings.Changed += OnSettingsChanged;
        overlay.Closed += (_, _) =>
        {
            settings.Changed -= OnSettingsChanged;
            hotkey?.Dispose();
            hotkey = null;
            hotkeyBinding = null;
        };
        overlay.Show();
        return overlay;
    }

    public void OpenSettings() => Open(Settings);

    public void OpenHistory() => Open(History);

    public void OpenPlayerDetails(long playerId, Guid? fightId = null)
    {
        services.GetRequiredService<DetailsSelection>().Select(new DetailsSource(playerId, fightId));
        Open(PlayerDetails);
    }

    // One window at a time; not owned by the overlay, so it can sit behind the game. Nothing once the meter is closing.
    public void OpenBossTimers()
    {
        if (closing) return;
        if (bossTimers is { } open)
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }
        var window = new BossTimersWindow(services, overlay);
        bossTimers = window;
        window.Closed += (_, _) => bossTimers = null;
        window.Show();
    }

    public void CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            // Another program holds the clipboard open.
            logger.LogWarning(ex, "Copying to the clipboard failed.");
        }
    }

    /// <summary>
    /// Opens a tool window of the given kind, or restores and activates the open one. Does nothing once the
    /// meter is closing, so no window is created after the overlay has collected the ones to close.
    /// </summary>
    public ToolWindow? Open(ToolWindowSpec spec)
    {
        if (closing) return null;
        if (toolWindows.TryGetValue(spec.Key, out var open))
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return open;
        }
        var window = new ToolWindow(services, spec, overlay) { CloseBlocked = () => modalOpen };
        toolWindows[spec.Key] = window;
        window.Closed += (_, _) => toolWindows.Remove(spec.Key);
        window.Show();
        return window;
    }

    public void BeginOverlayDrag() => overlay?.BeginDrag();

    public void FitOverlayToContent(double contentHeight, double emptyHeight, double rowStep)
    {
        if (!closing) overlay?.FitToContent(contentHeight, emptyHeight, rowStep);
    }

    public bool OverlayCutsContent(double contentHeight) => !closing && overlay?.CutsContent(contentHeight) == true;

    public bool OverlayResizing => !closing && overlay?.Resizing == true;

    public void MinimizeOverlay()
    {
        if (overlay is not null && !closing && !modalOpen) overlay.WindowState = WindowState.Minimized;
    }

    public void ToggleOverlay()
    {
        if (overlay is null || closing || modalOpen) return;
        if (overlay.WindowState == WindowState.Minimized)
        {
            overlay.WindowState = WindowState.Normal;
            overlay.Activate();
        }
        else
        {
            overlay.WindowState = WindowState.Minimized;
        }
    }

    public void CloseMeter()
    {
        if (!modalOpen) overlay?.Close();
    }

    /// <summary>
    /// Brings a minimized meter back once per event occurrence, when its Soon notice appears (if the player
    /// wants that), without activating it, so the game keeps keyboard focus. Owned tool windows reappear with
    /// the overlay. While the notices stand aside for a boss fight it waits until they come back
    /// (<see cref="AlertRestoreGate"/>).
    /// </summary>
    public void FollowAlerts(AlertMonitor alerts)
    {
        alertMonitor = alerts;
        alerts.EventApproaching += (_, _) => Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (restoreGate.Approaching()) RestoreForAlert();
        });
        alerts.Start();
    }

    public void HoldAlerts(bool held)
    {
        if (restoreGate.Hold(held, alertMonitor?.Current.Alerts.Count > 0)) RestoreForAlert();
    }

    // Shows the minimized overlay without activating it, if the player wants that and nothing forbids it now.
    private void RestoreForAlert()
    {
        if (overlay is null || closing || modalOpen || overlay.WindowState != WindowState.Minimized) return;
        if (!settings.Current.Alerts.RestoreMinimizedMeter) return;
        var handle = new System.Windows.Interop.WindowInteropHelper(overlay).Handle;
        if (handle != IntPtr.Zero) NativeMethods.ShowWindow(handle, NativeMethods.SwShowNoActivate);
    }

    public void SuspendHotkey(bool suspended)
    {
        hotkeySuspended = suspended;
        hotkeyBinding?.Suspend(suspended);
    }

    // The dialog runs from its own dispatcher operation, not inside the Blazor event handler that asked for
    // it, so its modal loop cannot re-enter the renderer that is dispatching the click. While it is open every
    // Scarlet window is disabled and refuses to close: closing one would release Blazor content under a
    // modal loop, which terminated the process (reproduced in review).
    public async Task<string?> PickPictureAsync()
    {
        if (closing || modalOpen) return null;
        var owner = toolWindows.GetValueOrDefault(Settings.Key) ?? (Window?)overlay;
        modalOpen = true;
        var disabled = new List<IntPtr>();
        try
        {
            return await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                foreach (var window in AllWindows().Where(w => w != owner))
                {
                    var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                    if (handle != IntPtr.Zero && !NativeMethods.EnableWindow(handle, false)) disabled.Add(handle);
                }
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Choose a background picture",
                    Filter = "Pictures|" + string.Join(';', BackgroundImages.Extensions.Select(e => "*" + e)),
                    CheckFileExists = true,
                    Multiselect = false,
                };
                return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
        finally
        {
            foreach (var handle in disabled) NativeMethods.EnableWindow(handle, true);
            modalOpen = false;
        }
    }

    private IEnumerable<Window> AllWindows() => toolWindows.Values.Cast<Window>().Prepend(overlay).OfType<Window>();

    // Only https pages under upstream's releases on GitHub: the checker validated it already, and the shell
    // must never be handed anything else from a network answer.
    public void OpenReleasePage(Uri page)
    {
        if (!IsUpstreamReleasePage(page))
        {
            logger.LogWarning("Refused to open a page that is not a RATmeter release page."); // the address is untrusted: not logged
            return;
        }
        DefaultBrowser.Open(page, logger);
    }

    /// <summary>
    /// True for an https page under https://github.com/Kuroukihime/AIon2-Dps-Meter/releases/ with no port,
    /// credentials (not even an empty "@"), query or fragment. Uri resolves dot segments, percent-encoded ones
    /// included, before the prefix check, so "releases/../../other" fails it; a ".." Uri leaves in the path (around
    /// an encoded slash) is refused too.
    /// </summary>
    public static bool IsUpstreamReleasePage(Uri page) =>
        page.IsAbsoluteUri && page.Scheme == Uri.UriSchemeHttps && page.IsDefaultPort
        && page.UserInfo.Length == 0 && !page.OriginalString.Contains('@')
        && string.Equals(page.Host, "github.com", StringComparison.OrdinalIgnoreCase)
        && page.AbsolutePath.StartsWith("/Kuroukihime/AIon2-Dps-Meter/releases/", StringComparison.OrdinalIgnoreCase)
        && !page.AbsolutePath.Contains("..", StringComparison.Ordinal)
        && page.Query.Length == 0 && page.Fragment.Length == 0;

    public void OpenDataFolder()
    {
        var folder = services.GetRequiredService<EngineOptions>().DataDirectory;
        try
        {
            using var _ = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                ArgumentList = { folder },
                UseShellExecute = false,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(ex, "Could not open the data folder.");
        }
    }

    // Closes each tool window through its own deferred close and waits for all of them, so none is left
    // with live Blazor content when the overlay (and the app) goes away.
    private async ValueTask CloseToolWindowsAsync()
    {
        closing = true;
        // The boss-timers window has no Blazor content to release: it simply closes, disposing its browser view.
        bossTimers?.Close();
        var windows = toolWindows.Values.ToList();
        foreach (var window in windows) window.Close();
        try
        {
            await Task.WhenAll(windows.Select(w => w.CloseCompletion)).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Tool windows did not finish closing within five seconds; closing the overlay anyway.");
        }
    }

    private void OnSettingsChanged(object? sender, MeterSettings changed) => hotkeyBinding?.Apply(changed.Overlay.ToggleHotkey);
}
