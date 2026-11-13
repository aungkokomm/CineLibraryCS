using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using CineLibraryCS.Services;

namespace CineLibraryCS.Views;

/// <summary>
/// 5.4.1: awards as a list, by award (the Oscars first): each line WON in gold or NOMINATED
/// in grey, then what for, the film (a person's) or who won it (a film's), and the year.
/// On an actor's or director's page and in a movie's Awards tile.
/// </summary>
internal static class AwardsView
{
    public static StackPanel List(IReadOnlyCollection<Awards.Award> awards, bool forFilm)
    {
        var text = ThemeBrushes.Get("TextBrush");
        var muted = ThemeBrushes.Get("MutedBrush");
        var panel = new StackPanel { Spacing = 14 };
        foreach (var (family, items) in Awards.Group(awards))
        {
            var group = new StackPanel { Spacing = 5 };
            group.Children.Add(new TextBlock { Text = family, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = text });
            foreach (var a in items)
            {
                var row = new Grid { ColumnSpacing = 10 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(Pill(a.Won));
                var parts = new[] { Awards.What(a), forFilm ? a.Who : a.Work, a.Year?.ToString() }
                    .Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
                var line = new TextBlock
                {
                    Text = parts.Count > 0 ? string.Join(" · ", parts) : a.Name,
                    FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxWidth = 620,
                    Foreground = a.Won ? text : muted,
                };
                Grid.SetColumn(line, 1);
                row.Children.Add(line);
                group.Children.Add(row);
            }
            panel.Children.Add(group);
        }
        return panel;
    }

    private static Border Pill(bool won) => new()
    {
        CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 1, 7, 2),
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0),
        Background = new SolidColorBrush(won ? Windows.UI.Color.FromArgb(0x33, 0xF5, 0x9E, 0x0B) : Windows.UI.Color.FromArgb(0x26, 0x80, 0x80, 0x80)),
        Child = new TextBlock
        {
            Text = won ? "WON" : "NOMINATED", FontSize = 9, FontWeight = FontWeights.Bold, CharacterSpacing = 60,
            Foreground = won ? ThemeBrushes.Get("ImdbGoldBrush") : ThemeBrushes.Get("MutedBrush"),
        },
    };

    /// <summary>The awards in a flyout that scrolls, for a long list.</summary>
    public static Flyout Flyout(IReadOnlyCollection<Awards.Award> awards, bool forFilm) => new()
    {
        Content = new ScrollViewer
        {
            Content = List(awards, forFilm), MaxHeight = 480, MaxWidth = 720,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(4, 4, 16, 4),
        },
    };
}
