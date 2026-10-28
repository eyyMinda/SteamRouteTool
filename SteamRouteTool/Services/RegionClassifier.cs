namespace SteamRouteTool.Services;

/// <summary>
/// Assigns routes to a region. Route descriptions name a country or US state (e.g. "Dallas (Texas)"), which is the most
/// reliable signal: some routes in Steam's config have their coordinates swapped (e.g. "Datapacket Vienna" is listed at
/// [48.21, 16.36] instead of [16.36, 48.21]). Coordinates are only used for descriptions that name nothing known.
/// </summary>
public static class RegionClassifier
{
    public const string Europe = "Europe";
    public const string NorthAmerica = "North America";
    public const string SouthAmerica = "South America";
    public const string Asia = "Asia";
    public const string MiddleEast = "Middle East";
    public const string Africa = "Africa";
    public const string Oceania = "Oceania";
    public const string Other = "Other";

    static readonly (string Region, string[] Places)[] Places =
    {
        (Africa, ["South Africa", "Egypt", "Nigeria", "Kenya", "Morocco"]),
        (Europe, ["Netherlands", "Germany", "Austria", "Sweden", "Poland", "England", "United Kingdom", "France", "Spain", "Finland",
                  "Norway", "Denmark", "Italy", "Ireland", "Portugal", "Czech", "Romania", "Hungary", "Switzerland", "Belgium",
                  "Luxembourg", "Bulgaria", "Greece", "Russia", "Turkey", "Ukraine"]),
        // Checked before Asia, so e.g. "Indiana" doesn't match "India".
        (NorthAmerica, ["Washington", "California", "Texas", "Illinois", "Indiana", "Georgia", "Virginia", "New York", "Florida", "Oregon",
                        "Colorado", "Arizona", "Canada", "Mexico", "United States", "USA"]),
        (SouthAmerica, ["Peru", "Chile", "Argentina", "Brazil", "Colombia", "Uruguay"]),
        (MiddleEast, ["United Arab Emirates", "UAE", "Bahrain", "Israel", "Saudi", "Qatar"]),
        (Oceania, ["Australia", "New Zealand", "Guam"]),
        (Asia, ["India", "China", "Japan", "Korea", "Hong Kong", "Singapore", "Taiwan", "Philippines", "Thailand", "Malaysia",
                "Indonesia", "Vietnam"]),
    };

    /// <summary>Every region, in the order they're listed in filters.</summary>
    public static IReadOnlyList<string> All { get; } = [Europe, NorthAmerica, SouthAmerica, Asia, MiddleEast, Africa, Oceania, Other];

    public static string Classify(string description, double? longitude = null, double? latitude = null)
    {
        foreach ((string region, string[] places) in Places)
        {
            if (places.Any(place => description.Contains(place, StringComparison.OrdinalIgnoreCase))) { return region; }
        }
        return longitude is double lon && latitude is double lat ? ClassifyByCoordinates(lon, lat) : Other;
    }

    // Rough continental boxes; good enough for a fallback.
    internal static string ClassifyByCoordinates(double longitude, double latitude)
    {
        if (longitude < -30) { return latitude >= 13 ? NorthAmerica : SouthAmerica; }
        if ((longitude >= 110 && latitude < 0) || (longitude >= 140 && latitude < 25)) { return Oceania; }
        if (latitude >= 35 && longitude < 45) { return Europe; }
        if (longitude >= 34 && longitude < 60 && latitude >= 12 && latitude < 42) { return MiddleEast; }
        if (longitude < 52 && latitude < 35) { return Africa; }
        return Asia;
    }
}
