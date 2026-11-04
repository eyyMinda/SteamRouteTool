using SteamRouteTool.Services;

namespace SteamRouteTool.Tests;

public sealed class UpdaterTests : IDisposable
{
    readonly string file = Path.Combine(Path.GetTempPath(), "SteamRouteTool.Tests", Guid.NewGuid().ToString("N") + ".exe");

    public void Dispose()
    {
        if (File.Exists(file)) { File.Delete(file); }
    }

    [Theory]
    [InlineData(true, "SteamRouteTool-2.1.1-win-x64.exe")]
    [InlineData(false, "SteamRouteTool-2.1.1-win-x64-small.exe")]
    public void GetAssetName_PicksTheSameKindOfExe(bool selfContained, string expected)
    {
        Assert.Equal(expected, Updater.GetAssetName(new Version(2, 1, 1), selfContained));
    }

    [Fact]
    public void FindChecksum_ReadsTheLineForTheFile()
    {
        const string sums = "AAAA1111  SteamRouteTool-2.1.1-win-x64.exe\r\nbbbb2222  SteamRouteTool-2.1.1-win-x64-small.exe\r\n";

        Assert.Equal("aaaa1111", Updater.FindChecksum(sums, "SteamRouteTool-2.1.1-win-x64.exe"));
        Assert.Equal("bbbb2222", Updater.FindChecksum(sums, "SteamRouteTool-2.1.1-win-x64-small.exe"));
        Assert.Null(Updater.FindChecksum(sums, "SteamRouteTool-2.1.0-win-x64.exe"));
    }

    [Fact]
    public void FindChecksum_AcceptsTheBinaryModeMarker()
    {
        Assert.Equal("cccc3333", Updater.FindChecksum("cccc3333 *app.exe", "app.exe"));
    }

    [Fact]
    public void ComputeChecksum_IsTheLowercaseSha256()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "abc");

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Updater.ComputeChecksum(file));
    }
}
