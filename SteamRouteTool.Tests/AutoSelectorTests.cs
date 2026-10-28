using SteamRouteTool.Models;
using SteamRouteTool.Services;

namespace SteamRouteTool.Tests;

public class AutoSelectorTests
{
    static Pop Route(string code) => new Pop(code, code, [new Relay("10.0.0." + code.Length, 27015, 27060)]);

    static readonly Pop Fra = Route("fra");
    static readonly Pop Ams = Route("ams");
    static readonly Pop Waw = Route("waw");
    static readonly Pop Iad = Route("iad");
    static readonly Pop Syd = Route("syd");

    [Fact]
    public void SelectRoutesToKeep_KeepsRoutesUpToTheLimitFastestFirst()
    {
        IReadOnlyList<Pop> kept = AutoSelector.SelectRoutesToKeep([(Ams, 30), (Iad, 110), (Fra, 26), (Waw, 13)], maxPingMs: 80, minRoutes: 1);

        Assert.Equal([Waw, Fra, Ams], kept);
    }

    [Fact]
    public void SelectRoutesToKeep_IncludesRoutesExactlyAtTheLimit()
    {
        Assert.Equal([Ams], AutoSelector.SelectRoutesToKeep([(Ams, 80), (Iad, 81)], maxPingMs: 80, minRoutes: 1));
    }

    [Fact]
    public void SelectRoutesToKeep_KeepsTheMinimumNumberOfFastestRoutesWhenTooFewAreUnderTheLimit()
    {
        IReadOnlyList<Pop> kept = AutoSelector.SelectRoutesToKeep([(Syd, 300), (Iad, 110), (Fra, 26)], maxPingMs: 50, minRoutes: 2);

        Assert.Equal([Fra, Iad], kept);
    }

    [Fact]
    public void SelectRoutesToKeep_NeverKeepsRoutesWithoutAKnownPing()
    {
        IReadOnlyList<Pop> kept = AutoSelector.SelectRoutesToKeep([(Ams, null), (Syd, 300), (Fra, null)], maxPingMs: 50, minRoutes: 3);

        Assert.Equal([Syd], kept);
    }

    [Fact]
    public void SelectRoutesToKeep_ReturnsNothingWhenNoPingIsKnown()
    {
        Assert.Empty(AutoSelector.SelectRoutesToKeep([(Ams, null)], maxPingMs: 80, minRoutes: 3));
    }
}
