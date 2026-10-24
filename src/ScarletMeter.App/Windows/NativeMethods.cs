using System.Runtime.InteropServices;

namespace ScarletMeter.App.Windows;

/// <summary>The few Win32 calls the windows need: moving a chromeless window, global hotkeys, dark title bars.</summary>
internal static class NativeMethods
{
    public const int WmHotkey = 0x0312;
    public const int WmNclButtonDown = 0x00A1;
    // The player starts and ends dragging or resizing a window (the system's move/size loop).
    public const int WmEnterSizeMove = 0x0231;
    public const int WmExitSizeMove = 0x0232;
    public const int HtCaption = 2;
    public const uint ModNoRepeat = 0x4000;
    public const int DwmUseImmersiveDarkMode = 20;
    public const int SwShowNoActivate = 4;

    /// <summary>Shows a window in its last size and position; with <see cref="SwShowNoActivate"/> it restores a minimized one without taking focus.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int command);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>Returns true when the window was already disabled.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnableWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool enable);

    public const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct RectInt
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfo
    {
        public int Size;
        public RectInt Monitor;
        public RectInt Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    /// <summary>Called once per monitor by <see cref="EnumDisplayMonitors"/>; return true to continue.</summary>
    public delegate bool MonitorEnumProc(IntPtr monitor, IntPtr deviceContext, IntPtr clip, IntPtr data);

    /// <summary>Lists every display monitor (with no device context or clip, the whole desktop).</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayMonitors(IntPtr deviceContext, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    /// <summary>The system DPI (96 at 100% scaling), which SystemParameters' device-independent units are based on.</summary>
    [DllImport("user32.dll")]
    public static extern uint GetDpiForSystem();

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
