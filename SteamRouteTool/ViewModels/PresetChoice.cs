namespace SteamRouteTool.ViewModels;

/// <summary>A choice in the "apply when the game starts" list: a preset's name (empty for none) and the text shown.</summary>
public sealed record PresetChoice(string Key, string Text)
{
    public override string ToString() => Text;
}
