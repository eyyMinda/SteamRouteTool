using SteamRouteTool.Services;

namespace SteamRouteTool.Tests;

public class UpdateCheckerTests
{
    [Fact]
    public void ParseRelease_ReadsVersionAndUrlFromGitHub()
    {
        const string json = """{ "tag_name": "v1.1.1", "html_url": "https://github.com/eyyMinda/SteamRouteTool/releases/tag/v1.1.1" }""";

        ReleaseInfo? release = UpdateChecker.ParseRelease(json);

        Assert.Equal(new Version(1, 1, 1), release?.Version);
        Assert.Equal("https://github.com/eyyMinda/SteamRouteTool/releases/tag/v1.1.1", release?.Url);
    }

    [Theory]
    [InlineData("""{ "message": "Not Found" }""")]
    [InlineData("""{ "tag_name": "nightly", "html_url": "https://example.com" }""")]
    [InlineData("not json")]
    public void ParseRelease_ReturnsNullForUnexpectedResponses(string json)
    {
        Assert.Null(UpdateChecker.ParseRelease(json));
    }
}
