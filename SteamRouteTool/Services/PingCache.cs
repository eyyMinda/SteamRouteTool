using System.Text.Json;

namespace SteamRouteTool.Services;

/// <summary>
/// Remembers the last successful ping per relay address between sessions, so the list can show them right away
/// (as stale values) while fresh pings come in. Read and write failures are ignored: the cache is only a convenience.
/// </summary>
public sealed class PingCache
{
    readonly string path;

    public PingCache(string path)
    {
        this.path = path;
    }

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamRouteTool", "pings.json");

    public Dictionary<string, int> Load()
    {
        try
        {
            if (!File.Exists(path)) { return new Dictionary<string, int>(); }
            return JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path)) ?? new Dictionary<string, int>();
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
        {
            return new Dictionary<string, int>();
        }
    }

    public void Save(IReadOnlyDictionary<string, int> pings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(pings));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
    }
}
