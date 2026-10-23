using System.Diagnostics.CodeAnalysis;

namespace ScarletMeter.App.Settings;

/// <summary>Modifier keys of a global hotkey. Values match the Win32 MOD_* flags RegisterHotKey takes.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Windows = 0x8,
}

/// <summary>
/// A global hotkey such as "Ctrl+Shift+F10": modifiers, a key name and its Win32 virtual-key code. Text
/// form is what settings.json stores; <see cref="TryFromBrowserKey"/> turns a key press captured by the
/// settings page (KeyboardEvent.code) into one. Keys other than function keys, Pause and Scroll Lock need a
/// modifier, so the overlay never steals a plain key from the game.
/// </summary>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, string Key, int VirtualKey)
{
    private static readonly (HotkeyModifiers Flag, string Name)[] ModifierNames =
    [
        (HotkeyModifiers.Control, "Ctrl"), (HotkeyModifiers.Alt, "Alt"), (HotkeyModifiers.Shift, "Shift"), (HotkeyModifiers.Windows, "Win"),
    ];

    // Display name -> virtual-key code, and the browser KeyboardEvent.code that produces it.
    private static readonly (string Name, int Vk, string Code)[] KeyTable = BuildKeyTable();

    private static readonly Dictionary<string, (string Name, int Vk)> ByName =
        KeyTable.ToDictionary(k => k.Name, k => (k.Name, k.Vk), StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, (string Name, int Vk)> ByCode =
        KeyTable.ToDictionary(k => k.Code, k => (k.Name, k.Vk), StringComparer.Ordinal);

    public override string ToString() =>
        string.Join("+", ModifierNames.Where(m => Modifiers.HasFlag(m.Flag)).Select(m => m.Name).Append(Key));

    /// <summary>Parses the stored text form. Empty or whitespace text is not a hotkey (returns false, no error).</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out HotkeyGesture? gesture, out string? error)
    {
        gesture = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var modifiers = HotkeyModifiers.None;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        foreach (var part in parts[..^1])
        {
            var flag = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => HotkeyModifiers.Control,
                "ALT" => HotkeyModifiers.Alt,
                "SHIFT" => HotkeyModifiers.Shift,
                "WIN" or "WINDOWS" => HotkeyModifiers.Windows,
                _ => HotkeyModifiers.None,
            };
            if (flag == HotkeyModifiers.None || modifiers.HasFlag(flag))
            {
                error = $"\"{part}\" is not a modifier (Ctrl, Alt, Shift, Win) or is repeated.";
                return false;
            }
            modifiers |= flag;
        }

        if (!ByName.TryGetValue(parts[^1], out var key))
        {
            error = $"\"{parts[^1]}\" is not a key the meter can use as a shortcut.";
            return false;
        }
        return TryCreate(modifiers, key.Name, key.Vk, out gesture, out error);
    }

    /// <summary>Builds a gesture from a browser key press. Returns false while only modifiers are held.</summary>
    public static bool TryFromBrowserKey(
        string? code, bool ctrl, bool alt, bool shift, bool meta,
        [NotNullWhen(true)] out HotkeyGesture? gesture, out string? error)
    {
        gesture = null;
        error = null;
        if (string.IsNullOrEmpty(code) || IsModifierCode(code)) return false;
        if (!ByCode.TryGetValue(code, out var key))
        {
            error = "That key can't be used as a shortcut.";
            return false;
        }
        var modifiers = (ctrl ? HotkeyModifiers.Control : 0) | (alt ? HotkeyModifiers.Alt : 0)
            | (shift ? HotkeyModifiers.Shift : 0) | (meta ? HotkeyModifiers.Windows : 0);
        return TryCreate(modifiers, key.Name, key.Vk, out gesture, out error);
    }

    private static bool TryCreate(HotkeyModifiers modifiers, string key, int vk,
        [NotNullWhen(true)] out HotkeyGesture? gesture, out string? error)
    {
        gesture = null;
        error = null;
        var standalone = vk is >= 0x70 and <= 0x87 or 0x13 or 0x91; // F1-F24, Pause, Scroll Lock
        if (modifiers == HotkeyModifiers.None && !standalone)
        {
            error = "Add Ctrl, Alt, Shift or Win, so the shortcut doesn't take a key from the game.";
            return false;
        }
        gesture = new HotkeyGesture(modifiers, key, vk);
        return true;
    }

    private static bool IsModifierCode(string code) => code is "ControlLeft" or "ControlRight" or "AltLeft"
        or "AltRight" or "ShiftLeft" or "ShiftRight" or "MetaLeft" or "MetaRight" or "OSLeft" or "OSRight";

    private static (string, int, string)[] BuildKeyTable()
    {
        var keys = new List<(string, int, string)>();
        for (var c = 'A'; c <= 'Z'; c++) keys.Add((c.ToString(), c, $"Key{c}"));
        for (var d = 0; d <= 9; d++) keys.Add((d.ToString(), 0x30 + d, $"Digit{d}"));
        for (var f = 1; f <= 24; f++) keys.Add(($"F{f}", 0x6F + f, $"F{f}"));
        for (var n = 0; n <= 9; n++) keys.Add(($"Num{n}", 0x60 + n, $"Numpad{n}"));
        keys.AddRange(
        [
            ("NumMultiply", 0x6A, "NumpadMultiply"), ("NumAdd", 0x6B, "NumpadAdd"), ("NumSubtract", 0x6D, "NumpadSubtract"),
            ("NumDecimal", 0x6E, "NumpadDecimal"), ("NumDivide", 0x6F, "NumpadDivide"),
            ("Space", 0x20, "Space"), ("Tab", 0x09, "Tab"), ("PageUp", 0x21, "PageUp"), ("PageDown", 0x22, "PageDown"),
            ("End", 0x23, "End"), ("Home", 0x24, "Home"), ("Left", 0x25, "ArrowLeft"), ("Up", 0x26, "ArrowUp"),
            ("Right", 0x27, "ArrowRight"), ("Down", 0x28, "ArrowDown"), ("Insert", 0x2D, "Insert"), ("Delete", 0x2E, "Delete"),
            ("Pause", 0x13, "Pause"), ("ScrollLock", 0x91, "ScrollLock"),
            ("Semicolon", 0xBA, "Semicolon"), ("Equals", 0xBB, "Equal"), ("Comma", 0xBC, "Comma"), ("Minus", 0xBD, "Minus"),
            ("Period", 0xBE, "Period"), ("Slash", 0xBF, "Slash"), ("Backquote", 0xC0, "Backquote"),
            ("BracketLeft", 0xDB, "BracketLeft"), ("Backslash", 0xDC, "Backslash"), ("BracketRight", 0xDD, "BracketRight"),
            ("Quote", 0xDE, "Quote"),
        ]);
        return [.. keys];
    }
}
