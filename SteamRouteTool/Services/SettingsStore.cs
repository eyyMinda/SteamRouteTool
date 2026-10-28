using System.Text.Json;

namespace SteamRouteTool.Services;

public sealed class AppSettings
{
    /// <summary>"System", "Light" or "Dark".</summary>
    public string Theme { get; set; } = "System";
    public bool PingOnStartup { get; set; } = true;
    public bool GroupByRegion { get; set; } = true;
    /// <summary>Remove all firewall rules when the app closes, and re-apply the game's profile when it starts.</summary>
    public bool UnblockOnExit { get; set; }
    /// <summary>How many of the fastest routes Auto select keeps.</summary>
    public int AutoRouteCount { get; set; } = AutoSelector.DefaultRouteCount;
    public int LastAppId { get; set; } = Models.Game.CounterStrike2;
    /// <summary>Saved block profile per game (by app ID), applied to the firewall when switching to that game.</summary>
    public Dictionary<int, GameProfile> Profiles { get; set; } = new Dictionary<int, GameProfile>();
}

public sealed class GameProfile
{
    /// <summary>Addresses of the relays blocked for this game.</summary>
    public List<string> BlockedRelays { get; set; } = new List<string>();
}

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON. Read and write failures fall back to defaults / are ignored.</summary>
public sealed class SettingsStore
{
    static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

    public SettingsStore(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static string DefaultDirectory => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamRouteTool");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(Path)) { return new AppSettings(); }
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
    }
}
