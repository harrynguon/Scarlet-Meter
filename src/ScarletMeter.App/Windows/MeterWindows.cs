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
    private GlobalHotkey? hotkey;
    private HotkeyBinding? hotkeyBinding;
    private bool hotkeySuspended;
    private bool modalOpen;
    private bool closing;

    public MeterWindows(IServiceProvider services, SettingsService settings, ILogger<MeterWindows> logger)
    {
        this.services = services;
        this.settings = settings;
        this.logger = logger;
    }

    public string? HotkeyError => hotkeyBinding?.Error;

    public event EventHandler? HotkeyStatusChanged;

    /// <summary>Creates and shows the overlay. The app shuts down when it closes.</summary>
    public OverlayWindow ShowOverlay()
    {
        overlay = new OverlayWindow(services, CloseToolWindowsAsync) { CloseBlocked = () => modalOpen };
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
    /// the overlay.
    /// </summary>
    public void FollowAlerts(AlertMonitor alerts)
    {
        alerts.EventApproaching += (_, _) => Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (overlay is null || closing || modalOpen || overlay.WindowState != WindowState.Minimized) return;
            if (!settings.Current.Alerts.RestoreMinimizedMeter) return;
            var handle = new System.Windows.Interop.WindowInteropHelper(overlay).Handle;
            if (handle != IntPtr.Zero) NativeMethods.ShowWindow(handle, NativeMethods.SwShowNoActivate);
        });
        alerts.Start();
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
        try
        {
            using var _ = Process.Start(new ProcessStartInfo { FileName = page.AbsoluteUri, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(ex, "Opening the release page failed.");
        }
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
