using SteamRouteTool.Models;
using SteamRouteTool.Services;

namespace SteamRouteTool.Tests;

public class GameWatcherTests
{
    [Fact]
    public void FindStarted_ReportsGamesThatWerentRunningBefore()
    {
        HashSet<int> before = [Game.CounterStrike2];
        HashSet<int> now = [Game.CounterStrike2, 570];

        Assert.Equal(["Dota 2"], GameWatcher.FindStarted(before, now).Select(game => game.Name));
    }

    [Fact]
    public void FindStarted_IgnoresGamesStillRunningOrClosed()
    {
        Assert.Empty(GameWatcher.FindStarted(new HashSet<int> { 570 }, new HashSet<int> { 570 }));
        Assert.Empty(GameWatcher.FindStarted(new HashSet<int> { 570 }, new HashSet<int>()));
    }

    [Fact]
    public void Games_HaveTheirProcessNames()
    {
        Assert.Equal(["cs2", "dota2", "project8"], Game.Supported.Select(game => game.ProcessName));
    }
}
