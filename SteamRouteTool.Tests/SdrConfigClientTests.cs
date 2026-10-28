using SteamRouteTool.Models;
using SteamRouteTool.Services;

namespace SteamRouteTool.Tests;

public class SdrConfigClientTests
{
    // Trimmed-down shape of Steam's GetSDRConfig response.
    const string Config = """
        {
          "revision": 1,
          "pops": {
            "ams": {
              "desc": "Amsterdam (Netherlands)",
              "relays": [
                { "ipv4": "155.133.248.36", "port_range": [27015, 27060] },
                { "ipv4": "155.133.248.37", "port_range": [27015, 27060] }
              ]
            },
            "sto": {
              "desc": "Stockholm - Kista (Sweden)",
              "relays": [
                { "ipv4": "185.25.180.1", "port_range": [27015, 27044] },
                { "ipv4": "185.25.180.2", "port_range": [27015, 27140] }
              ]
            },
            "pek": { "desc": "Beijing (China)" },
            "xyz": { "relays": [ { "ipv4": "10.0.0.1", "port_range": [27015, 27060] } ] }
          }
        }
        """;

    [Fact]
    public void ParsePops_ReadsRoutesAndRelays()
    {
        Pop ams = SdrConfigClient.ParsePops(Config).Single(pop => pop.Code == "ams");

        Assert.Equal("Amsterdam (Netherlands)", ams.Description);
        Assert.Equal(
            [new Relay("155.133.248.36", 27015, 27060), new Relay("155.133.248.37", 27015, 27060)],
            ams.Relays);
    }

    [Fact]
    public void ParsePops_KeepsEachRelaysOwnPortRange()
    {
        Pop sto = SdrConfigClient.ParsePops(Config).Single(pop => pop.Code == "sto");

        Assert.Equal([27044, 27140], sto.Relays.Select(relay => relay.MaxPort));
    }

    [Fact]
    public void ParsePops_SkipsRoutesWithoutRelays()
    {
        IReadOnlyList<Pop> pops = SdrConfigClient.ParsePops(Config);

        Assert.DoesNotContain(pops, pop => pop.Code == "pek");
        Assert.Equal(["ams", "sto", "xyz"], pops.Select(pop => pop.Code));
    }

    [Fact]
    public void ParsePops_UsesCodeWhenDescriptionIsMissing()
    {
        Pop xyz = SdrConfigClient.ParsePops(Config).Single(pop => pop.Code == "xyz");

        Assert.Equal("xyz", xyz.Description);
    }

    [Fact]
    public void ParsePops_ThrowsWhenPopsSectionIsMissing()
    {
        Assert.Throws<InvalidDataException>(() => SdrConfigClient.ParsePops("""{ "revision": 1 }"""));
    }

    [Fact]
    public void ParsePops_ThrowsWhenRelayHasNoPortRange()
    {
        const string config = """{ "pops": { "ams": { "relays": [ { "ipv4": "155.133.248.36" } ] } } }""";

        Assert.Throws<InvalidDataException>(() => SdrConfigClient.ParsePops(config));
    }
}
