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

    [Fact]
    public void NormalizeAddresses_StripsMasksAndSorts()
    {
        Assert.Equal("155.133.248.36,155.133.248.37",
            FirewallService.NormalizeAddresses("155.133.248.37/255.255.255.255,155.133.248.36/255.255.255.255"));
    }

    [Fact]
    public void GetDesiredRules_CreatesOneRulePerProtocolForEachBlockedRoute()
    {
        List<FirewallService.RuleSpec> rules = FirewallService.GetDesiredRules([Ams, Sto], new HashSet<Relay> { Sto2 });

        Assert.Equal(["SteamRouteTool-UDP-sto", "SteamRouteTool-TCP-sto", "SteamRouteTool-ICMP-sto"], rules.Select(rule => rule.Name));
        Assert.All(rules, rule => Assert.Equal("185.25.180.2", rule.RemoteAddresses));
        Assert.Equal(["27015-27140", "27015-27140", "*"], rules.Select(rule => rule.RemotePorts));
        Assert.All(rules, rule => Assert.Equal("SteamRouteTool", rule.Grouping));
    }

    [Fact]
    public void PlanChanges_LeavesMatchingRulesAlone()
    {
        List<FirewallService.RuleSpec> desired = FirewallService.GetDesiredRules([Ams, Sto], new HashSet<Relay> { Ams1, Sto1 });

        (List<string> toRemove, List<FirewallService.RuleSpec> toAdd) = FirewallService.PlanChanges(desired, desired);

        Assert.Empty(toRemove);
        Assert.Empty(toAdd);
    }

    [Fact]
    public void PlanChanges_ReplacesChangedRulesAndRemovesStaleOnes()
    {
        List<FirewallService.RuleSpec> existing = FirewallService.GetDesiredRules([Ams, Sto], new HashSet<Relay> { Ams1, Sto1 });
        // Rule from an earlier version: no group, so it differs from the desired rule.
        existing[0] = existing[0] with { Grouping = null };
        existing.Add(existing[1] with { Name = "SteamRouteTool-UDP-dlhr" });
        List<FirewallService.RuleSpec> desired = FirewallService.GetDesiredRules([Ams, Sto], new HashSet<Relay> { Ams1 });

        (List<string> toRemove, List<FirewallService.RuleSpec> toAdd) = FirewallService.PlanChanges(existing, desired);

        Assert.Equal(
            ["SteamRouteTool-UDP-ams", "SteamRouteTool-UDP-sto", "SteamRouteTool-TCP-sto", "SteamRouteTool-ICMP-sto", "SteamRouteTool-UDP-dlhr"],
            toRemove);
        Assert.Equal(["SteamRouteTool-UDP-ams"], toAdd.Select(rule => rule.Name));
    }

    [Fact]
    public void PlanChanges_RemovesEveryDuplicateAndAddsOneBack()
    {
        List<FirewallService.RuleSpec> desired = FirewallService.GetDesiredRules([Ams], new HashSet<Relay> { Ams1 });
        List<FirewallService.RuleSpec> existing = [.. desired, desired[0]];

        (List<string> toRemove, List<FirewallService.RuleSpec> toAdd) = FirewallService.PlanChanges(existing, desired);

        Assert.Equal(["SteamRouteTool-UDP-ams", "SteamRouteTool-UDP-ams"], toRemove);
        Assert.Equal([desired[0]], toAdd);
    }
}
