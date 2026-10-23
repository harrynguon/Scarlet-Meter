using ScarletMeter.Engine;

namespace ScarletMeter.Tests;

/// <summary>HistoryQuery's boundary rules: what is rejected, and how accepted input is normalised.</summary>
public sealed class HistoryQueryTests
{
    [Theory]
    [InlineData(0, 50)]
    [InlineData(-1, 50)]
    [InlineData(HistoryQuery.MaxPage + 1, 50)]
    [InlineData(20_000_000, HistoryQuery.MaxPageSize)] // upstream's int skip would wrap round to page 1
    [InlineData(int.MaxValue, 1)]
    [InlineData(1, 0)]
    [InlineData(1, HistoryQuery.MaxPageSize + 1)]
    public void RejectsPagesOutsideTheirRange(int page, int pageSize)
    {
        var error = Assert.Throws<ArgumentException>(() => new HistoryQuery(Page: page, PageSize: pageSize).Validate());
        var pageWrong = page is < 1 or > HistoryQuery.MaxPage;
        Assert.Contains(pageWrong ? "page must be 1 to 1,000,000" : "page size must be 1 to 200", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsTheEdgesOfEachRange()
    {
        var first = new HistoryQuery(Page: 1, PageSize: 1).Validate();
        Assert.Equal((1, 1), (first.Page, first.PageSize));
        Assert.Equal(HistoryQuery.MaxPageSize, new HistoryQuery(PageSize: HistoryQuery.MaxPageSize).Validate().PageSize);
        Assert.Equal(HistoryQuery.MaxPage, new HistoryQuery(Page: HistoryQuery.MaxPage, PageSize: HistoryQuery.MaxPageSize).Validate().Page);
        Assert.True((long)(HistoryQuery.MaxPage - 1) * HistoryQuery.MaxPageSize < int.MaxValue); // upstream's skip fits its int
        var instant = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Local);
        Assert.Equal(instant, new HistoryQuery(From: instant, To: instant).Validate().To);
    }

    [Fact]
    public void TrimsTheNameAndLimitsItsLength()
    {
        Assert.Equal("Aultross", new HistoryQuery("  Aultross \t").Validate().TargetNameContains);
        Assert.Null(new HistoryQuery("   ").Validate().TargetNameContains);
        var longest = new string('a', HistoryQuery.MaxNameLength);
        Assert.Equal(longest, new HistoryQuery($"  {longest}  ").Validate().TargetNameContains); // measured after trimming
        var error = Assert.Throws<ArgumentException>(() => new HistoryQuery(longest + "a").Validate());
        Assert.Contains("at most 100 characters", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsARangeThatEndsBeforeItStarts()
    {
        var from = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Local);
        var error = Assert.Throws<ArgumentException>(() => new HistoryQuery(From: from, To: from.AddTicks(-1)).Validate());
        Assert.Contains("starts", error.Message, StringComparison.Ordinal);
        Assert.Null(new HistoryQuery(From: from).Validate().To); // an open end is fine
    }

    [Fact]
    public void TimesBecomeLocalAsUpstreamStoresThem()
    {
        var local = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Local);
        var query = new HistoryQuery(From: local.ToUniversalTime(), To: DateTime.SpecifyKind(local, DateTimeKind.Unspecified)).Validate();
        Assert.Equal(DateTimeKind.Local, query.From!.Value.Kind);
        Assert.Equal(local, query.From.Value);
        Assert.Equal(DateTimeKind.Local, query.To!.Value.Kind);
        Assert.Equal(local, query.To.Value);
    }
}
