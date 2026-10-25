namespace ScarletMeter.App.Formatting;

/// <summary>
/// The colour band of a target's HP for the ring round the overlay header's face (app.css, .ov-avatar): green from 66%,
/// yellow from 33%, red above 0. At 0 the target is down, a state of its own, so there is no band.
/// </summary>
public static class HpBand
{
    public const double HighFrom = 66;
    public const double MidFrom = 33;

    /// <summary>A CSS class for the HP percent: "hp-high", "hp-mid", "hp-low", or empty at 0 or when unknown (NaN).</summary>
    public static string Of(double hpPercent) => hpPercent switch
    {
        >= HighFrom => "hp-high",
        >= MidFrom => "hp-mid",
        > 0 => "hp-low",
        _ => string.Empty,
    };
}
