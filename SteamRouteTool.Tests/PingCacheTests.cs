using SteamRouteTool.Services;

namespace SteamRouteTool.Tests;

public sealed class PingCacheTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "SteamRouteTool.Tests", Guid.NewGuid().ToString("N"));

    string CachePath => Path.Combine(directory, "pings.json");

    public void Dispose()
    {
        if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Save_ThenLoad_ReturnsTheSamePings()
    {
        PingCache cache = new PingCache(CachePath);

        cache.Save(new Dictionary<string, int> { ["155.133.248.36"] = 31, ["185.25.180.2"] = 17 });

        Assert.Equal(new Dictionary<string, int> { ["155.133.248.36"] = 31, ["185.25.180.2"] = 17 }, new PingCache(CachePath).Load());
    }

    [Fact]
    public void Load_ReturnsEmptyWhenTheFileDoesNotExist()
    {
        Assert.Empty(new PingCache(CachePath).Load());
    }

    [Fact]
    public void Load_ReturnsEmptyWhenTheFileIsCorrupt()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(CachePath, "{ not json");

        Assert.Empty(new PingCache(CachePath).Load());
    }
}
