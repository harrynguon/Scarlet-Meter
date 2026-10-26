using AionDpsMeter.Core.Windowing;
using AionDpsMeter.Services.Services.Settings;

namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// Presents <see cref="EngineSettings"/> to upstream code through its IAppSettingsService. Upstream mixes
/// fight rules with its own UI preferences in one interface: only the fight rules are mapped, the rest
/// hold inert in-memory defaults. Packet logging is always off; Scarlet Meter never writes captures to disk.
/// When an upstream release adds a member to the interface, this class stops compiling, which is the
/// intended signal to look at what changed.
/// </summary>
internal sealed class UpstreamSettingsBridge : IAppSettingsService
{
    private readonly EngineSettings settings;
    private readonly PauseState pause;
    private readonly Dictionary<string, WindowBounds> windowBounds = [];
    private OverlaySettings buffOverlay = new();
    private OverlaySettings skillCdOverlay = new();

    public UpstreamSettingsBridge(EngineSettings settings, PauseState pause)
    {
        this.settings = settings;
        this.pause = pause;
        settings.Changed += (_, _) => SettingsChanged(this, EventArgs.Empty);
    }

    public event EventHandler SettingsChanged = delegate { };

    // Fight rules upstream consults while tracking a fight. Boss-only also holds while the meter is paused (AbyssPause):
    // upstream reads it on every hit, so the crowd's hits on players and mobs are dropped before upstream keeps them; the
    // player's own setting is left as it was.
    public bool BossOnlyCapture { get => settings.BossOnlyCapture || pause.Paused; set => settings.BossOnlyCapture = value; }
    public bool GroupSummonDamage { get => settings.GroupSummonDamage; set => settings.GroupSummonDamage = value; }
    public bool OneMinuteDummyMode { get => settings.OneMinuteDummyMode; set => settings.OneMinuteDummyMode = value; }
    public int HistoryRetantionPeriod { get => settings.HistoryRetentionDays; set => settings.HistoryRetentionDays = value; }

    // Never enabled: the setter is ignored so no code path can start upstream's raw packet log.
    public bool IsPacketLoggingEnabled { get => false; set { } }

    // Upstream UI preferences with no meaning here, kept in memory so upstream code that reads them behaves.
    public bool IsNicknameHidden { get; set; }
    public bool ShowPlayerDeaths { get; set; } = true;
    public double WindowOpacity { get; set; } = 1;
    public string? BackgroundImagePath { get; set; }
    public bool RelativeProgressBar { get; set; }
    public string ToggleVisibilityHotkey { get; set; } = string.Empty;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public double PlayerRowScale { get; set; } = 1;
    public int UiStyle { get; set; }
    public bool UseClassColors { get; set; }
    public double StatCalcCritChance { get; set; }
    public double StatCalcCriticalDamageBoost { get; set; }
    public double StatCalcBackAttackRate { get; set; }
    public double StatCalcFrontAttackRate { get; set; }
    public string StatCalcAttackType { get; set; } = string.Empty;
    public double StatCalcAttackIncreaseCombatPercent { get; set; }
    public double StatCalcPartyDamageBoost { get; set; }
    public double StatCalcBossDamageTolerance { get; set; }
    public double StatCalcPartySmiteBuff { get; set; }
    public double StatCalcBossSmiteResist { get; set; }

    // Upstream v1.11.5.341's "My Style" colours for the player's own name and bar, used only by its UI;
    // upstream's defaults, kept in memory.
    public string CurrentPlayerNameStyleMode { get; set; } = "default";
    public string CurrentPlayerNameColor { get; set; } = "#F2F1F5";
    public string CurrentPlayerNameGradientColor1 { get; set; } = "#C084FC";
    public string CurrentPlayerNameGradientColor2 { get; set; } = "#60A5FA";
    public string CurrentPlayerBarStyleMode { get; set; } = "default";
    public string CurrentPlayerBarColor { get; set; } = "#9C27B0";
    public string CurrentPlayerBarGradientColor1 { get; set; } = "#7C3AED";
    public string CurrentPlayerBarGradientColor2 { get; set; } = "#EC4899";

    // Overlays stay disabled, so upstream's timed trackers track nothing.
    public OverlaySettings BufOverlaySettings { get => buffOverlay; set => buffOverlay = value ?? new(); }
    public OverlaySettings SkillCdOverlaySettings { get => skillCdOverlay; set => skillCdOverlay = value ?? new(); }

    public bool TryGetWindowBounds(WindowKey key, out WindowBounds? bounds)
    {
        var found = windowBounds.TryGetValue(key.ToString(), out var value);
        bounds = value;
        return found;
    }

    public void SetWindowBounds(WindowKey key, WindowBounds bounds) => windowBounds[key.ToString()] = bounds;
}
