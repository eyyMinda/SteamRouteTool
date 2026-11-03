using System.Reflection;

namespace SteamRouteTool.Services;

/// <summary>Version and links shown in the app.</summary>
public static class AppInfo
{
    public const string RepositoryUrl = "https://github.com/eyyMinda/SteamRouteTool";
    public const string ReleasesUrl = RepositoryUrl + "/releases";
    public const string ChangelogUrl = RepositoryUrl + "/blob/main/CHANGELOG.md";
    public const string IssuesUrl = RepositoryUrl + "/issues";
    public const string LicenseUrl = RepositoryUrl + "/blob/main/LICENSE";
    public const string OriginalRepositoryUrl = "https://github.com/dfrood/SteamRouteTool";
    public const string AuthorUrl = "https://github.com/eyyMinda";
    public const string BuyMeACoffeeUrl = "https://buymeacoffee.com/eyyminda";

    /// <summary>The app version, e.g. 2.0.0 (from &lt;Version&gt; in the project file).</summary>
    public static Version Version { get; } = ReadVersion();

    public static string VersionText => Version.ToString(3);

    /// <summary>Whether this is the self-contained exe (false for the small one that needs the .NET Desktop Runtime).</summary>
    public static bool IsSelfContained { get; } = typeof(AppInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Any(attribute => attribute.Key == "SelfContained" && string.Equals(attribute.Value, "true", StringComparison.OrdinalIgnoreCase));

    static Version ReadVersion()
    {
        string? informational = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return Version.TryParse(informational?.Split('+', '-')[0], out Version? version) ? version : new Version(0, 0, 0);
    }
}
