using System.Security.Cryptography;

namespace SteamRouteTool.Services;

/// <summary>Why an update couldn't be installed; <see cref="Exception.Message"/> is a text key (see Strings/).</summary>
public sealed class UpdateException : Exception
{
    public UpdateException(string textKey, string? detail = null) : base(textKey)
    {
        Detail = detail;
    }

    public string? Detail { get; }
}

/// <summary>
/// Installs a newer release from GitHub: downloads the same kind of exe as the running one next to it, checks it against the
/// release's SHA256SUMS.txt, then swaps it in. Windows lets a running exe be renamed but not overwritten, so the current exe
/// is renamed to .old (deleted on the next start) and the new one takes its name. The app runs as administrator, so it can
/// write next to its exe wherever that is.
/// </summary>
public static class Updater
{
    const string ChecksumsFile = "SHA256SUMS.txt";

    // Downloads take longer than the shared client's short timeout allows.
    static readonly HttpClient Downloads = CreateClient();

    static HttpClient CreateClient()
    {
        HttpClient client = new HttpClient() { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SteamRouteTool/" + AppInfo.VersionText);
        return client;
    }

    /// <summary>The running exe, or null when the app can't update itself (e.g. started through dotnet in development).</summary>
    public static string? ExePath
    {
        get
        {
            string? path = Environment.ProcessPath;
            if (path == null || Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) { return null; }
            return path;
        }
    }

    public static bool CanInstall => ExePath != null;

    /// <summary>The release file to download, e.g. "SteamRouteTool-2.1.1-win-x64.exe" or "...-win-x64-small.exe".</summary>
    public static string GetAssetName(Version version, bool selfContained)
    {
        return $"SteamRouteTool-{version.ToString(3)}-win-x64{(selfContained ? "" : "-small")}.exe";
    }

    /// <summary>The SHA-256 (lowercase hex) listed for a file in SHA256SUMS.txt ("&lt;hash&gt;  &lt;name&gt;" per line), or null.</summary>
    internal static string? FindChecksum(string checksums, string fileName)
    {
        foreach (string line in checksums.Split('\n'))
        {
            string[] parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[1].Trim().TrimStart('*').Equals(fileName, StringComparison.OrdinalIgnoreCase))
            {
                return parts[0].ToLowerInvariant();
            }
        }
        return null;
    }

    internal static string ComputeChecksum(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>Downloads and verifies the release's exe; returns the path of the verified file, next to the running exe.</summary>
    public static async Task<string> DownloadAsync(ReleaseInfo release, CancellationToken cancellation = default)
    {
        string exePath = ExePath ?? throw new UpdateException("Updates.NotSupported");
        string assetName = GetAssetName(release.Version, AppInfo.IsSelfContained);
        if (!release.Assets.TryGetValue(assetName, out string? exeUrl) || !release.Assets.TryGetValue(ChecksumsFile, out string? checksumsUrl))
        {
            throw new UpdateException("Updates.MissingDownload", assetName);
        }

        string newPath = exePath + ".new";
        try
        {
            string? expected = FindChecksum(await Downloads.GetStringAsync(checksumsUrl, cancellation), assetName);
            if (expected == null) { throw new UpdateException("Updates.MissingDownload", assetName); }

            using (Stream download = await Downloads.GetStreamAsync(exeUrl, cancellation))
            using (FileStream file = File.Create(newPath))
            {
                await download.CopyToAsync(file, cancellation);
            }
            if (ComputeChecksum(newPath) != expected)
            {
                File.Delete(newPath);
                throw new UpdateException("Updates.ChecksumMismatch");
            }
            return newPath;
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is UnauthorizedAccessException || ex is TaskCanceledException)
        {
            TryDelete(newPath);
            throw new UpdateException("Updates.InstallFailed", ex.Message);
        }
    }

    /// <summary>Puts the downloaded exe in place of the running one. If that fails, the running exe is put back.</summary>
    public static void Swap(string newPath)
    {
        string exePath = ExePath ?? throw new UpdateException("Updates.NotSupported");
        string oldPath = exePath + ".old";
        try
        {
            File.Move(exePath, oldPath, overwrite: true);
            File.Move(newPath, exePath);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            if (!File.Exists(exePath) && File.Exists(oldPath)) { File.Move(oldPath, exePath); }
            TryDelete(newPath);
            throw new UpdateException("Updates.InstallFailed", ex.Message);
        }
    }

    /// <summary>Removes what the last update left behind; the old exe can only be deleted once it's no longer running.</summary>
    public static void CleanUp()
    {
        if (ExePath is not string exePath) { return; }
        TryDelete(exePath + ".old");
        TryDelete(exePath + ".new");
    }

    static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
    }
}
