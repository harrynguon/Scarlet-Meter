using ScarletMeter.App.Settings;
using ScarletMeter.App.Windows;

namespace ScarletMeter.Tests;

/// <summary>
/// The hotkey's registration decisions. Recording a new shortcut must remove the current one from the
/// system (review reproduced it staying registered and minimizing the meter mid-recording), and finishing
/// must put it back; unchanged settings must not re-register.
/// </summary>
public sealed class HotkeyBindingTests
{
    [Fact]
    public void RecordingUnregistersTheShortcutAndFinishingRestoresIt()
    {
        var target = new FakeTarget();
        var binding = new HotkeyBinding(target);
        binding.Apply("Ctrl+Shift+D");
        Assert.Equal("Ctrl+Shift+D", target.Registered);

        binding.Suspend(true);
        Assert.Null(target.Registered);

        binding.Suspend(false);
        Assert.Equal("Ctrl+Shift+D", target.Registered);
    }

    [Fact]
    public void SuspendingBeforeTheFirstApplyRegistersNothing()
    {
        var target = new FakeTarget();
        var binding = new HotkeyBinding(target);
        binding.Suspend(true);
        binding.Apply("Ctrl+Shift+D");
        Assert.Null(target.Registered);
        binding.Suspend(false);
        Assert.Equal("Ctrl+Shift+D", target.Registered);
    }

    [Fact]
    public void AnUnchangedShortcutIsNotRegisteredAgain()
    {
        var target = new FakeTarget();
        var binding = new HotkeyBinding(target);
        binding.Apply("F10");
        binding.Apply("F10");
        Assert.Equal(1, target.Calls);
        binding.Apply("Ctrl+F10");
        Assert.Equal(2, target.Calls);
        Assert.Equal("Ctrl+F10", target.Registered);
    }

    [Fact]
    public void AShortcutOwnedElsewhereIsReportedAndClearedWhenFixed()
    {
        var target = new FakeTarget { Taken = "Ctrl+Shift+D" };
        var binding = new HotkeyBinding(target);
        var changes = 0;
        binding.ErrorChanged += (_, _) => changes++;

        binding.Apply("Ctrl+Shift+D");
        Assert.Contains("another program", binding.Error);

        binding.Suspend(true); // recording keeps the error visible
        Assert.NotNull(binding.Error);
        binding.Suspend(false);

        binding.Apply("Ctrl+Alt+D");
        Assert.Null(binding.Error);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void ReservedShortcutsAreRefused()
    {
        Assert.False(HotkeyGesture.TryParse("Alt+F4", out _, out var error));
        Assert.Contains("Windows", error);
        Assert.False(HotkeyGesture.TryFromBrowserKey("KeyC", ctrl: true, alt: false, shift: false, meta: false, out _, out _));
        Assert.True(HotkeyGesture.TryParse("Ctrl+Shift+C", out _, out _));
    }

    private sealed class FakeTarget : IHotkeyTarget
    {
        public string? Taken { get; init; }

        public string? Registered { get; private set; }

        public int Calls { get; private set; }

        public bool TrySet(HotkeyGesture? gesture, out string? error)
        {
            Calls++;
            error = null;
            Registered = null;
            if (gesture is null) return true;
            if (gesture.ToString() == Taken)
            {
                error = $"{gesture} is already used by another program; choose a different shortcut.";
                return false;
            }
            Registered = gesture.ToString();
            return true;
        }
    }
}
