using Microsoft.UI.Xaml.Controls;

namespace CineLibraryCS.Views;

/// <summary>
/// 5.1.0 (#25): what the action buttons of a movie or show say, each with one icon, the one the
/// cards use, so To Watch looks the same wherever it is.
/// </summary>
internal static class ActionLabel
{
    public static StackPanel Favorite(bool on) => Make(on ? "\uEB52" : "\uEB51", on ? "Favorited" : "Favorite");
    public static StackPanel Watched(bool on) => Make("\uE73E", on ? "Watched" : "Mark Watched");
    public static StackPanel ToWatch(bool on) => Make(on ? "\uE842" : "\uE718", "To Watch");
    public static StackPanel Trailer() => Make("\uE714", "Trailer");
    public static StackPanel OpenFolder() => Make("\uE838", "Open Folder");
    public static StackPanel AddToList(string text = "Add to list") => Make("\uE8FD", text);
    public static StackPanel Note(string text) => Make("\uE70F", text);

    private static StackPanel Make(string glyph, string text)
    {
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        label.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14 });
        label.Children.Add(new TextBlock { Text = text });
        return label;
    }
}
