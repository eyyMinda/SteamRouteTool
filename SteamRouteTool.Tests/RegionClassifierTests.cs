using SteamRouteTool.Services;

namespace SteamRouteTool.Tests;

public class RegionClassifierTests
{
    // Real route descriptions and coordinates from Steam's config.
    [Theory]
    [InlineData("Amsterdam (Netherlands)", 4.9, 52.37, RegionClassifier.Europe)]
    [InlineData("Stockholm - Kista (Sweden)", 17.9, 59.4, RegionClassifier.Europe)]
    [InlineData("Dallas (Texas)", -96.8, 32.77, RegionClassifier.NorthAmerica)]
    [InlineData("Atlanta (Georgia)", -84.39, 33.76, RegionClassifier.NorthAmerica)]
    [InlineData("Sao Paulo (Brazil)", -46.64, -23.53, RegionClassifier.SouthAmerica)]
    [InlineData("Dubai (United Arab Emirates)", 55.3, 25.25, RegionClassifier.MiddleEast)]
    [InlineData("Datapacket Fujairah (UAE)", 56.34, 25.12, RegionClassifier.MiddleEast)]
    [InlineData("Johannesburg (South Africa)", 28.0, -26.2, RegionClassifier.Africa)]
    [InlineData("Chennai - Ambattur (India)", 80.15, 13.11, RegionClassifier.Asia)]
    [InlineData("Seoul (South Korea)", 126.99, 37.56, RegionClassifier.Asia)]
    [InlineData("Hong Kong", 113.92, 22.31, RegionClassifier.Asia)]
    [InlineData("Singapore", 103.83, 1.28, RegionClassifier.Asia)]
    [InlineData("Alibaba Cloud Beijing - Mobile (China)", 116.38, 39.91, RegionClassifier.Asia)]
    [InlineData("Sydney (Australia)", 151.21, -33.86, RegionClassifier.Oceania)]
    [InlineData("Guam", 144.79, 13.44, RegionClassifier.Oceania)]
    public void Classify_UsesTheDescription(string description, double longitude, double latitude, string expected)
    {
        Assert.Equal(expected, RegionClassifier.Classify(description, longitude, latitude));
    }

    [Fact]
    public void Classify_IgnoresSwappedCoordinatesWhenTheDescriptionNamesACountry()
    {
        // Steam lists "Datapacket Vienna" at [48.21, 16.36]: latitude and longitude are swapped.
        Assert.Equal(RegionClassifier.Europe, RegionClassifier.Classify("Datapacket Vienna (Austria)", 48.21, 16.36));
    }

    [Fact]
    public void Classify_DoesNotMistakeIndianaForIndia()
    {
        Assert.Equal(RegionClassifier.NorthAmerica, RegionClassifier.Classify("Indianapolis (Indiana)"));
    }

    [Theory]
    [InlineData(-74.0, 40.7, RegionClassifier.NorthAmerica)]
    [InlineData(-58.4, -34.6, RegionClassifier.SouthAmerica)]
    [InlineData(13.4, 52.5, RegionClassifier.Europe)]
    [InlineData(139.7, 35.7, RegionClassifier.Asia)]
    [InlineData(174.8, -36.8, RegionClassifier.Oceania)]
    [InlineData(3.4, 6.5, RegionClassifier.Africa)]
    public void Classify_FallsBackToCoordinatesForUnknownPlaces(double longitude, double latitude, string expected)
    {
        Assert.Equal(expected, RegionClassifier.Classify("Somewhere new", longitude, latitude));
    }

    [Fact]
    public void Classify_ReturnsOtherWithoutDescriptionOrCoordinates()
    {
        Assert.Equal(RegionClassifier.Other, RegionClassifier.Classify("xyz"));
    }
}
