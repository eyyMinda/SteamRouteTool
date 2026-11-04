using System.Text.RegularExpressions;
using SteamRouteTool.Localization;

namespace SteamRouteTool.Tests;

public partial class LocalizationTests
{
    static readonly IReadOnlyDictionary<string, string> English = Loc.Load(Loc.DefaultLanguage);

    public static TheoryData<string> Translations => new TheoryData<string>(Loc.Languages.Select(language => language.Code).Where(code => code != Loc.DefaultLanguage));

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();

    [Fact]
    public void English_HasTexts()
    {
        Assert.True(English.Count > 100);
        Assert.Equal("Routes", English["Nav.Routes"]);
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void Translation_HasEveryEnglishTextAndNoOthers(string code)
    {
        IReadOnlyDictionary<string, string> translation = Loc.Load(code);

        Assert.Empty(English.Keys.Except(translation.Keys));
        Assert.Empty(translation.Keys.Except(English.Keys));
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void Translation_KeepsThePlaceholdersAndBoldMarkers(string code)
    {
        IReadOnlyDictionary<string, string> translation = Loc.Load(code);

        foreach ((string key, string text) in translation)
        {
            string english = English[key];
            // A singular text may leave out {0} (e.g. "the fastest route"); every other placeholder must stay.
            HashSet<string> expected = Placeholders(english);
            if (key.EndsWith(".One")) { expected.Remove("{0}"); }
            Assert.True(expected.IsSubsetOf(Placeholders(text)), $"{code} {key} is missing a placeholder: {text}");
            Assert.True(Placeholders(text).IsSubsetOf(Placeholders(english)), $"{code} {key} has an unknown placeholder: {text}");
            Assert.True(Count(text, "**") % 2 == 0, $"{code} {key} has an unpaired ** marker: {text}");
            Assert.False(string.IsNullOrWhiteSpace(text), $"{code} {key} is empty");
        }
    }

    [Fact]
    public void Plural_PicksOneOrOther()
    {
        Assert.Equal("1 relay", Loc.Plural("Routes.RelayCount", 1));
        Assert.Equal("5 relays", Loc.Plural("Routes.RelayCount", 5));
    }

    [Fact]
    public void T_FallsBackToTheKeyForUnknownTexts()
    {
        Assert.Equal("No.Such.Key", Loc.T("No.Such.Key"));
    }

    static HashSet<string> Placeholders(string text) => Placeholder().Matches(text).Select(match => match.Value).ToHashSet();

    static int Count(string text, string part) => (text.Length - text.Replace(part, "").Length) / part.Length;
}
