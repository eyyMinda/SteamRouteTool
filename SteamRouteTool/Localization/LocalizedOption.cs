using CommunityToolkit.Mvvm.ComponentModel;

namespace SteamRouteTool.Localization;

/// <summary>
/// A choice in a list whose value stays the same in every language (e.g. a region or "Dark") while its text is translated.
/// Bind lists with SelectedValuePath="Key" and DisplayMemberPath="Text".
/// </summary>
public sealed class LocalizedOption : ObservableObject
{
    public LocalizedOption(string key, string textKey)
    {
        Key = key;
        TextKey = textKey;
        // Options live as long as the app, so the subscription is never removed.
        Loc.Instance.Changed += () => OnPropertyChanged(nameof(Text));
    }

    public string Key { get; }
    public string TextKey { get; }
    public string Text => Loc.T(TextKey);

    // Screen readers and the combo box's own text use this.
    public override string ToString() => Text;
}
