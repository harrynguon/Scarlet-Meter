using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScarletMeter.App.Pages;
using ScarletMeter.App.Settings;
using ScarletMeter.Engine;

namespace ScarletMeter.App.Windows;

/// <summary>What the pages may ask of the windows. Every member must be called on the UI thread.</summary>
public interface IMeterWindows
{
    /// <summary>Opens the settings window, or brings it forward.</summary>
    void OpenSettings();

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

    private readonly IServiceProvider services;
    private readonly SettingsService settings;
    private readonly ILogger<MeterWindows> logger;
    private readonly Dictionary<string, ToolWindow> toolWindows = new(StringComparer.Ordinal);
    private OverlayWindow? overlay;
    private GlobalHotkey? hotkey;
    private string registeredHotkey = string.Empty;
    private bool hotkeySuspended;

    public MeterWindows(IServiceProvider services, SettingsService settings, ILogger<MeterWindows> logger)
    {
        this.services = services;
        this.settings = settings;
        this.logger = logger;
    }

    public string? HotkeyError { get; private set; }

    public event EventHandler? HotkeyStatusChanged;

    /// <summary>Creates and shows the overlay. The app shuts down when it closes.</summary>
    public OverlayWindow ShowOverlay()
    {
        overlay = new OverlayWindow(services, CloseToolWindowsAsync);
        overlay.SourceInitialized += (_, _) =>
        {
            hotkey = new GlobalHotkey(overlay);
            hotkey.Pressed += (_, _) => ToggleOverlay();
            ApplyHotkey(settings.Current.Overlay.ToggleHotkey);
        };
        settings.Changed += OnSettingsChanged;
        overlay.Closed += (_, _) =>
        {
            settings.Changed -= OnSettingsChanged;
            hotkey?.Dispose();
            hotkey = null;
        };
        overlay.Show();
        return overlay;
    }

    public void OpenSettings() => Open(Settings);

    /// <summary>Opens a tool window of the given kind, or restores and activates the open one.</summary>
    public ToolWindow Open(ToolWindowSpec spec)
    {
        if (toolWindows.TryGetValue(spec.Key, out var open))
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return open;
        }
        var window = new ToolWindow(services, spec, overlay);
        toolWindows[spec.Key] = window;
        window.Closed += (_, _) => toolWindows.Remove(spec.Key);
        window.Show();
        return window;
    }

    public void BeginOverlayDrag() => overlay?.BeginDrag();

    public void MinimizeOverlay()
    {
        if (overlay is not null) overlay.WindowState = WindowState.Minimized;
    }

    public void ToggleOverlay()
    {
        if (overlay is null) return;
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

    public void CloseMeter() => overlay?.Close();

    public void SuspendHotkey(bool suspended)
    {
        if (hotkeySuspended == suspended) return;
        hotkeySuspended = suspended;
        registeredHotkey = string.Empty; // force re-registration
        ApplyHotkey(settings.Current.Overlay.ToggleHotkey);
    }

    // The dialog runs from its own dispatcher operation, not inside the Blazor event handler that asked for
    // it, so its modal loop cannot re-enter the renderer that is dispatching the click.
    public async Task<string?> PickPictureAsync()
    {
        var owner = toolWindows.GetValueOrDefault(Settings.Key) ?? (Window?)overlay;
        return await Application.Current.Dispatcher.InvokeAsync(() =>
        {
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
        var closing = toolWindows.Values.ToList();
        foreach (var window in closing) window.Close();
        try
        {
            await Task.WhenAll(closing.Select(w => w.CloseCompletion)).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Tool windows did not finish closing within five seconds; closing the overlay anyway.");
        }
    }

    private void OnSettingsChanged(object? sender, MeterSettings changed) => ApplyHotkey(changed.Overlay.ToggleHotkey);

    private void ApplyHotkey(string text)
    {
        if (hotkey is null) return;
        var wanted = hotkeySuspended ? string.Empty : text;
        if (wanted == registeredHotkey) return;
        registeredHotkey = wanted;

        string? error = null;
        if (HotkeyGesture.TryParse(wanted, out var gesture, out var parseError))
            hotkey.TrySet(gesture, out error);
        else
        {
            hotkey.TrySet(null, out _);
            error = parseError;
        }
        if (error is not null && !hotkeySuspended) logger.LogWarning("Hotkey not registered: {Error}", error);
        if (!hotkeySuspended && error != HotkeyError)
        {
            HotkeyError = error;
            HotkeyStatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
