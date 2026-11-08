using System.Text;
using System.Text.Json;
using ScarletMeter.WorldEvents.FieldBosses;

namespace ScarletMeter.Tests;

/// <summary>
/// NotMeter's shared field-boss timers (NotMeterFieldBossFeed): which file a Global server's timers are in, and reading a
/// file in the shape NotMeter publishes (made up here from that shape, never a copy of a published file): a boss down with
/// its respawn, one up, bosses the catalog does not know or on another map, times out of bounds, and files that are not
/// NotMeter's.
/// </summary>
public sealed class NotMeterFieldBossFeedTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 5, 0, 0, TimeSpan.Zero);
    private static readonly long NowMs = Now.ToUnixTimeMilliseconds();
    private const long Minute = 60_000;

    // A file in NotMeter's published shape, one server with the given maps of entries (each an anonymous object).
    private static byte[] File(int serverId, object[] maps, string schema = NotMeterFieldBossFeed.Schema, string serviceRegion = "WW", int extraServers = 0, long? generatedAt = null) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            schema,
            version = 1,
            serviceRegion,
            globalRegion = "as",
            generatedAt = generatedAt ?? NowMs / 1000,
            servers = new object[] { new { serverId, maps } }
                .Concat(Enumerable.Range(0, extraServers).Select(n => (object)new { serverId = 9000 + n, maps = Array.Empty<object>() })).ToArray(),
        }));

    private static object Map(int mapId, params object[] entries) => new { mapId, observedAt = NowMs / 1000, entries };

    private static object DeadEntry(int npcCode, long targetAt, long observedAt) => new { observedAtMs = observedAt, bossCode = npcCode, targetAt };

    private static object UpEntry(int npcCode, long observedAt) => new { observedAtMs = observedAt, spawned = true, bossCode = npcCode, targetAt = observedAt };

    private static IReadOnlyList<FieldBossTimer> Read(byte[] file, int serverId = 1503)
    {
        Assert.True(NotMeterFieldBossFeed.TryRead(file, serverId, Now, out var timers, out var problem), problem);
        return timers;
    }

    // The region file of each Global region (literal addresses), the "-expanded" file for Asia, NA East and Europe, and
    // none for Korea's and Taiwan's servers or ids that are no server.
    [Fact]
    public void AGlobalServersTimersAreInItsRegionsFile()
    {
        const string Base = "https://raw.githubusercontent.com/Not4You-Dev/NotMeter-Cache/main/presence/notmeter-field-boss-public-ww-";
        Assert.Equal(Base + "as-expanded.json", NotMeterFieldBossFeed.Address(1503)?.ToString());
        Assert.Equal(Base + "as-expanded.json", NotMeterFieldBossFeed.Address(2510)?.ToString());
        Assert.Equal(Base + "nae-expanded.json", NotMeterFieldBossFeed.Address(1109)?.ToString());
        Assert.Equal(Base + "naw.json", NotMeterFieldBossFeed.Address(2205)?.ToString());
        Assert.Equal(Base + "eu-expanded.json", NotMeterFieldBossFeed.Address(1323)?.ToString());
        Assert.Equal(Base + "la.json", NotMeterFieldBossFeed.Address(1406)?.ToString());
        Assert.Null(NotMeterFieldBossFeed.Address(1003)); // Korea
        Assert.Null(NotMeterFieldBossFeed.Address(1500)); // no slot
        Assert.Null(NotMeterFieldBossFeed.Address(3503)); // no faction
        Assert.Null(NotMeterFieldBossFeed.Address(1603)); // no region
    }

    // A boss down carries its respawn; one up is up with since when not given (NotMeter's targetAt for it is when it was
    // seen). Eternal Gartua is NPC 2101074 in the file and world-map id 101021 here.
    [Fact]
    public void AFileGivesEachBossDownWithItsRespawnOrUp()
    {
        var timers = Read(File(1503, [Map(1010, DeadEntry(2101074, NowMs + 419 * Minute, NowMs - 2 * Minute), UpEntry(2100050, NowMs - Minute))]));
        Assert.Equal(
            [
                new FieldBossTimer(101003, true, null, Now.AddMinutes(-1), FieldBossSource.NotMeter),
                new FieldBossTimer(101021, false, Now.AddMinutes(419), Now.AddMinutes(-2), FieldBossSource.NotMeter),
            ],
            timers);
    }

    // Another server's timers, a map that is none of the three, a boss the catalog does not know or on another map, and an
    // observation older than two weeks or a day ahead are left out; a respawn out of bounds is no time.
    [Fact]
    public void WhatIsNotTheServersOrDoesNotFitIsLeftOut()
    {
        Assert.Empty(Read(File(1504, [Map(1010, DeadEntry(2101074, NowMs + Minute, NowMs))])));
        Assert.Empty(Read(File(1503, [Map(503001, DeadEntry(2101074, NowMs + Minute, NowMs))])));
        Assert.Empty(Read(File(1503, [Map(1010, DeadEntry(9999999, NowMs + Minute, NowMs))])));
        Assert.Empty(Read(File(1503, [Map(1110, DeadEntry(2101074, NowMs + Minute, NowMs))]))); // Gartua is Verteron's
        Assert.Empty(Read(File(1503, [Map(1010, DeadEntry(2101074, NowMs + Minute, NowMs - 15L * 24 * 60 * Minute))])));
        Assert.Empty(Read(File(1503, [Map(1010, DeadEntry(2101074, NowMs + Minute, NowMs + 25 * 60 * Minute))])));
        var farAhead = Assert.Single(Read(File(1503, [Map(1010, DeadEntry(2101074, NowMs + 9L * 24 * 60 * Minute, NowMs))])));
        Assert.Null(farAhead.Time);
        Assert.Null(Assert.Single(Read(File(1503, [Map(1010, DeadEntry(2101074, 0, NowMs))]))).Time);
    }

    // A reporter whose clock ran fast dates its entry ahead: it is taken as made when the file was generated (NotMeter's own
    // clock), or now when that is later, so the player's own map read after the file still wins, at every merge after it
    // (Codex and the deep audit on #75: capped at each merge's now instead, it kept outranking an earlier map reading).
    [Fact]
    public void AnEntryDatedAheadIsTakenAsMadeWhenTheFileWasGenerated()
    {
        var generated = Now.AddMinutes(-3);
        var shared = Assert.Single(Read(File(1503, [Map(1010, UpEntry(2101074, NowMs + 20 * Minute))], generatedAt: generated.ToUnixTimeSeconds())));
        Assert.Equal(generated, shared.ObservedAt);

        var own = new FieldBossTimer(101021, false, Now.AddMinutes(30), Now.AddMinutes(-1), FieldBossSource.Map);
        foreach (var later in new[] { Now, Now.AddSeconds(1), Now.AddMinutes(30) })
        {
            Assert.Same(own, FieldBossBoard.Merge([shared, own], later)[101021]);
            Assert.Same(own, FieldBossBoard.Merge([own, shared], later)[101021]);
        }
        // An Up notice runs from that time, not from the fast clock's.
        Assert.Empty(FieldBossAlertPlanner.Plan(FieldBossBoard.Merge([shared], Now.AddMinutes(13)), new HashSet<int> { 101021 }, Now.AddMinutes(13), TimeSpan.FromMinutes(10)).Alerts);

        // Without a usable generation time, the cap is now; one older than the bounds is not used.
        Assert.Equal(Now, Assert.Single(Read(File(1503, [Map(1010, UpEntry(2101074, NowMs + 20 * Minute))], generatedAt: 0))).ObservedAt);
        Assert.Equal(Now, Assert.Single(Read(File(1503, [Map(1010, UpEntry(2101074, NowMs + 20 * Minute))], generatedAt: Now.AddDays(-15).ToUnixTimeSeconds()))).ObservedAt);
        // An entry dated before the file keeps its own time.
        Assert.Equal(Now.AddMinutes(-5), Assert.Single(Read(File(1503, [Map(1010, UpEntry(2101074, NowMs - 5 * Minute))], generatedAt: generated.ToUnixTimeSeconds()))).ObservedAt);
    }

    // Text that is not valid UTF-16 or UTF-8 where the schema or region goes is simply not NotMeter's file: no exception
    // (deep audit on #75: GetString threw).
    [Fact]
    public void BrokenTextInTheSchemaIsNotNotMetersFile()
    {
        var loneSurrogate = Encoding.UTF8.GetBytes("""{ "schema": "\ud800", "serviceRegion": "WW", "servers": [] }""");
        Assert.False(NotMeterFieldBossFeed.TryRead(loneSurrogate, 1503, Now, out _, out var problem));
        Assert.NotNull(problem);
        var regionSurrogate = Encoding.UTF8.GetBytes("{ \"schema\": \"" + NotMeterFieldBossFeed.Schema + "\", \"serviceRegion\": \"\\udfff\", \"servers\": [] }");
        Assert.False(NotMeterFieldBossFeed.TryRead(regionSurrogate, 1503, Now, out _, out _));
        byte[] badUtf8 = [.. Encoding.UTF8.GetBytes("{ \"schema\": \""), 0xC3, 0x28, .. Encoding.UTF8.GetBytes("\", \"serviceRegion\": \"WW\", \"servers\": [] }")];
        Assert.False(NotMeterFieldBossFeed.TryRead(badUtf8, 1503, Now, out _, out _));
    }

    // Two entries for one boss: the newer observation stands.
    [Fact]
    public void OfTwoEntriesForABossTheNewerStands()
    {
        var timers = Read(File(1503, [Map(1010, DeadEntry(2101074, NowMs + 300 * Minute, NowMs - 10 * Minute)), Map(1010, UpEntry(2101074, NowMs - Minute))]));
        Assert.True(Assert.Single(timers).Up);
    }

    // A file that is not NotMeter's global field-boss file, or not JSON, does not read; a file without the server reads as none.
    [Fact]
    public void AFileThatIsNotNotMetersDoesNotRead()
    {
        Assert.False(NotMeterFieldBossFeed.TryRead(Encoding.UTF8.GetBytes("not json"), 1503, Now, out _, out var notJson));
        Assert.NotNull(notJson);
        Assert.False(NotMeterFieldBossFeed.TryRead(File(1503, [], schema: "notmeter-field-boss-cache-v1"), 1503, Now, out _, out _));
        Assert.False(NotMeterFieldBossFeed.TryRead(File(1503, [], serviceRegion: "KR"), 1503, Now, out _, out _));
        Assert.False(NotMeterFieldBossFeed.TryRead(Encoding.UTF8.GetBytes("[]"), 1503, Now, out _, out _));
        Assert.False(NotMeterFieldBossFeed.TryRead(File(1503, [], extraServers: 500), 1503, Now, out _, out var tooMany));
        Assert.Contains("more servers", tooMany);
        Assert.True(NotMeterFieldBossFeed.TryRead(File(1503, [], extraServers: 499), 1503, Now, out var none, out _));
        Assert.Empty(none);
    }
}
