using System.Runtime.InteropServices;

namespace ScarletMeter.App.Windows;

/// <summary>The few Win32 calls the windows need: moving a chromeless window, global hotkeys, dark title bars.</summary>
internal static class NativeMethods
{
    public const int WmHotkey = 0x0312;
    public const int WmNclButtonDown = 0x00A1;
    public const int HtCaption = 2;
    public const uint ModNoRepeat = 0x4000;
    public const int DwmUseImmersiveDarkMode = 20;

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

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
