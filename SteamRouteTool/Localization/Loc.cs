using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace SteamRouteTool.Localization;

/// <summary>A language the app is translated into. <see cref="Name"/> is the language's own name for itself.</summary>
public sealed record LanguageInfo(string Code, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// The app's texts in the chosen language, from Strings/&lt;code&gt;.json (embedded). Texts missing from a translation fall
/// back to English. XAML binds to the indexer through <see cref="TrExtension"/>, so switching languages updates the UI live;
/// view models listen to <see cref="Changed"/> to refresh texts they build themselves.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public const string DefaultLanguage = "en";

    public static IReadOnlyList<LanguageInfo> Languages { get; } =
    [
        new LanguageInfo("en", "English"),
        new LanguageInfo("de", "Deutsch"),
        new LanguageInfo("es", "Español"),
        new LanguageInfo("fr", "Français"),
        new LanguageInfo("pl", "Polski"),
        new LanguageInfo("pt-BR", "Português (Brasil)"),
        new LanguageInfo("ru", "Русский"),
        new LanguageInfo("tr", "Türkçe"),
        new LanguageInfo("zh-Hans", "简体中文"),
    ];

    public static Loc Instance { get; } = new Loc();

    readonly IReadOnlyDictionary<string, string> english;
    IReadOnlyDictionary<string, string> current;

    Loc()
    {
        english = Load(DefaultLanguage);
        current = english;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the language changed and the XAML texts were refreshed.</summary>
    public event Action? Changed;

    public string Language { get; private set; } = DefaultLanguage;

    public string this[string key] => current.GetValueOrDefault(key) ?? english.GetValueOrDefault(key) ?? key;

    /// <summary>The text for <paramref name="key"/>, with {0}, {1}, ... filled in.</summary>
    public static string T(string key, params object?[] args)
    {
        string text = Instance[key];
        return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
    }

    /// <summary>The "&lt;key&gt;.One" text when <paramref name="count"/> is 1, otherwise "&lt;key&gt;.Other"; {0} is the count.</summary>
    public static string Plural(string key, int count, params object?[] args)
    {
        return T(key + (count == 1 ? ".One" : ".Other"), [count, .. args]);
    }

    /// <summary>Switches to the language with this code; unknown codes switch to English.</summary>
    public void SetLanguage(string? code)
    {
        LanguageInfo language = Languages.FirstOrDefault(info => info.Code == code) ?? Languages[0];
        if (language.Code == Language) { return; }
        current = language.Code == DefaultLanguage ? english : Load(language.Code);
        Language = language.Code;
        CultureInfo culture = CultureInfo.GetCultureInfo(language.Code);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        Changed?.Invoke();
    }

    /// <summary>The texts of a language as stored in its file.</summary>
    internal static IReadOnlyDictionary<string, string> Load(string code)
    {
        using Stream? stream = typeof(Loc).Assembly.GetManifestResourceStream($"SteamRouteTool.Strings.{code}.json");
        if (stream == null) { return new Dictionary<string, string>(); }
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new Dictionary<string, string>();
    }
}
