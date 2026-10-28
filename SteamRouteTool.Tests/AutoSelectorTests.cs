using SteamRouteTool.Models;
using SteamRouteTool.Services;

namespace SteamRouteTool.Tests;

public class AutoSelectorTests
{
    static Pop Route(string code) => new Pop(code, code, [new Relay("10.0.0.1", 27015, 27060)]);

    static readonly Pop Fra = Route("fra");
    static readonly Pop Ams = Route("ams");
    static readonly Pop Waw = Route("waw");
    static readonly Pop Iad = Route("iad");
    static readonly Pop Syd = Route("syd");

    [Fact]
    public void SelectFastest_KeepsTheFastestRoutesFastestFirst()
    {
        IReadOnlyList<Pop> kept = AutoSelector.SelectFastest([(Ams, 30), (Iad, 110), (Fra, 26), (Syd, 300), (Waw, 13)], count: 3);

        Assert.Equal([Waw, Fra, Ams], kept);
    }

    [Fact]
    public void SelectFastest_KeepsEveryKnownRouteWhenThereAreFewerThanTheCount()
    {
        Assert.Equal([Fra, Iad], AutoSelector.SelectFastest([(Iad, 110), (Fra, 26)], count: AutoSelector.DefaultRouteCount));
    }

    [Fact]
    public void SelectFastest_NeverKeepsRoutesWithoutAKnownPing()
    {
        IReadOnlyList<Pop> kept = AutoSelector.SelectFastest([(Ams, null), (Syd, 300), (Fra, null)], count: 3);

        Assert.Equal([Syd], kept);
    }

    [Fact]
    public void SelectFastest_KeepsTheOriginalOrderForEquallyFastRoutes()
    {
        Assert.Equal([Ams, Fra], AutoSelector.SelectFastest([(Ams, 26), (Fra, 26), (Waw, 26)], count: 2));
    }

    [Fact]
    public void SelectFastest_ReturnsNothingWhenNoPingIsKnown()
    {
        Assert.Empty(AutoSelector.SelectFastest([(Ams, null)], count: 5));
    }
}
