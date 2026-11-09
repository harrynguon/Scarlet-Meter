using ScarletMeter.App.FieldBosses;
using ScarletMeter.WorldEvents.FieldBosses;

namespace ScarletMeter.Tests;

/// <summary>
/// The Field bosses window's words (FieldBosses/FieldBossText) and its order (FieldBossListing). Harry, 2026-11-09: "make the
/// pinned bosses be automatically arranged to the top of the list", and "say 'Respawns in ...' and then the timer/countdown,
/// followed by the exact time on the other column". Made-up times; no player's.
/// </summary>
public sealed class FieldBossTextTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 3, 2, 0, TimeSpan.Zero);

    // The state column says what the boss is doing, with the time left to a respawn as the overlay's notice counts it
    // (minutes and seconds under an hour); the clock time is in the column beside it.
    [Fact]
    public void TheStateSaysWhatTheBossIsDoingWithTheTimeLeftToARespawn()
    {
        Assert.Equal("Respawns in 2h 59m", FieldBossText.State(Down(Now.AddMinutes(179)), Now));
        Assert.Equal("Respawns in 14:43", FieldBossText.State(Down(Now.Add(new TimeSpan(0, 14, 43))), Now));
        Assert.Equal("Up", FieldBossText.State(Up(Now.AddMinutes(-4)), Now));
        Assert.Equal("Up", FieldBossText.State(Up(null), Now));
        Assert.Equal("Up", FieldBossText.State(Down(Now.AddMinutes(-1)), Now)); // its respawn has passed
        Assert.Equal("Killed, no time given", FieldBossText.State(Down(null), Now));
        Assert.Equal("No timer yet", FieldBossText.State(null, Now));
    }

    // The column beside the state: the respawn's clock time, since when a boss is up, or when it was seen up when the source
    // did not say since when; nothing with no time. On the 12-hour clock, in the zone given (the PC's in the window).
    [Fact]
    public void TheTimeBesideTheStateIsTheRespawnOrSinceWhenItIsUp()
    {
        var utc = TimeZoneInfo.Utc;
        Assert.Equal("6:01am", FieldBossText.Time(Down(Now.AddMinutes(179)), Now, utc));
        Assert.Equal("since 2:58am", FieldBossText.Time(Up(Now.AddMinutes(-4)), Now, utc));
        Assert.Equal("seen 2:59am", FieldBossText.Time(Up(null), Now, utc)); // observed three minutes ago
        Assert.Equal("since 3:01am", FieldBossText.Time(Down(Now.AddMinutes(-1)), Now, utc)); // up since its respawn
        Assert.Equal(string.Empty, FieldBossText.Time(Down(null), Now, utc));
        Assert.Equal(string.Empty, FieldBossText.Time(null, Now, utc));
        // Another zone: 6:01am UTC is 7:01pm at UTC+13.
        var plus13 = TimeZoneInfo.CreateCustomTimeZone("Test+13", TimeSpan.FromHours(13), "Test+13", "Test+13");
        Assert.Equal("7:01pm", FieldBossText.Time(Down(Now.AddMinutes(179)), Now, plus13));
    }

    // The belled bosses come first, then the rest, each in the order given; a line goes under the last belled boss only
    // when an unbelled one follows it.
    [Fact]
    public void TheBelledBossesComeFirstInTheirOwnOrder()
    {
        FieldBoss[] map = [Boss(1), Boss(2), Boss(3), Boss(4), Boss(5)];
        var chosen = new HashSet<int> { 4, 2 };
        var ordered = FieldBossListing.Ordered(map, chosen);
        Assert.Equal([2, 4, 1, 3, 5], ordered.Select(b => b.Id));
        Assert.Equal([false, true, false, false, false], Enumerable.Range(0, ordered.Count).Select(i => FieldBossListing.EndsChosen(ordered, i, chosen)));

        Assert.Equal([1, 2, 3, 4, 5], FieldBossListing.Ordered(map, new HashSet<int>()).Select(b => b.Id)); // none belled
        var all = FieldBossListing.Ordered(map, new HashSet<int> { 1, 2, 3, 4, 5 });
        Assert.Equal([1, 2, 3, 4, 5], all.Select(b => b.Id));
        Assert.DoesNotContain(true, Enumerable.Range(0, all.Count).Select(i => FieldBossListing.EndsChosen(all, i, all.Select(b => b.Id).ToHashSet())));
        Assert.False(FieldBossListing.EndsChosen(ordered, -1, chosen));
        Assert.False(FieldBossListing.EndsChosen(ordered, ordered.Count, chosen));
        Assert.Equal([3], FieldBossListing.Ordered(map, new HashSet<int> { 3, 99 }).Take(1).Select(b => b.Id)); // an id not on the map is ignored
    }

    private static FieldBoss Boss(int id) => new(id, 2100000 + id, $"Boss {id}", 1010);

    private static FieldBossTimer Down(DateTimeOffset? back) => new(101021, false, back, Now.AddMinutes(-3), FieldBossSource.NotMeter);

    private static FieldBossTimer Up(DateTimeOffset? since) => new(101021, true, since, Now.AddMinutes(-3), FieldBossSource.Map);
}
