using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using SteamRouteTool.Models;

namespace SteamRouteTool.Services
{
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
            List<Pop> pops = new List<Pop>();
            foreach (KeyValuePair<string, JsonNode> entry in JsonNode.Parse(json)["pops"].AsObject())
            {
                if (entry.Value["relays"] is not JsonArray relays || relays.Count == 0) { continue; }

                string description = entry.Value["desc"]?.GetValue<string>() ?? entry.Key;
                List<Relay> parsedRelays = relays
                    .Select(relay => new Relay(
                        relay["ipv4"].GetValue<string>(),
                        relay["port_range"][0].GetValue<int>(),
                        relay["port_range"][1].GetValue<int>()))
                    .ToList();
                pops.Add(new Pop(entry.Key, description, parsedRelays));
            }
            return pops;
        }
    }
}
