using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using CineLibraryCS.Services;

namespace CineLibraryCS.Views;

/// <summary>
/// v5.3.0: the movie and TV details, coloured by kind: the label and a bar beside the value take the
/// colour, the value keeps the text colour so it reads as text. The facts sit in tiles in a row (ratings,
/// about the film, the file, the IDs), the people and production under the plot. The same on both pages;
/// the TV page's header is dark in both themes, so it asks for the dark colours.
/// </summary>
internal static class DetailField
{
    public enum Kind { Story, People, Production, File, Ids }

    public static SolidColorBrush Brush(Kind kind, ElementTheme? theme = null) => ThemeBrushes.Get($"Detail{kind}Brush", theme);

    public static TextBlock Label(string text, Brush brush) => new()
    {
        Text = text.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeights.SemiBold,
        CharacterSpacing = 150, Foreground = brush,
    };

    /// <summary>A label over its value, with the bar beside the value.</summary>
    public static StackPanel Field(Kind kind, string label, UIElement value, ElementTheme? theme = null)
    {
        var brush = Brush(kind, theme);
        var field = new StackPanel { Spacing = 5 };
        field.Children.Add(Label(label, brush));
        field.Children.Add(new Border
        {
            BorderBrush = brush, BorderThickness = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(10, 0, 0, 0), Child = value,
        });
        return field;
    }

    /// <summary>
    /// Names on one wrapping line, each a link when there's somewhere to go: no underline, and
    /// <paramref name="hover"/>'s colour with an underline while the pointer is on it. The commas are
    /// plain text right after each name, so none starts a line and none has a space before it. Past
    /// <paramref name="max"/> they end in "+3 more", all of them on hover.
    /// </summary>
    public static TextBlock Names(IReadOnlyList<string> names, Action<string>? onClick, ElementTheme? theme = null,
                                  int max = int.MaxValue, double fontSize = 15, Brush? hover = null)
    {
        var text = ThemeBrushes.Get("TextBrush", theme);
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = fontSize, Foreground = text, LineHeight = Math.Round(fontSize * 1.45) };
        var shown = names.Take(max).ToList();
        for (int i = 0; i < shown.Count; i++)
        {
            if (i > 0) block.Inlines.Add(new Run { Text = ", " });
            var name = shown[i];
            if (onClick == null) { block.Inlines.Add(new Run { Text = name }); continue; }
            var link = new Hyperlink { Foreground = text, UnderlineStyle = UnderlineStyle.None };
            link.Inlines.Add(new Run { Text = name });
            link.Click += (_, _) => onClick(name);
            block.Inlines.Add(link);
        }
        if (onClick != null)
        {
            // A Hyperlink has no pointer events of its own, so the block finds the one under the pointer
            var lit = hover ?? text;
            block.PointerMoved += (_, e) => Light(block, LinkAt(block, e.GetCurrentPoint(block).Position), text, lit);
            block.PointerExited += (_, _) => Light(block, null, text, lit);
            block.PointerCaptureLost += (_, _) => Light(block, null, text, lit);
        }
        if (names.Count > shown.Count)
        {
            block.Inlines.Add(new Run { Text = $" +{names.Count - shown.Count} more", Foreground = ThemeBrushes.Get("DetailKeyBrush", theme) });
            ToolTipService.SetToolTip(block, string.Join(", ", names));
        }
        return block;
    }

    /// <summary>
    /// The link under a point in the block, from the edges of its letters: each letter's left and right
    /// edge on its own line, so a name that wraps onto the next line counts on both.
    /// </summary>
    internal static Hyperlink? LinkAt(TextBlock block, Windows.Foundation.Point p)
    {
        foreach (var link in block.Inlines.OfType<Hyperlink>())
        {
            var start = link.ContentStart;
            int count = link.ContentEnd.Offset - start.Offset;
            for (int i = 0; i < count; i++)
            {
                var left = start.GetPositionAtOffset(i, LogicalDirection.Forward).GetCharacterRect(LogicalDirection.Forward);
                var right = start.GetPositionAtOffset(i + 1, LogicalDirection.Forward).GetCharacterRect(LogicalDirection.Backward);
                if (Math.Abs(left.Y - right.Y) > 1) continue;   // the letter's edges on two lines: an element's edge, not a letter
                if (p.X >= Math.Min(left.X, right.X) && p.X <= Math.Max(left.X, right.X) && p.Y >= left.Y && p.Y <= left.Y + left.Height)
                    return link;
            }
        }
        return null;
    }

    /// <summary>The link under the pointer in its colour and underlined; the rest as text.</summary>
    internal static void Light(TextBlock block, Hyperlink? on, Brush text, Brush lit)
    {
        foreach (var link in block.Inlines.OfType<Hyperlink>())
        {
            var style = link == on ? UnderlineStyle.Single : UnderlineStyle.None;
            if (link.UnderlineStyle == style) continue;
            link.UnderlineStyle = style;
            link.Foreground = link == on ? lit : text;
        }
    }

    /// <summary>
    /// A tile: as wide as what's in it, in a row of them (the WrapPanel gives each the row's height, so
    /// their bottoms line up). On the TV page's dark header a light wash, as its buttons have.
    /// </summary>
    public static Border Tile(UIElement content, ElementTheme? theme = null, bool wash = false) => new()
    {
        Child = content, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
        Padding = new Thickness(16, 13, 18, 13),
        Background = wash ? new SolidColorBrush(Windows.UI.Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)) : ThemeBrushes.Get("CardBrush", theme),
        BorderBrush = wash ? new SolidColorBrush(Windows.UI.Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)) : ThemeBrushes.Get("BorderBrush", theme),
    };

    /// <summary>Rows of a name and its value under one label: About the film, The file.</summary>
    public static StackPanel Section(Kind kind, string label, IReadOnlyList<(string Name, string Value)> rows, ElementTheme? theme = null)
    {
        var grid = new Grid { ColumnSpacing = 14, RowSpacing = 4 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < rows.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = new TextBlock { Text = rows[i].Name, FontSize = 13, Foreground = ThemeBrushes.Get("DetailKeyBrush", theme) };
            var value = new TextBlock
            {
                Text = rows[i].Value, FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
                MaxWidth = 300,   // a long list of languages wraps rather than stretching its tile across the page
                Foreground = ThemeBrushes.Get("TextBrush", theme),
            };
            Grid.SetRow(name, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            grid.Children.Add(name);
            grid.Children.Add(value);
        }
        return Field(kind, label, grid, theme);
    }

    /// <summary>The IMDb and TMDb IDs. The badges on the banner open the pages; a click here copies the ID.</summary>
    public static StackPanel Ids(IReadOnlyList<(string Site, string Id)> ids, ElementTheme? theme = null)
    {
        var grid = new Grid { ColumnSpacing = 14, RowSpacing = 2 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var mono = new FontFamily("Cascadia Mono, Consolas");
        for (int i = 0; i < ids.Count; i++)
        {
            var (site, id) = ids[i];
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = new TextBlock { Text = site, FontSize = 13, Foreground = ThemeBrushes.Get("DetailKeyBrush", theme), VerticalAlignment = VerticalAlignment.Center };
            var shown = new TextBlock { Text = id, FontSize = 13, FontFamily = mono, Foreground = ThemeBrushes.Get("TextBrush", theme) };
            var copy = new Button
            {
                Content = shown, Padding = new Thickness(4, 1, 4, 1), Margin = new Thickness(-4, 0, 0, 0), MinHeight = 0,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            ToolTipService.SetToolTip(copy, $"Copy the {site} ID");
            copy.Click += (_, _) => Copy(id, shown, copy.DispatcherQueue);
            Grid.SetRow(name, i);
            Grid.SetRow(copy, i);
            Grid.SetColumn(copy, 1);
            grid.Children.Add(name);
            grid.Children.Add(copy);
        }
        return Field(Kind.Ids, "IDs", grid, theme);
    }

    private static void Copy(string id, TextBlock shown, DispatcherQueue queue)
    {
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(id);
        try { Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package); }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { return; }   // another app holds the clipboard
        shown.Text = "Copied";
        var back = queue.CreateTimer();
        back.Interval = TimeSpan.FromSeconds(1.5);
        back.IsRepeating = false;
        back.Tick += (_, _) => shown.Text = id;
        back.Start();
    }

    /// <summary>Each rating source: the figure in its colour (IMDb gold, TMDb green), the source and votes beside it.</summary>
    public static StackPanel Ratings(IReadOnlyList<(string Source, double Value, int? Votes)> ratings, double? user, ElementTheme? theme = null)
    {
        var list = new StackPanel { Spacing = 6 };
        list.Children.Add(Label("Ratings", Brush(Kind.Ids, theme)));
        foreach (var (source, value, votes) in ratings)
        {
            var tomatoes = source.ToLowerInvariant() is "rottentomatoes" or "tomatometerallcritics" or "tomatometerallaudience";
            var figure = tomatoes
                ? (value * 10).ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + "%"   // out of 10 now; back to a percentage
                : value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            var colour = ThemeBrushes.Get(source.ToLowerInvariant() switch
            {
                "imdb" => "ImdbGoldBrush",
                "themoviedb" or "tmdb" => "TmdbGreenBrush",
                _ => "TextBrush",
            }, theme);
            list.Children.Add(Line(figure, colour, MovieDetailDialog.PrettySource(source) + (votes is > 0 ? " · " + MovieDetailDialog.FormatVotes(votes.Value) : ""), theme));
        }
        if (user is double mine)
            list.Children.Add(Line(mine.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "/10", ThemeBrushes.Get("TextBrush", theme), "User", theme));
        return list;
    }

    private static StackPanel Line(string figure, Brush colour, string caption, ElementTheme? theme)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        line.Children.Add(new TextBlock { Text = figure, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = colour });
        line.Children.Add(new TextBlock
        {
            Text = caption, FontSize = 12, Foreground = ThemeBrushes.Get("DetailKeyBrush", theme),
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2),
        });
        return line;
    }
}
