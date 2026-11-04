using SteamRouteTool.Models;
using SteamRouteTool.Services;

namespace SteamRouteTool.Tests;

public class PresetLogicTests
{
    static readonly Relay Ams1 = new Relay("155.133.248.36", 27015, 27060);
    static readonly Relay Ams2 = new Relay("155.133.248.37", 27015, 27060);
    static readonly Relay Waw1 = new Relay("155.133.230.34", 27015, 27060);
    static readonly Relay Waw2 = new Relay("155.133.230.50", 27015, 27060);
    static readonly Relay Fra1 = new Relay("155.133.226.68", 27015, 27060);

    static readonly Pop Ams = new Pop("ams", "Amsterdam (Netherlands)", [Ams1, Ams2]);
    static readonly Pop Waw = new Pop("waw", "Warsaw (Poland)", [Waw1, Waw2]);
    static readonly Pop Fra = new Pop("fra", "Frankfurt (Germany)", [Fra1]);
    static readonly Pop[] Pops = [Ams, Waw, Fra];

    [Fact]
    public void Capture_SavesWholeRoutesByCodeAndPartlyBlockedRoutesByRelay()
    {
        GamePreset preset = PresetLogic.Capture("EU", Pops, new HashSet<Relay> { Ams1, Ams2, Waw2 });

        Assert.Equal("EU", preset.Name);
        Assert.Equal(["ams"], preset.BlockedRoutes);
        Assert.Equal(["155.133.230.50"], preset.BlockedRelays);
    }

    [Fact]
    public void Resolve_BlocksTheSavedRoutesAndRelays()
    {
        GamePreset preset = new GamePreset { BlockedRoutes = ["ams"], BlockedRelays = ["155.133.230.50"] };

        Assert.Equal(new HashSet<Relay> { Ams1, Ams2, Waw2 }, PresetLogic.Resolve(preset, Pops));
    }

    [Fact]
    public void Resolve_BlocksRelaysAddedToAWholeRouteLater()
    {
        GamePreset preset = PresetLogic.Capture("No Warsaw", Pops, new HashSet<Relay> { Waw1, Waw2 });
        Relay added = new Relay("155.133.230.99", 27015, 27060);
        Pop[] updated = [Ams, new Pop("waw", "Warsaw (Poland)", [Waw1, Waw2, added]), Fra];

        Assert.Contains(added, PresetLogic.Resolve(preset, updated));
    }

    [Fact]
    public void Resolve_SkipsRoutesAndRelaysThatNoLongerExist()
    {
        GamePreset preset = new GamePreset { BlockedRoutes = ["ams", "gone"], BlockedRelays = ["10.0.0.1"] };

        Assert.Equal(new HashSet<Relay> { Ams1, Ams2 }, PresetLogic.Resolve(preset, Pops));
    }

    [Fact]
    public void Matches_OnlyWhenExactlyThePresetsRelaysAreBlocked()
    {
        GamePreset preset = PresetLogic.Capture("No Warsaw", Pops, new HashSet<Relay> { Waw1, Waw2 });

        Assert.True(PresetLogic.Matches(preset, Pops, new HashSet<Relay> { Waw1, Waw2 }));
        Assert.False(PresetLogic.Matches(preset, Pops, new HashSet<Relay> { Waw1 }));
        Assert.False(PresetLogic.Matches(preset, Pops, new HashSet<Relay> { Waw1, Waw2, Fra1 }));
    }

    [Fact]
    public void Matches_AnEmptyPresetWhenNothingIsBlocked()
    {
        GamePreset allOpen = PresetLogic.Capture("All open", Pops, new HashSet<Relay>());

        Assert.True(PresetLogic.Matches(allOpen, Pops, new HashSet<Relay>()));
    }

    [Theory]
    [InlineData("  EU only  ", "EU only")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void CleanName_TrimsAndRefusesEmptyNames(string? name, string? expected)
    {
        Assert.Equal(expected, PresetLogic.CleanName(name));
    }

    [Fact]
    public void CleanName_ShortensLongNames()
    {
        Assert.Equal(PresetLogic.MaxNameLength, PresetLogic.CleanName(new string('x', 60))!.Length);
    }

    [Fact]
    public void Find_IgnoresCase()
    {
        GamePreset eu = new GamePreset { Name = "EU blocked" };

        Assert.Same(eu, PresetLogic.Find([new GamePreset { Name = "Warsaw only" }, eu], "eu BLOCKED"));
        Assert.Null(PresetLogic.Find([eu], "EU"));
    }

    [Theory]
    [InlineData(0, 0, "Nothing blocked")]
    [InlineData(1, 0, "1 route blocked")]
    [InlineData(12, 0, "12 routes blocked")]
    [InlineData(0, 1, "1 relay blocked")]
    [InlineData(12, 2, "12 routes and 2 relays blocked")]
    public void Describe_CountsRoutesAndRelays(int routes, int relays, string expected)
    {
        GamePreset preset = new GamePreset
        {
            BlockedRoutes = Enumerable.Range(0, routes).Select(i => "r" + i).ToList(),
            BlockedRelays = Enumerable.Range(0, relays).Select(i => "10.0.0." + i).ToList()
        };

        Assert.Equal(expected, PresetLogic.Describe(preset));
    }

    [Fact]
    public void Capture_RemembersEveryRouteThatExisted()
    {
        Assert.Equal(["ams", "waw", "fra"], PresetLogic.Capture("Any", Pops, new HashSet<Relay>()).KnownRoutes);
    }

    [Fact]
    public void Resolve_BlocksRoutesAddedAfterThePresetOnlyWhenAsked()
    {
        GamePreset warsawOnly = PresetLogic.Capture("Warsaw only", [Ams, Waw], new HashSet<Relay> { Ams1, Ams2 });

        Assert.DoesNotContain(Fra1, PresetLogic.Resolve(warsawOnly, Pops));
        Assert.Contains(Fra1, PresetLogic.Resolve(warsawOnly, Pops, blockNewRoutes: true));
        Assert.DoesNotContain(Waw1, PresetLogic.Resolve(warsawOnly, Pops, blockNewRoutes: true));
    }

    [Fact]
    public void Resolve_LeavesUnknownRoutesAloneForPresetsSavedBeforeRoutesWereRemembered()
    {
        GamePreset old = new GamePreset { BlockedRoutes = ["ams"] };

        Assert.Equal(new HashSet<Relay> { Ams1, Ams2 }, PresetLogic.Resolve(old, Pops, blockNewRoutes: true));
    }

    [Fact]
    public void FindNewRoutes_ListsRoutesNotSeenBefore()
    {
        Assert.Equal([Fra], PresetLogic.FindNewRoutes(["ams", "waw"], Pops));
        Assert.Empty(PresetLogic.FindNewRoutes(["ams", "waw", "fra"], Pops));
    }

    [Fact]
    public void FindNewRoutes_ReportsNothingTheFirstTime()
    {
        Assert.Empty(PresetLogic.FindNewRoutes(null, Pops));
        Assert.Empty(PresetLogic.FindNewRoutes([], Pops));
    }
}
