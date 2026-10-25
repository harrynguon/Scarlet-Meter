using ScarletMeter.App.Combat;
using ScarletMeter.Engine;

namespace ScarletMeter.App.Formatting;

/// <summary>
/// The ring round the overlay header's face (app.css, .ov-avatar): the colour band of the target's HP, green from 66%,
/// yellow from 33%, red above 0, neutral while its total HP is not known yet; without its glow once the HP has been
/// still (<see cref="FightPhase.Settled"/>); grey when the target is down; none between fights.
/// </summary>
public static class HpBand
{
    public const double HighFrom = 66;
    public const double MidFrom = 33;

    /// <summary>A CSS class for an HP percent: "hp-high", "hp-mid", "hp-low", or empty at 0 (the target is down).</summary>
    public static string Of(double hpPercent) => hpPercent switch
    {
        >= HighFrom => "hp-high",
        >= MidFrom => "hp-mid",
        > 0 => "hp-low",
        _ => string.Empty,
    };

    /// <summary>
    /// The ring's CSS classes for the fight's phase and its target: an HP band (or "hp-unknown" when the target's
    /// total HP is not known, since <see cref="TargetInfo.HpPercent"/> reads 0 then), with " still" once the HP has
    /// not moved for a while; "done" when the target is down; empty with no target.
    /// </summary>
    public static string RingClass(FightPhase phase, TargetInfo? target)
    {
        if (phase == FightPhase.Defeated) return "done";
        if (phase is not (FightPhase.Live or FightPhase.Settled) || target is null) return string.Empty;
        var band = target.HpTotal > 0 ? Of(target.HpPercent) : "hp-unknown";
        return phase == FightPhase.Settled ? band + " still" : band;
    }
}
