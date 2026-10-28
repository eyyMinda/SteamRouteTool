using SteamRouteTool.Models;
using SteamRouteTool.Services;

namespace SteamRouteTool.Tests;

public class FirewallServiceTests
{
    static readonly Relay Ams1 = new Relay("155.133.248.36", 27015, 27060);
    static readonly Relay Ams2 = new Relay("155.133.248.37", 27015, 27060);
    static readonly Relay Sto1 = new Relay("185.25.180.1", 27015, 27044);
    static readonly Relay Sto2 = new Relay("185.25.180.2", 27015, 27140);
    static readonly Pop Ams = new Pop("ams", "Amsterdam (Netherlands)", [Ams1, Ams2]);
    static readonly Pop Sto = new Pop("sto", "Stockholm - Kista (Sweden)", [Sto1, Sto2]);

    [Theory]
    [InlineData("SteamRouteTool-UDP-ams", "ams")]
    [InlineData("SteamRouteTool-TCP-ams", "ams")]
    [InlineData("SteamRouteTool-ICMP-sto", "sto")]
    [InlineData("SteamRouteTool-UDP-ord-2", "ord-2")]
    public void ParseRouteCode_ReadsCurrentFormat(string ruleName, string expectedCode)
    {
        Assert.Equal(expectedCode, FirewallService.ParseRouteCode(ruleName));
    }

    [Theory]
    [InlineData("SteamRouteTool-ams")]          // 1.0.1 and earlier
    [InlineData("SteamRouteTool-UDP-")]         // no route code
    [InlineData("SteamRouteTool-GRE-ams")]      // unknown protocol
    [InlineData("CSGORoutingTool-UDP-ams")]     // another tool
    public void ParseRouteCode_RejectsOtherFormats(string ruleName)
    {
        Assert.Null(FirewallService.ParseRouteCode(ruleName));
    }

    [Fact]
    public void GetRuleName_RoundTripsThroughParseRouteCode()
    {
        Assert.Equal("ams", FirewallService.ParseRouteCode(FirewallService.GetRuleName("TCP", "ams")));
    }

    [Fact]
    public void ParseBlockedAddresses_GroupsByRouteAndStripsSubnetMasks()
    {
        Dictionary<string, HashSet<string>> blocked = FirewallService.ParseBlockedAddresses(
        [
            ("SteamRouteTool-UDP-ams", "155.133.248.36/255.255.255.255,155.133.248.37/255.255.255.255"),
            ("SteamRouteTool-ICMP-ams", "155.133.248.36/255.255.255.255"),
            ("SteamRouteTool-TCP-sto", "185.25.180.2/255.255.255.255"),
        ]);

        Assert.Equal(["155.133.248.36", "155.133.248.37"], blocked["ams"].Order());
        Assert.Equal(["185.25.180.2"], blocked["sto"]);
    }

    [Fact]
    public void ParseBlockedAddresses_IgnoresOutdatedRulesAndRulesWithoutAddresses()
    {
        Dictionary<string, HashSet<string>> blocked = FirewallService.ParseBlockedAddresses(
        [
            ("SteamRouteTool-ams", "155.133.248.36/255.255.255.255"),
            ("SteamRouteTool-UDP-sto", null),
        ]);

        Assert.Empty(blocked);
    }

    [Fact]
    public void MatchBlockedRelays_KeepsRelaysStillInTheConfig()
    {
        Dictionary<string, HashSet<string>> blocked = new()
        {
            ["ams"] = ["155.133.248.37"],
            ["sto"] = ["185.25.180.1", "185.25.180.2"],
        };

        HashSet<Relay> relays = FirewallService.MatchBlockedRelays([Ams, Sto], blocked);

        Assert.Equal(new HashSet<Relay> { Ams2, Sto1, Sto2 }, relays);
    }

    [Fact]
    public void MatchBlockedRelays_DropsRemovedRoutesAndRetiredRelays()
    {
        Dictionary<string, HashSet<string>> blocked = new()
        {
            ["ams"] = ["155.133.248.99"],      // relay no longer in the config
            ["dlhr"] = ["212.102.63.140"],     // route no longer in the config
        };

        Assert.Empty(FirewallService.MatchBlockedRelays([Ams, Sto], blocked));
    }

    [Fact]
    public void MatchBlockedRelays_OnlyMatchesAddressesUnderTheirOwnRoute()
    {
        Dictionary<string, HashSet<string>> blocked = new() { ["sto"] = ["155.133.248.36"] };

        Assert.Empty(FirewallService.MatchBlockedRelays([Ams, Sto], blocked));
    }

    [Fact]
    public void GetRemotePorts_SpansTheCombinedRangeOfAllRelays()
    {
        Assert.Equal("27015-27140", FirewallService.GetRemotePorts([Sto1, Sto2]));
        Assert.Equal("27015-27044", FirewallService.GetRemotePorts([Sto1]));
    }
}
