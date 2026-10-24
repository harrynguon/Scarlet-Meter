using ScarletMeter.Engine.Upstream;

namespace ScarletMeter.Tests;

/// <summary>
/// AION 2 Global's names over upstream's: the class RATmeter's data calls "Elementalist" is Global's Spiritmaster,
/// as a whole word only, and every other name passes through unchanged.
/// </summary>
public sealed class GlobalNamesTests
{
    [Theory]
    [InlineData("Elementalist", "Spiritmaster")]
    [InlineData("Elementalist spirit", "Spiritmaster spirit")]
    [InlineData("Gladiator", "Gladiator")]
    [InlineData("Spirit 55", "Spirit 55")]
    [InlineData("Elementalists", "Elementalists")] // not a whole word
    [InlineData("elementalist", "elementalist")]   // upstream's data is capitalised; anything else is left as written
    [InlineData("", "")]
    [InlineData(null, "")]
    public void GlobalNamesReplaceUpstreamsWholeWords(string? upstream, string global) =>
        Assert.Equal(global, GlobalNames.Of(upstream));
}
