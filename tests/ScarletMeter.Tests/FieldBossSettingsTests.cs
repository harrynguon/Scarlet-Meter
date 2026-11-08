using Microsoft.Extensions.Logging.Abstractions;
using ScarletMeter.App.Settings;
using ScarletMeter.Engine;

namespace ScarletMeter.Tests;

/// <summary>
/// The field-boss settings (FieldBossSettings): their defaults, the normalizer's repairs (bosses the catalog knows, once
/// each, in its order; the lead time clamped; a server that cannot be one cleared) and the numeric values surviving a save.
/// Every on/off switch is covered by SettingsTests.EveryOnOffSettingSurvivesASave.
/// </summary>
public sealed class FieldBossSettingsTests : IDisposable
{
    private readonly TestDataDirectory directory = new();

    public void Dispose() => directory.Dispose();

    [Fact]
    public void TheDefaultsChooseNoBossAndReadSharedTimersOnlyOnceWanted()
    {
        var defaults = new MeterSettings().FieldBosses;
        Assert.Equal((true, 10, true, (int?)null), (defaults.Enabled, defaults.LeadMinutes, defaults.SharedTimers, defaults.ServerId));
        Assert.Empty(defaults.Bosses);
    }

    [Fact]
    public void TheNormalizerKeepsKnownBossesOnceInTheCatalogsOrder()
    {
        var (settings, repairs) = SettingsNormalizer.Normalize(new MeterSettings
        {
            FieldBosses = new FieldBossSettings { Bosses = [111021, 101021, 999999, 101021], LeadMinutes = 0, ServerId = 42 },
        });
        Assert.Equal([101021, 111021], settings.FieldBosses.Bosses);
        Assert.Equal(1, settings.FieldBosses.LeadMinutes);
        Assert.Null(settings.FieldBosses.ServerId);
        Assert.Equal(3, repairs.Count);

        var (late, _) = SettingsNormalizer.Normalize(new MeterSettings { FieldBosses = new FieldBossSettings { LeadMinutes = 61, ServerId = 2510 } });
        Assert.Equal((60, (int?)2510), (late.FieldBosses.LeadMinutes, late.FieldBosses.ServerId));
        var (none, noRepairs) = SettingsNormalizer.Normalize(new MeterSettings { FieldBosses = null! });
        Assert.Equal(new FieldBossSettings(), none.FieldBosses);
        Assert.Empty(noRepairs);
    }

    [Fact]
    public void TheServerIdRuleTakesEveryRegionsServersAndNothingElse()
    {
        Assert.All(new[] { 1001, 1058, 2021, 1101, 1109, 1205, 1323, 1406, 1503, 2510 }, id => Assert.True(FieldBossSettings.IsServerId(id), $"{id}"));
        Assert.All(new[] { 0, 42, 1000, 1500, 1603, 3503, -1503, 15030 }, id => Assert.False(FieldBossSettings.IsServerId(id), $"{id}"));
    }

    // The bosses, the lead time and the server come back from the file as saved; changing only one of them saves it.
    [Fact]
    public void TheBossesLeadTimeAndServerSurviveASave()
    {
        var store = new SettingsStore(directory.FullPath, NullLogger<SettingsStore>.Instance);
        using (var service = new SettingsService(store, new EngineSettings(), NullLogger<SettingsService>.Instance))
        {
            service.Update(s => s with { FieldBosses = s.FieldBosses with { Bosses = [101021, 2002], LeadMinutes = 17, ServerId = 1503 } });
            service.Flush();
        }
        using (var service = new SettingsService(new SettingsStore(directory.FullPath, NullLogger<SettingsStore>.Instance), new EngineSettings(), NullLogger<SettingsService>.Instance))
        {
            Assert.Equal([101021, 2002], service.Current.FieldBosses.Bosses);
            Assert.Equal((17, (int?)1503), (service.Current.FieldBosses.LeadMinutes, service.Current.FieldBosses.ServerId));
            service.Update(s => s with { FieldBosses = s.FieldBosses with { ServerId = 1504 } });
            service.Flush();
        }
        using var reread = new SettingsService(new SettingsStore(directory.FullPath, NullLogger<SettingsStore>.Instance), new EngineSettings(), NullLogger<SettingsService>.Instance);
        Assert.Equal(1504, reread.Current.FieldBosses.ServerId);
    }
}
