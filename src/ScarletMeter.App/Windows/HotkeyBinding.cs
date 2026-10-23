using ScarletMeter.App.Settings;

namespace ScarletMeter.App.Windows;

/// <summary>Something that can hold one system-wide hotkey (the overlay's <see cref="GlobalHotkey"/>, or a test fake).</summary>
public interface IHotkeyTarget
{
    /// <summary>Replaces the registered hotkey (null removes it); false with a reason when it cannot be registered.</summary>
    bool TrySet(HotkeyGesture? gesture, out string? error);
}

/// <summary>
/// Decides what the hotkey target holds: the shortcut from settings, or nothing while the settings page is
/// recording a new one (so pressing the current shortcut is captured instead of toggling the meter). It only
/// re-registers when the wanted shortcut changes, and reports why a shortcut is not active.
/// </summary>
public sealed class HotkeyBinding
{
    private readonly IHotkeyTarget target;
    private string? registered; // null: nothing applied yet, so the next Apply always registers
    private string configured = string.Empty;
    private bool suspended;

    public HotkeyBinding(IHotkeyTarget target) => this.target = target;

    /// <summary>Why the configured shortcut is not active, or null when it is (or none is set). Kept while suspended.</summary>
    public string? Error { get; private set; }

    /// <summary>Raised when <see cref="Error"/> changes.</summary>
    public event EventHandler? ErrorChanged;

    /// <summary>Applies the shortcut from settings (text form, empty for none).</summary>
    public void Apply(string text)
    {
        configured = text;
        Sync();
    }

    /// <summary>Removes the hotkey while recording, and restores the configured one afterwards.</summary>
    public void Suspend(bool value)
    {
        if (suspended == value) return;
        suspended = value;
        Sync();
    }

    private void Sync()
    {
        var wanted = suspended ? string.Empty : configured;
        if (wanted == registered) return;
        registered = wanted;

        string? error = null;
        if (HotkeyGesture.TryParse(wanted, out var gesture, out var parseError))
            target.TrySet(gesture, out error);
        else
        {
            target.TrySet(null, out _);
            error = parseError;
        }
        if (suspended || error == Error) return;
        Error = error;
        ErrorChanged?.Invoke(this, EventArgs.Empty);
    }
}
