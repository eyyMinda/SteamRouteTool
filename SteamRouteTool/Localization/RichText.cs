using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SteamRouteTool.Localization;

/// <summary>
/// Fills a TextBlock from a text in which **double asterisks** mark bold parts, so translations can keep their emphasis:
/// <c>&lt;TextBlock l:RichText.Text="{l:Tr HowTo.Presets1}" /&gt;</c>.
/// </summary>
public static class RichText
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(RichText), new PropertyMetadata(null, OnTextChanged));

    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);

    public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);

    static void OnTextChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBlock block) { return; }
        block.Inlines.Clear();
        string[] parts = ((string?)e.NewValue ?? "").Split("**");
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) { continue; }
            // Odd parts are between a pair of markers.
            block.Inlines.Add(i % 2 == 1 ? new Bold(new Run(parts[i])) : new Run(parts[i]));
        }
    }
}
