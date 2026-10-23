using System.Windows;
using System.Windows.Interop;
using ScarletMeter.App.Settings;

namespace ScarletMeter.App.Windows;

/// <summary>
/// One system-wide hotkey registered on a window (RegisterHotKey), raising <see cref="Pressed"/> on that
/// window's UI thread. Auto-repeat is suppressed, so holding the keys toggles once.
/// </summary>
internal sealed class GlobalHotkey : IDisposable
{
    private const int HotkeyId = 0x5C41;

    private readonly IntPtr handle;
    private readonly HwndSource source;
    private bool registered;

    /// <param name="window">A window whose handle exists (call after SourceInitialized).</param>
    public GlobalHotkey(Window window)
    {
        handle = new WindowInteropHelper(window).Handle;
        source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("The window has no handle yet.");
        source.AddHook(Hook);
    }

    public event EventHandler? Pressed;

    /// <summary>The registered gesture, or null.</summary>
    public HotkeyGesture? Current { get; private set; }

    /// <summary>
    /// Replaces the registered hotkey (null removes it). Returns false, with a message, when another program
    /// already owns the combination; the meter then has no hotkey until a different one is chosen.
    /// </summary>
    public bool TrySet(HotkeyGesture? gesture, out string? error)
    {
        error = null;
        Unregister();
        if (gesture is null) return true;
        if (!NativeMethods.RegisterHotKey(handle, HotkeyId, (uint)gesture.Modifiers | NativeMethods.ModNoRepeat, (uint)gesture.VirtualKey))
        {
            error = $"{gesture} is already used by another program; choose a different shortcut.";
            return false;
        }
        registered = true;
        Current = gesture;
        return true;
    }

    public void Dispose()
    {
        Unregister();
        source.RemoveHook(Hook);
    }

    private void Unregister()
    {
        if (registered) NativeMethods.UnregisterHotKey(handle, HotkeyId);
        registered = false;
        Current = null;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }
}
