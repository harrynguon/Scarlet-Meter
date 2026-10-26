using ScarletMeter.Engine.Upstream;

namespace ScarletMeter.Tests;

/// <summary>
/// The rule for training-dummy fights on its own (<see cref="DummyFightRule"/>): every combination of its three facts,
/// and that the user-known read, which can race the capture thread, is made only when it decides.
/// </summary>
public sealed class DummyFightRuleTests
{
    [Theory]
    // Not a dummy, or a target no longer known: always kept.
    [InlineData(false, false, false, true)]
    [InlineData(false, false, true, true)]
    [InlineData(false, true, true, true)]
    [InlineData(null, false, true, true)]
    [InlineData(null, false, false, true)]
    // A dummy the user hit: kept, with everyone else in the fight.
    [InlineData(true, true, true, true)]
    // A dummy the user did not hit: left out once the user is known, kept until then.
    [InlineData(true, false, true, false)]
    [InlineData(true, false, false, true)]
    public void KeepsEveryFightButAKnownUsersStrangersDummyFight(bool? isDummy, bool userInFight, bool userKnown, bool kept) =>
        Assert.Equal(kept, DummyFightRule.Keeps(isDummy, userInFight, () => userKnown));

    [Theory]
    [InlineData(false, false)]
    [InlineData(null, false)]
    [InlineData(true, true)]
    public void AsksWhetherTheUserIsKnownOnlyWhenItDecides(bool? isDummy, bool userInFight)
    {
        var asked = 0;
        Assert.True(DummyFightRule.Keeps(isDummy, userInFight, () => { asked++; return true; }));
        Assert.Equal(0, asked);

        Assert.False(DummyFightRule.Keeps(isDummy: true, userInFight: false, () => { asked++; return true; }));
        Assert.Equal(1, asked);
    }
}
