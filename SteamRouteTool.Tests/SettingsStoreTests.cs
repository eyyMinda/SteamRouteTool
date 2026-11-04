using SteamRouteTool.Services;

namespace SteamRouteTool.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "SteamRouteTool.Tests", Guid.NewGuid().ToString("N"));

    string SettingsPath => Path.Combine(directory, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Save_ThenLoad_KeepsSettingsAndGameProfiles()
    {
        AppSettings settings = new AppSettings { Theme = "Dark", PingOnStartup = false, GroupByRegion = false, UnblockOnExit = true, MinimizeToTray = true, CheckForUpdates = false, BlockNewRoutes = true, LastAppId = 570 };
        settings.Profiles[730] = new GameProfile { BlockedRelays = ["155.133.248.36", "155.133.248.37"] };
        settings.Profiles[570] = new GameProfile();
        settings.Presets[730] = [new GamePreset { Name = "Warsaw only", BlockedRoutes = ["ams", "fra"], BlockedRelays = ["155.133.230.50"] }];

        new SettingsStore(SettingsPath).Save(settings);
        AppSettings loaded = new SettingsStore(SettingsPath).Load();

        Assert.Equal("Dark", loaded.Theme);
        Assert.False(loaded.PingOnStartup);
        Assert.False(loaded.GroupByRegion);
        Assert.True(loaded.UnblockOnExit);
        Assert.True(loaded.MinimizeToTray);
        Assert.False(loaded.CheckForUpdates);
        Assert.True(loaded.BlockNewRoutes);
        Assert.Equal(570, loaded.LastAppId);
        Assert.Equal(["155.133.248.36", "155.133.248.37"], loaded.Profiles[730].BlockedRelays);
        Assert.Empty(loaded.Profiles[570].BlockedRelays);
        GamePreset preset = Assert.Single(loaded.Presets[730]);
        Assert.Equal("Warsaw only", preset.Name);
        Assert.Equal(["ams", "fra"], preset.BlockedRoutes);
        Assert.Equal(["155.133.230.50"], preset.BlockedRelays);
    }

    [Fact]
    public void Load_ReturnsDefaultsWhenTheFileIsMissingOrCorrupt()
    {
        Assert.Equal("System", new SettingsStore(SettingsPath).Load().Theme);

        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, "{ not json");
        AppSettings loaded = new SettingsStore(SettingsPath).Load();

        Assert.True(loaded.PingOnStartup);
        Assert.False(loaded.UnblockOnExit);
        Assert.Equal(730, loaded.LastAppId);
        Assert.Empty(loaded.Profiles);
    }

    [Fact]
    public void NewSettings_MinimizeToTray()
    {
        Assert.True(new AppSettings().MinimizeToTray);
    }
}
