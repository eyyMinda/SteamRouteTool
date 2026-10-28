using System.Text.Json.Nodes;
using SteamRouteTool.Models;

namespace SteamRouteTool.Services;

/// <summary>Downloads and parses Steam's SDR network config (the list of routes and their relays).</summary>
public sealed class SdrConfigClient
{
    readonly HttpClient httpClient;
    readonly string configUrl;

    public SdrConfigClient(HttpClient httpClient, int appId = 730)
    {
        this.httpClient = httpClient;
        configUrl = "https://api.steampowered.com/ISteamApps/GetSDRConfig/v1?appid=" + appId;
    }

    public async Task<IReadOnlyList<Pop>> GetPopsAsync()
    {
        string json = await httpClient.GetStringAsync(configUrl);
        return ParsePops(json);
    }

    // Routes without relays (e.g. only reachable through partner networks) can't be blocked, so they're skipped.
    public static IReadOnlyList<Pop> ParsePops(string json)
    {
        JsonObject entries = JsonNode.Parse(json)?["pops"]?.AsObject()
            ?? throw new InvalidDataException("The network config has no \"pops\" section.");

        List<Pop> pops = new List<Pop>();
        foreach ((string code, JsonNode? entry) in entries)
        {
            if (entry?["relays"] is not JsonArray relays || relays.Count == 0) { continue; }

            string description = entry["desc"]?.GetValue<string>() ?? code;
            List<Relay> parsedRelays = relays.Select(relay => ParseRelay(code, relay)).ToList();
            pops.Add(new Pop(code, description, parsedRelays));
        }
        return pops;
    }

    static Relay ParseRelay(string code, JsonNode? relay)
    {
        string? address = relay?["ipv4"]?.GetValue<string>();
        if (address == null || relay?["port_range"] is not JsonArray { Count: 2 } ports)
        {
            throw new InvalidDataException($"Route \"{code}\" has a relay without an address or port range.");
        }
        return new Relay(address, ports[0]!.GetValue<int>(), ports[1]!.GetValue<int>());
    }
}
