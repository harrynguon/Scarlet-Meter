using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using ScarletMeter.App.Settings;
using ScarletMeter.Engine;

namespace ScarletMeter.Tests;

/// <summary>
/// Settings validation, durable storage and live application. The file is untrusted input: out-of-range
/// values are repaired, unreadable files are kept aside (never deleted) before the backup or defaults are
/// used, and a file from a newer build is never overwritten.
/// </summary>
public sealed class SettingsTests : IDisposable
{
    private readonly TestDataDirectory directory = new();

    public SettingsTests() => Directory.CreateDirectory(directory.FullPath);

    [Fact]
    public void DefaultsNeedNoRepair()
    {
        var (settings, repairs) = SettingsNormalizer.Normalize(null);
        Assert.Empty(repairs);
        Assert.Equal(new MeterSettings(), settings);
        Assert.True(settings.Fight.GroupSummonDamage);
        Assert.Equal(30, settings.Fight.HistoryRetentionDays);
        Assert.Equal("Ctrl+Shift+D", settings.Overlay.ToggleHotkey);
    }

    [Fact]
    public void AClearedHotkeyStaysClearedAcrossSaves()
    {
        var store = NewStore();
        store.Save(new MeterSettings { Overlay = new OverlaySettings { ToggleHotkey = string.Empty } });
        Assert.Equal(string.Empty, store.Load().Settings.Overlay.ToggleHotkey);
    }

    [Fact]
    public void OutOfRangeValuesAreRepairedAndReported()
    {
        var input = new MeterSettings
        {
            Fight = new FightSettings { HistoryRetentionDays = 0 },
            Appearance = new AppearanceSettings { BackgroundOpacity = double.NaN, PictureOpacity = -2, RowScale = 9, BackgroundImage = @"..\..\secret.png" },
            Overlay = new OverlaySettings { ToggleHotkey = "Ctrl+Nope" },
            Windows = new Dictionary<string, WindowPlacement>
            {
                [WindowKeys.Overlay] = new(10, 20, 400, 260),
                ["somewhere-else"] = new(0, 0, 400, 300),
                [WindowKeys.Settings] = new(double.PositiveInfinity, 0, 400, 300),
                [WindowKeys.History] = new(0, 0, 5, 300),
            },
        };

        var (settings, repairs) = SettingsNormalizer.Normalize(input);

        Assert.Equal(1, settings.Fight.HistoryRetentionDays);
        Assert.Equal(0.9, settings.Appearance.BackgroundOpacity);
        Assert.Equal(AppearanceSettings.MinPictureOpacity, settings.Appearance.PictureOpacity);
        Assert.Equal(AppearanceSettings.MaxRowScale, settings.Appearance.RowScale);
        Assert.Null(settings.Appearance.BackgroundImage);
        Assert.Equal(string.Empty, settings.Overlay.ToggleHotkey);
        Assert.Equal(new[] { WindowKeys.Overlay }, settings.Windows.Keys);
        Assert.Equal(9, repairs.Count);

        // Picture opacity's other edges: too high is clamped to 100%, not finite falls back to 100%, each reported.
        foreach (var bad in new[] { 5.0, double.PositiveInfinity })
        {
            var (repaired, notes) = SettingsNormalizer.Normalize(new MeterSettings { Appearance = new AppearanceSettings { PictureOpacity = bad } });
            Assert.Equal(1.0, repaired.Appearance.PictureOpacity);
            Assert.Contains(notes, n => n.StartsWith("Picture opacity", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void NullSectionsFromAFileTakeDefaults()
    {
        File.WriteAllText(Path.Combine(directory.FullPath, "settings.json"),
            """{ "version": 1, "fight": null, "appearance": { "classColors": false }, "windows": null }""");
        var result = NewStore().Load();
        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(new FightSettings(), result.Settings.Fight);
        Assert.False(result.Settings.Appearance.ClassColors);
        Assert.Equal(0.9, result.Settings.Appearance.BackgroundOpacity); // missing field keeps its default
        Assert.Empty(result.Settings.Windows);
    }

    [Fact]
    public void AFileFromBeforeAlertsLoadsTheirDefaultsWithoutRepair()
    {
        File.WriteAllText(Path.Combine(directory.FullPath, "settings.json"),
            """{ "version": 1, "fight": { "bossOnlyCapture": true }, "appearance": {}, "overlay": { "toggleHotkey": "F9" }, "windows": {} }""");
        var result = NewStore().Load();
        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Null(result.Notice);
        Assert.Equal(new AlertSettings(), result.Settings.Alerts);
        Assert.False(result.Settings.Alerts.RestoreMinimizedMeter); // an upgrade never starts restoring a hidden meter
        Assert.True(result.Settings.Fight.BossOnlyCapture);
    }

    // Every on/off setting in every section survives a save and a load when flipped from its default, so a switch
    // that is never written ([JsonIgnore], no setter) fails here, not in the player's hands (LESSONS, "Switches that
    // were never saved").
    [Fact]
    public void EveryOnOffSettingSurvivesASave()
    {
        var store = NewStore();
        var settings = new MeterSettings();
        var flipped = new List<(PropertyInfo Section, PropertyInfo Flag, bool Value)>();
        foreach (var section in typeof(MeterSettings).GetProperties()
                     .Where(p => p.CanWrite && p.PropertyType.Namespace == typeof(MeterSettings).Namespace && p.PropertyType.IsClass))
        {
            var value = Activator.CreateInstance(section.PropertyType)!;
            foreach (var flag in section.PropertyType.GetProperties().Where(p => p.PropertyType == typeof(bool)))
            {
                Assert.True(flag.CanWrite, $"{section.Name}.{flag.Name} has no setter, so it is never loaded");
                var on = !(bool)flag.GetValue(value)!;
                flag.SetValue(value, on);
                flipped.Add((section, flag, on));
            }
            section.SetValue(settings, value);
        }
        Assert.Contains(flipped, f => f.Flag.Name == nameof(AlertSettings.HideDuringBossFights));
        Assert.Contains(flipped, f => f.Flag.Name == nameof(AppearanceSettings.BarEffects));

        store.Save(settings);
        var loaded = store.Load().Settings;
        foreach (var (section, flag, value) in flipped)
            Assert.True(value == (bool)flag.GetValue(section.GetValue(loaded))!, $"{section.Name}.{flag.Name} was not saved");
    }

    // "Hide during boss fights" is on for a file from before it, and turning it off survives a save and a load.
    [Fact]
    public void HidingDuringBossFightsStartsOnAndATurnedOffChoiceIsSaved()
    {
        var store = NewStore();
        Directory.CreateDirectory(directory.FullPath); // TestDataDirectory names the folder; the test makes it
        File.WriteAllText(store.FilePath, """{ "version": 1, "alerts": { "leadMinutes": 15 } }""");

        var loaded = store.Load().Settings;
        Assert.True(loaded.Alerts.HideDuringBossFights);
        Assert.Equal(15, loaded.Alerts.LeadMinutes);

        var off = loaded with { Alerts = loaded.Alerts with { HideDuringBossFights = false } };
        store.Save(off);
        var reloaded = store.Load().Settings;
        Assert.False(reloaded.Alerts.HideDuringBossFights);
        Assert.Equal(off, reloaded);
    }

    [Fact]
    public void AFileSavedWithAByteOrderMarkLoads()
    {
        var json = """{ "version": 1, "alerts": { "leadMinutes": 20 } }""";
        File.WriteAllText(Path.Combine(directory.FullPath, "settings.json"), json, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var result = NewStore().Load();
        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(20, result.Settings.Alerts.LeadMinutes);
    }

    [Fact]
    public void ANullAlertEventListTakesTheDefaults()
    {
        File.WriteAllText(Path.Combine(directory.FullPath, "settings.json"),
            """{ "version": 1, "alerts": { "events": null, "leadMinutes": 10 } }""");
        var result = NewStore().Load();
        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(AlertSettings.DefaultEvents, result.Settings.Alerts.Events);
        Assert.Equal(10, result.Settings.Alerts.LeadMinutes);
    }

    [Fact]
    public void HotkeysAreStoredInCanonicalForm()
    {
        var settings = SettingsNormalizer.Normalize(new MeterSettings { Overlay = new OverlaySettings { ToggleHotkey = " shift + ctrl + d " } }).Settings;
        Assert.Equal("Ctrl+Shift+D", settings.Overlay.ToggleHotkey);
    }

    [Fact]
    public void EqualityCoversEveryProperty()
    {
        // Adding a property to MeterSettings must extend its Equals; this count is the reminder.
        var properties = typeof(MeterSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract").Select(p => p.Name).Order().ToList();
        Assert.Equal(new[] { "Alerts", "Appearance", "Fight", "Overlay", "Updates", "Version", "Windows" }, properties);

        var baseline = SettingsNormalizer.Normalize(null).Settings with
        {
            Windows = new Dictionary<string, WindowPlacement> { [WindowKeys.Overlay] = new(1, 2, 300, 200) },
        };
        var same = baseline with { Windows = new Dictionary<string, WindowPlacement> { [WindowKeys.Overlay] = new(1, 2, 300, 200) } };
        Assert.Equal(baseline, same);
        Assert.NotEqual(baseline, baseline with { Version = 2 });
        Assert.NotEqual(baseline, baseline with { Fight = baseline.Fight with { BossOnlyCapture = true } });
        Assert.NotEqual(baseline, baseline with { Appearance = baseline.Appearance with { RowScale = 1.2 } });
        Assert.NotEqual(baseline, baseline with { Overlay = baseline.Overlay with { ToggleHotkey = "F10" } });
        // Every AlertSettings member, since it has a hand-written Equals too.
        var alertProperties = typeof(AlertSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract").Select(p => p.Name).Order().ToList();
        Assert.Equal(new[] { "Enabled", "Events", "HideDuringBossFights", "LeadMinutes", "RestoreMinimizedMeter", "ServerOffsetHours", "ShowNextEvent" }, alertProperties);
        var alerts = baseline.Alerts;
        Assert.Equal(alerts, alerts with { Events = alerts.Events.ToList() });
        Assert.NotEqual(alerts, alerts with { Enabled = !alerts.Enabled });
        Assert.NotEqual(alerts, alerts with { HideDuringBossFights = !alerts.HideDuringBossFights });
        Assert.NotEqual(alerts, alerts with { Events = alerts.Events.Skip(1).ToList() });
        Assert.NotEqual(alerts, alerts with { LeadMinutes = 7 });
        Assert.NotEqual(alerts, alerts with { RestoreMinimizedMeter = !alerts.RestoreMinimizedMeter });
        Assert.NotEqual(alerts, alerts with { ServerOffsetHours = 8 });
        Assert.NotEqual(alerts, alerts with { ShowNextEvent = !alerts.ShowNextEvent });
        Assert.NotEqual(baseline, baseline with { Alerts = alerts with { LeadMinutes = 7 } });
        Assert.NotEqual(baseline, baseline with { Updates = baseline.Updates with { CheckUpstreamReleases = false } });
        Assert.NotEqual(baseline, baseline with { Updates = baseline.Updates with { DismissedRelease = "v1.11.6.350" } });
        Assert.NotEqual(baseline, baseline with { Windows = new Dictionary<string, WindowPlacement> { [WindowKeys.Overlay] = new(1, 3, 300, 200) } });
    }

    [Fact]
    public void SaveThenLoadRoundTripsAndKeepsThePreviousSave()
    {
        var store = NewStore();
        Assert.Equal(SettingsLoadStatus.Defaults, store.Load().Status);

        var first = new MeterSettings { Appearance = new AppearanceSettings { HideNicknames = true } };
        store.Save(first);
        var second = first with { Fight = new FightSettings { BossOnlyCapture = true } };
        store.Save(second);

        var loaded = store.Load();
        Assert.Equal(SettingsLoadStatus.Loaded, loaded.Status);
        Assert.True(loaded.CanSave);
        Assert.Null(loaded.Notice);
        Assert.Equal(second, loaded.Settings);
        Assert.True(File.Exists(store.BackupPath));
        Assert.False(File.Exists(store.FilePath + ".tmp"));
    }

    [Fact]
    public void AnUnreadableFileIsKeptAsideAndThePreviousSaveRestored()
    {
        var store = NewStore();
        var saved = new MeterSettings { Appearance = new AppearanceSettings { ShowDeaths = false } };
        store.Save(saved);
        store.Save(saved); // the second save leaves the first as settings.json.bak
        File.WriteAllText(store.FilePath, "{ this is not json");

        var result = store.Load();

        Assert.Equal(SettingsLoadStatus.RecoveredFromBackup, result.Status);
        Assert.Equal(saved, result.Settings);
        Assert.True(result.CanSave);
        Assert.Contains("previous save was restored", result.Notice);
        var kept = Assert.Single(Directory.GetFiles(directory.FullPath, "settings.unreadable-*.json"));
        Assert.Equal("{ this is not json", File.ReadAllText(kept));
    }

    [Fact]
    public void AFileFromAnEarlierBuildGetsTheNewAppearanceOptionsOnAndKeepsItsOwn()
    {
        var store = NewStore();
        Directory.CreateDirectory(directory.FullPath); // TestDataDirectory names the folder; the test makes it
        File.WriteAllText(store.FilePath, "{ \"version\": 1, \"appearance\": { \"showDeaths\": false } }");

        var loaded = store.Load().Settings;
        Assert.False(loaded.Appearance.ShowDeaths);
        Assert.True(loaded.Appearance.BarEffects);
        Assert.True(loaded.Appearance.EffectsOnEveryBar);
        Assert.True(loaded.Appearance.ShowCombatPower);
        Assert.Equal(1.0, loaded.Appearance.PictureOpacity); // the picture as it looked before the setting

        var plain = loaded with { Appearance = loaded.Appearance with { BarEffects = false, EffectsOnEveryBar = false, ShowCombatPower = false, PictureOpacity = 0.4 } };
        store.Save(plain);
        Assert.Equal(plain, store.Load().Settings);
    }

    [Fact]
    public void WithoutABackupAnUnreadableFileFallsBackToDefaultsAndIsKept()
    {
        var store = NewStore();
        File.WriteAllText(store.FilePath, "[1, 2, 3]");
        var result = store.Load();
        Assert.Equal(SettingsLoadStatus.ResetToDefaults, result.Status);
        Assert.Equal(SettingsNormalizer.Normalize(null).Settings, result.Settings);
        Assert.Single(Directory.GetFiles(directory.FullPath, "settings.unreadable-*.json"));
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void ADamagedFileThatCannotBeSetAsideSwitchesSavingOff()
    {
        var store = NewStore();
        File.WriteAllText(store.FilePath, "{ damaged");
        // An open handle without delete sharing blocks the rename, as another program holding the file would.
        using (new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            using var service = new SettingsService(store, new EngineSettings(), NullLogger<SettingsService>.Instance);
            Assert.Equal(SettingsLoadStatus.ReadOnly, service.LoadResult.Status);
            Assert.False(service.CanSave);
            service.Update(s => s with { Appearance = s.Appearance with { ShowDeaths = false } });
            service.Flush();
        }
        Assert.Equal("{ damaged", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void ABackupThatCannotBeOpenedSwitchesSavingOff()
    {
        var store = NewStore();
        var good = new MeterSettings { Appearance = new AppearanceSettings { RowScale = 1.3 } };
        store.Save(good);
        store.Save(good);
        File.WriteAllText(store.FilePath, "{ damaged");
        using (new FileStream(store.BackupPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = store.Load();
            Assert.Equal(SettingsLoadStatus.ReadOnly, result.Status);
            Assert.False(result.CanSave);
        }
        Assert.Equal(good, NewStore().Load().Settings); // the backup survived; the damaged file was kept aside
    }

    // Codex review on PR #2: with settings.json missing, a backup that cannot be used must survive the next saves.
    [Fact]
    public void WithoutTheMainFileADamagedBackupIsKeptAsideBeforeAnySave()
    {
        var store = NewStore();
        File.WriteAllText(store.BackupPath, "{ damaged");

        var result = store.Load();
        Assert.Equal(SettingsLoadStatus.ResetToDefaults, result.Status);
        Assert.True(result.CanSave);
        Assert.False(File.Exists(store.BackupPath));
        var kept = Assert.Single(Directory.GetFiles(directory.FullPath, "settings.bak.unreadable-*.json"));
        Assert.Equal("{ damaged", File.ReadAllText(kept));

        store.Save(new MeterSettings());
        store.Save(new MeterSettings { Appearance = new AppearanceSettings { RowScale = 1.2 } });
        Assert.Equal("{ damaged", File.ReadAllText(kept)); // two saves later, the old backup is intact
    }

    // As a newer main file is: used, never overwritten.
    [Fact]
    public void WithoutTheMainFileABackupFromANewerBuildIsUsedButNeverOverwritten()
    {
        var store = NewStore();
        const string newer = """{ "version": 99, "appearance": { "rowScale": 1.25 } }""";
        File.WriteAllText(store.BackupPath, newer);

        var result = store.Load();
        Assert.Equal(SettingsLoadStatus.ReadOnly, result.Status);
        Assert.False(result.CanSave);
        Assert.Equal(1.25, result.Settings.Appearance.RowScale);
        Assert.Contains("newer version", result.Notice);
        Assert.Equal(newer, File.ReadAllText(store.BackupPath));
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void WithoutTheMainFileABackupThatCannotBeOpenedSwitchesSavingOff()
    {
        var store = NewStore();
        File.WriteAllText(store.BackupPath, """{ "version": 1 }""");
        using (new FileStream(store.BackupPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = store.Load();
            Assert.Equal(SettingsLoadStatus.ReadOnly, result.Status);
            Assert.False(result.CanSave);
            Assert.Contains("could not be opened", result.Notice); // this branch, not the keep-aside one
        }
        Assert.Equal("""{ "version": 1 }""", File.ReadAllText(store.BackupPath));
    }

    [Fact]
    public void WithoutTheMainFileAnUnusableBackupThatCannotBeMovedSwitchesSavingOff()
    {
        var store = NewStore();
        File.WriteAllText(store.BackupPath, "{ damaged");
        using (new FileStream(store.BackupPath, FileMode.Open, FileAccess.Read, FileShare.Read)) // readable, not movable
        {
            var result = store.Load();
            Assert.Equal(SettingsLoadStatus.ReadOnly, result.Status);
            Assert.False(result.CanSave);
            Assert.Contains("could not be read or set aside", result.Notice);
        }
        Assert.Equal("{ damaged", File.ReadAllText(store.BackupPath));
    }

    [Fact]
    public void AnOversizedFileIsNotParsed()
    {
        var store = NewStore();
        File.WriteAllText(store.FilePath, "{\"version\":1,\"pad\":\"" + new string('x', (int)SettingsStore.MaxFileBytes) + "\"}");
        Assert.Equal(SettingsLoadStatus.ResetToDefaults, store.Load().Status);
    }

    [Fact]
    public void AFileFromANewerBuildIsReadButNeverOverwritten()
    {
        var store = NewStore();
        const string newer = """{ "version": 99, "appearance": { "rowScale": 1.25 }, "futureSection": { "x": 1 } }""";
        File.WriteAllText(store.FilePath, newer);

        using var service = new SettingsService(store, new EngineSettings(), NullLogger<SettingsService>.Instance);
        Assert.Equal(SettingsLoadStatus.ReadOnly, service.LoadResult.Status);
        Assert.False(service.CanSave);
        Assert.Equal(1.25, service.Current.Appearance.RowScale);

        service.Update(s => s with { Appearance = s.Appearance with { RowScale = 1.0 } });
        service.Flush();
        Assert.Equal(newer, File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void UpdatesApplyToTheEngineRaiseOneEventAndSaveOnFlush()
    {
        var store = NewStore();
        var engineSettings = new EngineSettings();
        using var service = new SettingsService(store, engineSettings, NullLogger<SettingsService>.Instance);
        var raised = 0;
        service.Changed += (_, _) => raised++;

        service.Update(s => s with { Fight = s.Fight with { BossOnlyCapture = true, GroupSummonDamage = false, HistoryRetentionDays = 12_000 } });
        service.Update(s => s); // no change: no event, no save

        Assert.Equal(1, raised);
        Assert.True(engineSettings.BossOnlyCapture);
        Assert.False(engineSettings.GroupSummonDamage);
        Assert.Equal(FightSettings.MaxRetentionDays, engineSettings.HistoryRetentionDays);

        service.Flush();
        Assert.Equal(service.Current, NewStore().Load().Settings);
    }

    [Fact]
    public void SavedFightRulesReachTheEngineAtLoad()
    {
        var store = NewStore();
        store.Save(new MeterSettings { Fight = new FightSettings { OneMinuteDummyMode = true } });
        var engineSettings = new EngineSettings();
        using var service = new SettingsService(store, engineSettings, NullLogger<SettingsService>.Instance);
        Assert.True(engineSettings.OneMinuteDummyMode);
    }

    [Theory]
    [InlineData("Ctrl+Shift+D", "Ctrl+Shift+D", 0x44)]
    [InlineData("control+alt+num5", "Ctrl+Alt+Num5", 0x65)]
    [InlineData("F10", "F10", 0x79)]
    [InlineData("Win+Shift+Up", "Shift+Win+Up", 0x26)]
    [InlineData("Ctrl+Backquote", "Ctrl+Backquote", 0xC0)]
    public void HotkeysParse(string text, string canonical, int virtualKey)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture, out var error), error);
        Assert.Equal(canonical, gesture.ToString());
        Assert.Equal(virtualKey, gesture.VirtualKey);
    }

    [Theory]
    [InlineData("D")]                 // a plain key would be taken from the game
    [InlineData("Ctrl+Ctrl+D")]
    [InlineData("Hyper+D")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+NotAKey")]
    public void BadHotkeysAreRejectedWithAReason(string text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void BrowserKeyPressesBecomeHotkeys()
    {
        Assert.True(HotkeyGesture.TryFromBrowserKey("KeyD", ctrl: true, alt: false, shift: true, meta: false, out var gesture, out _));
        Assert.Equal("Ctrl+Shift+D", gesture.ToString());
        Assert.True(HotkeyGesture.TryFromBrowserKey("Digit1", ctrl: false, alt: true, shift: false, meta: false, out gesture, out _));
        Assert.Equal("Alt+1", gesture.ToString());

        Assert.False(HotkeyGesture.TryFromBrowserKey("ShiftLeft", false, false, true, false, out _, out var modifierOnly));
        Assert.Null(modifierOnly); // still waiting for the main key
        Assert.False(HotkeyGesture.TryFromBrowserKey("KeyQ", false, false, false, false, out _, out var bare));
        Assert.NotNull(bare);
        Assert.False(HotkeyGesture.TryFromBrowserKey("MediaPlayPause", true, false, false, false, out _, out var unknown));
        Assert.NotNull(unknown);
    }

    [Fact]
    public void BackgroundPicturesAreCopiedUnderAContentNameAndOldOnesRemoved()
    {
        var source = Path.Combine(directory.FullPath, "picked.png");
        File.WriteAllBytes(source, [1, 2, 3, 4]);
        var first = BackgroundImages.Import(source, directory.FullPath);
        Assert.True(BackgroundImages.IsStoredName(first));
        Assert.StartsWith("background-", first);

        File.WriteAllBytes(source, [5, 6, 7, 8]);
        var second = BackgroundImages.Import(source, directory.FullPath);
        Assert.NotEqual(first, second);
        Assert.Equal(new[] { second }, Directory.GetFiles(BackgroundImages.Folder(directory.FullPath)).Select(Path.GetFileName));
    }

    [Fact]
    public void BackgroundPicturesAreValidated()
    {
        var text = Path.Combine(directory.FullPath, "notes.txt");
        File.WriteAllText(text, "hello");
        Assert.Throws<InvalidDataException>(() => BackgroundImages.Import(text, directory.FullPath));

        var huge = Path.Combine(directory.FullPath, "huge.png");
        using (var stream = File.Create(huge)) stream.SetLength(BackgroundImages.MaxBytes + 1);
        Assert.Throws<InvalidDataException>(() => BackgroundImages.Import(huge, directory.FullPath));

        Assert.Throws<ArgumentException>(() => BackgroundImages.Import(Path.Combine("relative", "a.png"), directory.FullPath));
        Assert.False(BackgroundImages.IsStoredName("background-1234abcd.exe"));
        Assert.False(BackgroundImages.IsStoredName(@"..\background-1234abcd.png"));
        Assert.False(BackgroundImages.IsStoredName("background-1234abcd.png\n"));
    }

    [Fact]
    public void ARelativeDataDirectoryIsRejected() =>
        Assert.Throws<ArgumentException>(() => new SettingsStore(Path.Combine("data", "scarlet"), NullLogger<SettingsStore>.Instance));

    private SettingsStore NewStore() => new(directory.FullPath, NullLogger<SettingsStore>.Instance);

    public void Dispose() => directory.Dispose();
}
