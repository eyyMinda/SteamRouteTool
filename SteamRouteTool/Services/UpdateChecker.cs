using System.Text.Json.Nodes;

namespace SteamRouteTool.Services;

public sealed record ReleaseInfo(Version Version, string Url);

/// <summary>Looks up the latest published release on GitHub.</summary>
public sealed class UpdateChecker
{
    readonly HttpClient httpClient;

    public UpdateChecker(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    /// <summary>Returns the latest release, or null if it can't be determined (offline, rate-limited, unexpected response).</summary>
    public async Task<ReleaseInfo?> GetLatestReleaseAsync()
    {
        try
        {
            string json = await httpClient.GetStringAsync("https://api.github.com/repos/eyyMinda/SteamRouteTool/releases/latest");
            return ParseRelease(json);
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
        {
            return null;
        }
    }

    internal static ReleaseInfo? ParseRelease(string json)
    {
        try
        {
            JsonNode? release = JsonNode.Parse(json);
            string? tag = release?["tag_name"]?.GetValue<string>();
            string? url = release?["html_url"]?.GetValue<string>();
            if (tag == null || url == null || !Version.TryParse(tag.TrimStart('v'), out Version? version)) { return null; }
            return new ReleaseInfo(version, url);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException || ex is InvalidOperationException)
        {
            return null;
        }
    }
}
