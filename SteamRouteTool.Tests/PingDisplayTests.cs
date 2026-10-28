using SteamRouteTool.Models;
using SteamRouteTool.Services;

namespace SteamRouteTool.Tests;

public class PingDisplayTests
{
    static readonly Relay A = new Relay("10.0.0.1", 27015, 27060);
    static readonly Relay B = new Relay("10.0.0.2", 27015, 27060);

    static PingText Describe(IEnumerable<Relay> relays, HashSet<Relay>? blocked = null, Dictionary<string, int?>? results = null,
        HashSet<string>? pending = null, Dictionary<string, int>? cached = null)
    {
        return PingDisplay.Describe(relays, blocked ?? [], results ?? [], pending ?? [], cached ?? []);
    }

    [Fact]
    public void Describe_ShowsTheBestFreshPingOfTheRoute()
    {
        PingText ping = Describe([A, B], results: new() { [A.Address] = null, [B.Address] = 17 });

        Assert.Equal(new PingText("17 ms", PingLevel.Good, 17), ping);
    }

    [Theory]
    [InlineData(50, PingLevel.Good)]
    [InlineData(51, PingLevel.Fair)]
    [InlineData(100, PingLevel.Fair)]
    [InlineData(101, PingLevel.Poor)]
    public void Describe_RatesLatency(int ms, PingLevel expected)
    {
        Assert.Equal(expected, Describe([A], results: new() { [A.Address] = ms }).Level);
    }

    [Fact]
    public void Describe_ShowsNoReplyWhenEveryTriedRelayFailed()
    {
        Assert.Equal(PingLevel.Failed, Describe([A, B], results: new() { [A.Address] = null }).Level);
    }

    [Fact]
    public void Describe_ShowsBlockedWhenEveryRelayIsBlocked()
    {
        PingText ping = Describe([A, B], blocked: [A, B], results: new() { [A.Address] = 20 });

        Assert.Equal(PingLevel.Blocked, ping.Level);
        Assert.Equal("blocked", ping.Text);
    }

    [Fact]
    public void Describe_IgnoresBlockedRelaysOfAPartlyBlockedRoute()
    {
        PingText ping = Describe([A, B], blocked: [A], results: new() { [A.Address] = 5, [B.Address] = 40 });

        Assert.Equal("40 ms", ping.Text);
    }

    [Fact]
    public void Describe_ShowsTheLastSessionsPingAsStaleUntilAFreshOneArrives()
    {
        Assert.Equal(new PingText("31 ms", PingLevel.Stale, 31), Describe([A], cached: new() { [A.Address] = 31 }));
        Assert.Equal(PingLevel.Stale, Describe([A], pending: [A.Address], cached: new() { [A.Address] = 31 }).Level);
    }

    [Fact]
    public void Describe_ShowsAnEllipsisWhilePingingWithoutACachedValue()
    {
        Assert.Equal("…", Describe([A], pending: [A.Address]).Text);
    }

    [Fact]
    public void Describe_SortsMeasuredPingsBeforeFailuresAndBlockedRoutes()
    {
        int measured = Describe([A], results: new() { [A.Address] = 300 }).SortValue;
        int failed = Describe([A], results: new() { [A.Address] = null }).SortValue;
        int blocked = Describe([A], blocked: [A]).SortValue;

        Assert.True(measured < failed && failed < blocked);
    }
}
