using System.IO;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using ScarletMeter.App.Settings;
using ScarletMeter.App.Windows;
using ScarletMeter.Engine;

namespace ScarletMeter.App.Pages;

/// <summary>
/// The settings window. Every control writes through <see cref="SettingsService.Update"/>, so values are
/// validated, applied live and saved; the page re-renders when settings change anywhere (a window move,
/// another control).
/// </summary>
public partial class SettingsPage : IDisposable
{
    private static readonly string[] Tabs = ["Appearance", "Tracking", "Overlay", "About"];

    private string current = Tabs[0];
    private string? pictureError;

    [Inject] private SettingsService Settings { get; set; } = default!;

    [Inject] private IMeterWindows Windows { get; set; } = default!;

    [Inject] private EngineOptions Options { get; set; } = default!;

    [Inject] private ILogger<SettingsPage> Logger { get; set; } = default!;

    private MeterSettings S => Settings.Current;

    private string DataDirectory => Options.DataDirectory;

    private static string Version =>
        typeof(SettingsPage).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";

    protected override void OnInitialized() => Settings.Changed += OnSettingsChanged;

    private void Change(Func<MeterSettings, MeterSettings> change) => Settings.Update(change);

    private void OnRetentionChanged(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var days))
            Change(s => s with { Fight = s.Fight with { HistoryRetentionDays = days } });
    }

    private async Task ChoosePicture()
    {
        pictureError = null;
        var path = await Windows.PickPictureAsync();
        if (path is null) return;
        try
        {
            var stored = BackgroundImages.Import(path, Options.DataDirectory);
            Change(s => s with { Appearance = s.Appearance with { BackgroundImage = stored } });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            Logger.LogWarning(ex, "Importing a background picture failed.");
            pictureError = ex.Message;
        }
    }

    private void RemovePicture()
    {
        pictureError = null;
        Change(s => s with { Appearance = s.Appearance with { BackgroundImage = null } });
        BackgroundImages.RemoveExcept(BackgroundImages.Folder(Options.DataDirectory), keep: null);
    }

    private void OnSettingsChanged(object? sender, MeterSettings changed) => _ = InvokeAsync(StateHasChanged);

    public void Dispose() => Settings.Changed -= OnSettingsChanged;
}
