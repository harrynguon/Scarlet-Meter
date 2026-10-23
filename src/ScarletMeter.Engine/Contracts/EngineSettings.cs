namespace ScarletMeter.Engine;

/// <summary>
/// Runtime toggles the fight logic honours. The app owns their persistence; the engine reads them live
/// (upstream consults them on every hit) and forwards <see cref="Changed"/> to upstream code.
/// </summary>
public sealed class EngineSettings
{
    private bool bossOnlyCapture;
    private bool groupSummonDamage = true;
    private bool oneMinuteDummyMode;
    private int historyRetentionDays = 30;

    /// <summary>Ignore hits on mobs that are not bosses.</summary>
    public bool BossOnlyCapture { get => bossOnlyCapture; set => Set(ref bossOnlyCapture, value); }

    /// <summary>Fold a summon's damage into its owner's skill breakdown.</summary>
    public bool GroupSummonDamage { get => groupSummonDamage; set => Set(ref groupSummonDamage, value); }

    /// <summary>Stop counting hits on a training dummy one minute after the first hit.</summary>
    public bool OneMinuteDummyMode { get => oneMinuteDummyMode; set => Set(ref oneMinuteDummyMode, value); }

    /// <summary>Days of boss-fight history to keep, clamped to 1 to 9999 as upstream does.</summary>
    public int HistoryRetentionDays
    {
        get => historyRetentionDays;
        set => Set(ref historyRetentionDays, Math.Clamp(value, 1, 9999));
    }

    /// <summary>Raised after a property changes value.</summary>
    public event EventHandler? Changed;

    private void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
