namespace ScarletMeter.App.Formatting;

/// <summary>Ping quality bands for the header indicator, with RATmeter's thresholds (60, 100 and 200 ms).</summary>
public static class PingLevel
{
    /// <summary>A CSS class for the latency: "good", "fair", "slow", "bad", or "none" when unknown.</summary>
    public static string Of(int? milliseconds) => milliseconds switch
    {
        null => "none",
        < 60 => "good",
        < 100 => "fair",
        < 200 => "slow",
        _ => "bad",
    };
}
