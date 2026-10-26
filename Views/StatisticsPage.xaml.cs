using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using CineLibraryCS.Models;
using CineLibraryCS.Services;

namespace CineLibraryCS.Views;

/// <summary>
/// v4.8.0: the Statistics page as a dashboard: a summary strip, quality and
/// storage, what's in the library, your watching, people, and the best-rated
/// films not watched yet. A bar, column, person or card that has a matching
/// library view opens it.
/// </summary>
public sealed partial class StatisticsPage : Page
{
    private DashboardStats? _stats;
    private int _loadToken;
    // The window and the page's Loaded both refresh on opening; the second shares the first's read
    private Task<DashboardStats>? _reading;

    // Grids that put their cards side by side on a wide page and one under the
    // other on a narrow one; rows that show as many people or cards as fit.
    private readonly List<(Grid Grid, int Wide)> _flows = new();
    private readonly List<Grid> _peopleRows = new();
    private readonly List<(ItemsRepeater Repeater, List<MovieListItem> Movies)> _cardRows = new();
    private readonly FontFamily _icons = new("Segoe Fluent Icons,Segoe MDL2 Assets");

    public StatisticsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
        // the chart colours are picked per theme
        ActualThemeChanged += (_, _) => { if (_stats != null) Build(_stats); };
    }

    public async void Refresh()
    {
        var token = ++_loadToken;
        var connected = new Dictionary<string, string>(AppState.Instance.Connected);
        var reading = _reading ??= Task.Run(() => AppState.Instance.Db.GetDashboardStats(connected));
        DashboardStats stats;
        try { stats = await reading; }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Statistics: {ex.Message}"); return; }
        finally { if (ReferenceEquals(_reading, reading)) _reading = null; }
        if (token != _loadToken) return;
        _stats = stats;
        Build(stats);
    }

    // v3.7.2: the page column gets an explicit width. With MaxWidth alone,
    // WinUI centred it by the width it asked for, so a small library pushed
    // it right and cut off the right-hand tiles.
    private void OnPageScrollerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var inner = e.NewSize.Width - PageScroller.Padding.Left - PageScroller.Padding.Right;
        PageStack.Width = Math.Clamp(inner, 0, 1100);
        Reflow();
    }

    // ── Colours ──────────────────────────────────────────────────────────────

    private bool Light => ActualTheme == ElementTheme.Light;
    private static SolidColorBrush Rgb(uint rgb) =>
        new(Windows.UI.Color.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
    // Checked for colour-blind separation and for contrast on the card in each theme
    private SolidColorBrush Series1 => Rgb(Light ? 0x7C5CE0u : 0x8B6FF0u);
    private SolidColorBrush Series2 => Rgb(Light ? 0x14A37Au : 0x1FA982u);
    // 4K, 1080p, 720p, SD: one hue, the better the stronger
    private SolidColorBrush[] QualityRamp => (Light
        ? new uint[] { 0x5034B0, 0x7354DB, 0x9479F0, 0xB9A6FA }
        : new uint[] { 0xB9A6FA, 0x8B6FF0, 0x6A50D4, 0x4F3AA8 }).Select(Rgb).ToArray();
    private SolidColorBrush GreyBrush => Rgb(Light ? 0xC9C9D6u : 0x3A3A4Cu);
    private static SolidColorBrush TrackBrush => ThemeBrushes.Get("BorderBrush");
    private static SolidColorBrush TextBrush => ThemeBrushes.Get("TextBrush");

    // ── Page ─────────────────────────────────────────────────────────────────

    private void Build(DashboardStats s)
    {
        _flows.Clear();
        _peopleRows.Clear();
        _cardRows.Clear();
        SectionsHost.Children.Clear();
        BuildSummary(s);
        if (s.Movies > 0)
        {
            SectionsHost.Children.Add(QualitySection(s));
            SectionsHost.Children.Add(ContentsSection(s));
            if (s.Shows > 0) SectionsHost.Children.Add(TvSection(s));
            SectionsHost.Children.Add(WatchingSection(s));
            if (s.Actors.Count + s.Directors.Count > 0) SectionsHost.Children.Add(PeopleSection(s));
            if (s.WorthWatching.Count > 0)
            {
                var next = Section("Worth watching next", "the best rated you haven't watched");
                next.Children.Add(CardRow(s.WorthWatching));
                SectionsHost.Children.Add(next);
            }
        }
        Reflow();
    }

    private void BuildSummary(DashboardStats s)
    {
        SummaryGrid.Children.Clear();
        // v4.8.1 (#25): the grid outlives a visit, so its columns go too; Arrange
        // skips a grid that already has its columns and the new tiles stacked in one
        SummaryGrid.ColumnDefinitions.Clear();
        SummaryGrid.RowDefinitions.Clear();
        var hours = s.RuntimeMinutes / 60;
        var watched = s.Movies > 0 ? 100.0 * s.Watched / s.Movies : 0;
        SummaryGrid.Children.Add(Tile("", s.Movies.ToString("N0"), "Movies",
            s.Archived > 0 ? $"{s.Archived:N0} in Watched & Gone" : null));
        SummaryGrid.Children.Add(Tile("", s.Shows.ToString("N0"), "TV shows", $"{s.Episodes:N0} episodes"));
        SummaryGrid.Children.Add(Tile("", s.Bytes > 0 ? Size(s.Bytes) : "Unknown", "Storage",
            $"on {s.DrivesUsed} drive{(s.DrivesUsed == 1 ? "" : "s")}"));
        SummaryGrid.Children.Add(Tile("", hours >= 48 ? $"{hours / 24:N0} days" : $"{hours:N0} hours", "Running time",
            $"{hours:N0} hours of movies"));
        SummaryGrid.Children.Add(Tile("", $"{watched:F0}%", "Watched", $"{s.Watched:N0} of {s.Movies:N0} movies"));
        _flows.Add((SummaryGrid, 5));

        MissingHint.Text = $"⚠ {s.Missing:N0} movie{(s.Missing == 1 ? " is" : "s are")} marked missing. Clean up on the Drives page.";
        MissingHint.Visibility = s.Missing > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private FrameworkElement QualitySection(DashboardStats s)
    {
        var section = Section("Quality and storage",
            s.Bytes > 0 ? $"{Size(s.Bytes)} on {s.DrivesUsed} drive{(s.DrivesUsed == 1 ? "" : "s")}" : null);

        var quality = QualityCard("Picture quality", s.Quality);

        // Storage: each drive, biggest first; a click shows its movies
        var maxBytes = Math.Max(1, s.Drives.Count > 0 ? s.Drives.Max(d => d.Bytes) : 1);
        var drives = new StackPanel { Spacing = 4 };
        foreach (var d in s.Drives)
        {
            var bits = $"{Size(d.Bytes)} · {d.Movies:N0} movies" + (d.Shows > 0 ? $" · {d.Shows:N0} shows" : "");
            drives.Children.Add(TallRow(d.Label, (double)d.Bytes / maxBytes, bits, Series1,
                d.Movies > 0 ? () => Go(mw => mw.NavigateLibraryByDrive(d.Serial, d.Label)) : null));
        }
        var storage = Card("Storage", null, drives);
        section.Children.Add(Flow(2, quality, storage));

        var video = Card("Video format", null, Rows(s.VideoFormats));
        var formats = s.AudioFormats.Where(f => f.Label != "Unknown").ToList();
        var sound = formats.Count == 0
            ? Card("Sound", null, Rows(s.Channels))
            : Card("Sound", null, Rows(s.Channels),
                Text("Formats: " + string.Join("  ·  ", formats.Select(f => $"{f.Label} {f.Count:N0}")), 12, dim: true, wrap: true));
        section.Children.Add(Flow(2, video, sound));
        return section;
    }

    /// <summary>Picture quality: the whole as one bar, then each part in its colour.</summary>
    private Border QualityCard(string title, List<StatCount> quality)
    {
        var ramp = QualityRamp;
        // each quality keeps its colour; one nothing has (no 4K episodes, say) isn't listed
        SolidColorBrush QualityBrush(StatCount q) => q.Label switch
        {
            "4K" => ramp[0], "1080p" => ramp[1], "720p" => ramp[2], "SD" => ramp[3], _ => GreyBrush,
        };
        quality = quality.Where(q => q.Count > 0).ToList();
        var parts = quality.Select(q => (q, Brush: QualityBrush(q))).ToList();
        var whole = new Grid { Height = 12, ColumnSpacing = 2 };
        for (int i = 0; i < parts.Count; i++)
        {
            whole.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(parts[i].q.Count, GridUnitType.Star) });
            var piece = new Border
            {
                Background = parts[i].Brush,
                CornerRadius = new CornerRadius(i == 0 ? 4 : 0, i == parts.Count - 1 ? 4 : 0, i == parts.Count - 1 ? 4 : 0, i == 0 ? 4 : 0),
            };
            Grid.SetColumn(piece, i);
            whole.Children.Add(piece);
        }
        return Card(title, null, whole, Rows(quality, colour: (q, _) => QualityBrush(q)));
    }

    // v4.8.0 (#25): the TV shows on their own: how far you are, their status,
    // genres, studios and networks, the biggest ones and the episodes' quality
    private FrameworkElement TvSection(DashboardStats s)
    {
        var hours = s.TvRuntimeMinutes / 60;
        var section = Section("TV shows", string.Join(" · ", new[]
        {
            $"{s.Shows:N0} shows", $"{s.Episodes:N0} episodes",
            hours > 0 ? $"{hours:N0} hours" : null,
            s.TvBytes > 0 ? Size(s.TvBytes) : null,
        }.Where(t => t != null)));

        var progress = Card("Your progress", "shows with episodes",
            Rows(s.TvProgress, p => Go(mw => mw.NavigateContinueWatching()), unit: "shows",
                colour: (p, _) => p.Label switch { "Finished" => Series2, "In progress" => Series1, _ => GreyBrush },
                openWhen: p => p.Label == "In progress"));
        var status = Card("Status", null, Rows(s.TvStatus, unit: "shows"));
        section.Children.Add(Flow(2, progress, status));

        var genres = Card("Genres", "share of your shows",
            Rows(s.TvGenres, g => Go(mw => mw.NavigateLibraryByGenre(g.Label)), total: Math.Max(1, s.Shows)));
        var studios = Card("Studios and networks", "share of your shows", Rows(s.TvStudios, total: Math.Max(1, s.Shows)));
        section.Children.Add(Flow(2, genres, studios));

        // the most episodes on disk; a click opens the show
        var maxEpisodes = Math.Max(1, s.TvBiggest.Count > 0 ? s.TvBiggest.Max(b => b.Episodes) : 1);
        var biggest = new StackPanel { Spacing = 4 };
        foreach (var b in s.TvBiggest)
            biggest.Children.Add(TallRow(b.Title, (double)b.Episodes / maxEpisodes,
                $"{b.Episodes:N0} episodes · {b.Watched:N0} watched", Series1, () => Go(mw => mw.OpenTvShow(b.Id))));
        section.Children.Add(Flow(2, Card("The biggest shows", "episodes in your library", biggest),
            QualityCard("Episode quality", s.TvQuality)));
        return section;
    }

    private FrameworkElement ContentsSection(DashboardStats s)
    {
        var section = Section("What's in it", null);
        var here = Math.Max(1, s.Movies - s.Missing);

        if (s.Decades.Count > 0)
        {
            var known = s.Decades.ToDictionary(d => d.Decade);
            var all = Enumerable.Range(0, (s.Decades[^1].Decade - s.Decades[0].Decade) / 10 + 1)
                .Select(i => known.GetValueOrDefault(s.Decades[0].Decade + i * 10, new DecadeCount(s.Decades[0].Decade + i * 10, 0, null)));
            var decades = all.Select(d => new Column(
                $"{d.Decade}s", d.AverageRating is double r ? $"★ {r:F1}" : null, new[] { d.Count }, d.Count.ToString("N0"),
                $"{d.Decade}s: {d.Count:N0} movies" + (d.AverageRating is double a ? $", average rating {a:F1}" : ""),
                d.Count > 0 ? () => Go(mw => mw.NavigateLibraryByDecade(d.Decade, $"{d.Decade}s")) : null)).ToList();
            section.Children.Add(Card("Release decades", "average rating under each", ColumnChart(decades, new[] { Series1 })));
        }

        var bands = s.RatingBands.Select(b => new Column(
            b.Label, null, new[] { b.Count }, b.Count.ToString("N0"), $"Rated {b.Label}: {b.Count:N0} movies",
            b.Count > 0 ? () => Go(mw => mw.NavigateLibraryByRatingBand(b.Key!, BandTitle(b.Key!))) : null)).ToList();
        var ratings = Card("Ratings", null, ColumnChart(bands, new[] { Series1 }, 120));
        var lengths = Card("Length", s.AverageLength > 0 ? $"average {Hm(s.AverageLength)}" : null, Rows(s.Lengths));
        section.Children.Add(Flow(2, ratings, lengths));

        var genres = Card("Genres", "share of your movies",
            Rows(s.Genres, g => Go(mw => mw.NavigateLibraryByGenre(g.Label)), total: here));
        var countries = Card("Countries", "share of your movies", Rows(s.Countries, total: here));
        section.Children.Add(Flow(2, genres, countries));

        var certs = Card("Certification", s.Certified > 0 ? $"known for {s.Certified:N0} of {here:N0}" : null, Rows(s.Certifications));
        var collections = Card("Collections",
            s.CollectionCount > 0 ? $"{s.CollectionCount:N0} collections, {s.MoviesInCollections:N0} movies" : null,
            Rows(s.Collections, c => Go(mw => mw.NavigateLibraryByCollection(int.Parse(c.Key!), c.Label)), percent: false, unit: "movies"));
        section.Children.Add(Flow(2, certs, collections));
        return section;
    }

    private FrameworkElement WatchingSection(DashboardStats s)
    {
        var section = Section("Your watching", s.Months.Count > 0 ? $"since {s.Months[0].Month:MMMM yyyy}" : null);
        var cards = new List<FrameworkElement>();
        if (s.Months.Count > 0)
        {
            var months = s.Months.Select((m, i) => new Column(
                m.Month.ToString("MMM"), i == 0 || m.Month.Month == 1 ? m.Month.ToString("yyyy") : null,
                new[] { m.Movies, m.Episodes }, (m.Movies + m.Episodes).ToString("N0"),
                $"{m.Month:MMMM yyyy}: {m.Movies:N0} movies, {m.Episodes:N0} episodes", null)).ToList();
            cards.Add(Card("Watched per month", null, Legend(("Movies", Series1), ("Episodes", Series2)),
                ColumnChart(months, new[] { Series1, Series2 })));
        }

        var rings = new Grid { ColumnSpacing = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        rings.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rings.Children.Add(Ring(s.Movies > 0 ? 100.0 * s.Watched / s.Movies : 0, "Movies", $"{s.Watched:N0} of {s.Movies:N0}", Series1));
        if (s.Episodes > 0)
        {
            rings.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var episodes = Ring(100.0 * s.EpisodesWatched / s.Episodes, "Episodes", $"{s.EpisodesWatched:N0} of {s.Episodes:N0}", Series2);
            Grid.SetColumn(episodes, 1);
            rings.Children.Add(episodes);
        }
        cards.Add(Card("Watched so far", s.Watchlist > 0 ? $"{s.Watchlist:N0} on your watchlist" : null, rings));
        section.Children.Add(Flow(2, cards.ToArray()));

        if (s.RecentlyWatched.Count > 0)
        {
            section.Children.Add(Text("Recently watched", 14, semi: true));
            section.Children.Add(CardRow(s.RecentlyWatched));
        }
        return section;
    }

    private FrameworkElement PeopleSection(DashboardStats s)
    {
        var section = Section("People", "the most movies in your library");
        if (s.Actors.Count > 0)
            section.Children.Add(Card("Actors", null, PeopleRow(s.Actors, n => Go(mw => mw.NavigateLibraryByActor(n)))));
        if (s.Directors.Count > 0)
            section.Children.Add(Card("Directors", null, PeopleRow(s.Directors, n => Go(mw => mw.NavigateLibraryByDirector(n)))));
        return section;
    }

    // ── Building blocks ──────────────────────────────────────────────────────

    private TextBlock Text(string text, double size, bool semi = false, bool dim = false, bool wrap = false) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = TextBrush,
        FontWeight = semi ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
        Opacity = dim ? 0.7 : 1,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private FrameworkElement Tile(string glyph, string value, string label, string? sub)
    {
        var tile = new Grid();
        // the tile's icon, big and faint behind it, running off the corner
        tile.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 116,
            FontFamily = _icons,
            Foreground = Series1,
            Opacity = Light ? 0.11 : 0.16,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -26, -34),
        });
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(Text(value, 28, semi: true));
        stack.Children.Add(Text(label, 13, semi: true));
        if (sub != null) stack.Children.Add(Text(sub, 12, dim: true, wrap: true));
        tile.Children.Add(stack);
        // the icon is cut at the tile's edge
        tile.SizeChanged += (_, e) => tile.Clip = new RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(-16, -16, e.NewSize.Width + 32, e.NewSize.Height + 32),
        };
        return new Border { Style = (Style)Application.Current.Resources["StatTileStyle"], Child = tile, MinHeight = 128 };
    }

    private StackPanel Section(string title, string? caption)
    {
        var section = new StackPanel { Spacing = 14 };
        var head = new Controls.WrapPanel { HorizontalSpacing = 12, VerticalSpacing = 2 };
        head.Children.Add(Text(title, 18, semi: true));
        if (caption != null)
        {
            var c = Text(caption, 13, dim: true);
            c.VerticalAlignment = VerticalAlignment.Bottom;
            c.Margin = new Thickness(0, 0, 0, 2);
            head.Children.Add(c);
        }
        section.Children.Add(head);
        return section;
    }

    private Border Card(string title, string? caption, params UIElement[] body)
    {
        var stack = new StackPanel { Spacing = 14 };
        var head = new Grid { ColumnSpacing = 12 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Text(title, 14, semi: true));
        if (caption != null)
        {
            var c = Text(caption, 12, dim: true);
            c.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(c, 1);
            head.Children.Add(c);
        }
        stack.Children.Add(head);
        foreach (var b in body) stack.Children.Add(b);
        return new Border { Style = (Style)Application.Current.Resources["StatPanelStyle"], Child = stack };
    }

    private Grid Flow(int wide, params FrameworkElement[] cards)
    {
        var grid = new Grid { ColumnSpacing = 14, RowSpacing = 14 };
        foreach (var c in cards) grid.Children.Add(c);
        _flows.Add((grid, wide));
        return grid;
    }

    /// <summary>A bar list: label, a bar against the biggest, and the count (and share).</summary>
    private FrameworkElement Rows(List<StatCount> items, Action<StatCount>? open = null, int? total = null,
        bool percent = true, string? unit = null, Func<StatCount, int, SolidColorBrush>? colour = null,
        Func<StatCount, bool>? openWhen = null)
    {
        var stack = new StackPanel { Spacing = 2 };
        if (items.Count == 0)
        {
            stack.Children.Add(Text("Nothing to show yet.", 13, dim: true));
            return stack;
        }
        var sum = total ?? items.Sum(i => i.Count);
        var max = Math.Max(1, items.Max(i => i.Count));
        for (int n = 0; n < items.Count; n++)
        {
            var item = items[n];
            var value = unit != null ? $"{item.Count:N0} {unit}" : $"{item.Count:N0}";
            if (percent && sum > 0) value += $"  ·  {Share(item.Count, sum)}";
            var rest = item.Label is "Unknown" or "Other";
            var brush = colour?.Invoke(item, n) ?? (rest ? GreyBrush : Series1);
            stack.Children.Add(BarRow(item.Label, (double)item.Count / max, value, brush,
                open == null || rest || openWhen?.Invoke(item) == false ? null : () => open(item)));
        }
        return stack;
    }

    private FrameworkElement BarRow(string label, double fraction, string value, SolidColorBrush brush, Action? open)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(Text(label, 13));
        var bar = Bar(fraction, brush);
        Grid.SetColumn(bar, 1);
        row.Children.Add(bar);
        var v = Text(value, 13, dim: true);
        v.MinWidth = 90;
        v.TextAlignment = TextAlignment.Right;
        Grid.SetColumn(v, 2);
        row.Children.Add(v);
        return Clickable(row, open, $"{label}: {value}", new Thickness(8, 5, 8, 5));
    }

    /// <summary>A row with its label and value on one line and the bar under them.</summary>
    private FrameworkElement TallRow(string label, double fraction, string value, SolidColorBrush brush, Action? open)
    {
        var top = new Grid { ColumnSpacing = 12 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(Text(label, 13, semi: true));
        var v = Text(value, 13, dim: true);
        Grid.SetColumn(v, 1);
        top.Children.Add(v);
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(top);
        stack.Children.Add(Bar(fraction, brush));
        return Clickable(stack, open, $"{label}: {value}", new Thickness(8, 6, 8, 6));
    }

    private static FrameworkElement Bar(double fraction, SolidColorBrush brush)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        var bar = new Grid { Height = 8, VerticalAlignment = VerticalAlignment.Center };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fraction, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - fraction, GridUnitType.Star) });
        var track = new Border { CornerRadius = new CornerRadius(4), Background = TrackBrush };
        Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track);
        if (fraction > 0) bar.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = brush, MinWidth = 4 });
        return bar;
    }

    /// <summary>A plain block, or a button that opens the movies it counts.</summary>
    private static FrameworkElement Clickable(FrameworkElement content, Action? open, string tip, Thickness padding)
    {
        if (open == null)
        {
            var plain = new Border { Padding = padding, Child = content, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            ToolTipService.SetToolTip(plain, tip);
            return plain;
        }
        var button = new Button
        {
            Content = content,
            Padding = padding,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        ToolTipService.SetToolTip(button, tip + ". Click to see them.");
        AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => open();
        return button;
    }

    private sealed record Column(string Label, string? Sub, int[] Parts, string Value, string Tip, Action? Open);

    /// <summary>Upright bars on one baseline, stacked when a column has parts (bottom part first).</summary>
    private FrameworkElement ColumnChart(IReadOnlyList<Column> columns, SolidColorBrush[] brushes, double height = 140)
    {
        var max = Math.Max(1, columns.Max(c => c.Parts.Sum()));
        var chart = new Grid();
        // In a narrow card every other column keeps its label, and counts read 1.6k
        var fitted = new List<(TextBlock Text, string Full, string Tight)>();
        for (int i = 0; i < columns.Count; i++)
        {
            chart.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var c = columns[i];
            var bar = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Bottom, MaxWidth = 44, Margin = new Thickness(3, 0, 3, 0) };
            var value = Text(c.Value, 12, dim: true);
            fitted.Add((value, c.Value, Brief(c.Parts.Sum())));
            value.HorizontalAlignment = HorizontalAlignment.Center;
            value.Margin = new Thickness(0, 0, 0, 2);
            bar.Children.Add(value);
            var top = true;
            for (int p = c.Parts.Length - 1; p >= 0; p--)
            {
                if (c.Parts[p] <= 0) continue;
                bar.Children.Add(new Border
                {
                    Height = Math.Max(3, (height - 22) * c.Parts[p] / max),
                    Background = brushes[p],
                    CornerRadius = top ? new CornerRadius(4, 4, 0, 0) : new CornerRadius(0),
                });
                top = false;
            }
            var area = new Grid { Height = height };
            area.Children.Add(bar);
            area.Children.Add(new Border { Height = 1, Background = TrackBrush, VerticalAlignment = VerticalAlignment.Bottom });
            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(area);
            var label = Text(c.Label, 12);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(label);
            fitted.Add((label, c.Label, i % 2 == 0 ? c.Label : ""));
            if (c.Sub != null)
            {
                var sub = Text(c.Sub, 11, dim: true);
                sub.HorizontalAlignment = HorizontalAlignment.Center;
                stack.Children.Add(sub);
                fitted.Add((sub, c.Sub, i % 2 == 0 ? c.Sub : ""));
            }
            var cell = Clickable(stack, c.Open, c.Tip, new Thickness(0, 4, 0, 4));
            Grid.SetColumn(cell, i);
            chart.Children.Add(cell);
        }
        chart.SizeChanged += (_, e) =>
        {
            var tight = e.NewSize.Width / columns.Count < 46;
            foreach (var (text, full, brief) in fitted)
            {
                text.Text = tight ? brief : full;
                // a kept label may use the empty neighbours' room
                text.Margin = new Thickness(tight ? -20 : 0, text.Margin.Top, tight ? -20 : 0, text.Margin.Bottom);
                text.TextTrimming = tight ? TextTrimming.None : TextTrimming.CharacterEllipsis;
            }
        };
        return chart;
    }

    private FrameworkElement Legend(params (string Label, SolidColorBrush Brush)[] series)
    {
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        foreach (var (label, brush) in series)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            item.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), Background = brush, VerticalAlignment = VerticalAlignment.Center });
            item.Children.Add(Text(label, 12));
            legend.Children.Add(item);
        }
        return legend;
    }

    private FrameworkElement Ring(double percent, string title, string sub, SolidColorBrush brush)
    {
        var ring = new Grid { Width = 104, Height = 104, HorizontalAlignment = HorizontalAlignment.Center };
        ring.Children.Add(new ProgressRing
        {
            IsIndeterminate = false,
            Minimum = 0,
            Maximum = 100,
            Value = percent,
            Width = 104,
            Height = 104,
            Foreground = brush,
            Background = TrackBrush,
        });
        var mid = Text(percent > 0 && percent < 1 ? "<1%" : $"{percent:F0}%", 20, semi: true);
        mid.HorizontalAlignment = HorizontalAlignment.Center;
        mid.VerticalAlignment = VerticalAlignment.Center;
        ring.Children.Add(mid);
        var stack = new StackPanel { Spacing = 4, Margin = new Thickness(12, 0, 12, 0) };
        stack.Children.Add(ring);
        var t = Text(title, 13, semi: true);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(t);
        var s = Text(sub, 12, dim: true);
        s.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(s);
        return stack;
    }

    private Grid PeopleRow(List<PersonCount> people, Action<string> open)
    {
        var row = new Grid { ColumnSpacing = 8 };
        foreach (var p in people) row.Children.Add(Person(p, open));
        _peopleRows.Add(row);
        return row;
    }

    private FrameworkElement Person(PersonCount p, Action<string> open)
    {
        var photo = new Grid { Width = 72, Height = 72, HorizontalAlignment = HorizontalAlignment.Center };
        var initials = Text(Initials(p.Name), 20, semi: true, dim: true);
        initials.HorizontalAlignment = HorizontalAlignment.Center;
        initials.VerticalAlignment = VerticalAlignment.Center;
        photo.Children.Add(new Border { CornerRadius = new CornerRadius(36), Background = ThemeBrushes.Get("PlaceholderBgBrush"), Child = initials });
        var image = new Image { Stretch = Stretch.UniformToFill };
        photo.Children.Add(new Border { CornerRadius = new CornerRadius(36), Child = image });
        LoadPhoto(image, p.Thumb);
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(photo);
        var name = Text(p.Name, 13, semi: true, wrap: true);
        name.TextAlignment = TextAlignment.Center;
        name.MaxLines = 2;
        name.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(name);
        foreach (var line in new[] { $"{p.Movies:N0} movies", $"{p.Watched:N0} watched" })
        {
            var count = Text(line, 12, dim: true);
            count.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(count);
        }
        return Clickable(stack, () => open(p.Name),
            $"{p.Name}: {p.Movies:N0} movies in your library, {p.Watched:N0} watched", new Thickness(4, 8, 4, 8));
    }

    /// <summary>A person's photo: a web address, a file, or one in the data folder.</summary>
    private static void LoadPhoto(Image image, string? thumb)
    {
        if (string.IsNullOrWhiteSpace(thumb)) return;
        try
        {
            var raw = thumb.Trim();
            Uri? uri = raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? new Uri(raw)
                : File.Exists(raw) ? new Uri(raw)
                : AppState.Instance.Db.GetCachedImagePath(raw) is { } cached && File.Exists(cached) ? new Uri(cached)
                : null;
            if (uri == null) return;
            var bitmap = new BitmapImage { DecodePixelWidth = 144 };
            bitmap.UriSource = uri;
            image.Source = bitmap;
        }
        catch { }
    }

    private FrameworkElement CardRow(List<MovieListItem> movies)
    {
        var layout = new UniformGridLayout();
        Controls.CardCell.FitGrid(layout, (MovieCardControl.GlobalCardWidth, MovieCardControl.GlobalCardHeight),
                                  MovieCardControl.GlobalDensity);
        var repeater = new ItemsRepeater
        {
            Layout = layout,
            ItemTemplate = (DataTemplate)Resources["MovieCardTemplate"],
            TabFocusNavigation = Microsoft.UI.Xaml.Input.KeyboardNavigationMode.Once,
        };
        _cardRows.Add((repeater, movies));
        repeater.SizeChanged += (_, e) => { if (e.PreviousSize.Width != e.NewSize.Width) Reflow(); };
        return repeater;
    }

    // ── Width ────────────────────────────────────────────────────────────────

    private void Reflow()
    {
        var width = double.IsNaN(PageStack.Width) ? PageStack.ActualWidth : PageStack.Width;
        if (width <= 0) return;
        foreach (var (grid, wide) in _flows)
            Arrange(grid, wide == 5 ? (width >= 900 ? 5 : width >= 560 ? 3 : 2) : width >= 720 ? wide : 1);

        // a full-width card's inside: less its padding and border
        foreach (var row in _peopleRows)
        {
            var fit = Math.Clamp((int)((width - 38 + 8) / (112 + 8)), 1, row.Children.Count);
            if (row.ColumnDefinitions.Count == fit) continue;
            row.ColumnDefinitions.Clear();
            for (int i = 0; i < fit; i++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < row.Children.Count; i++)
            {
                var person = (FrameworkElement)row.Children[i];
                person.Visibility = i < fit ? Visibility.Visible : Visibility.Collapsed;
                Grid.SetColumn(person, Math.Min(i, fit - 1));
            }
        }

        // one row of movie cards, as many as the grid puts in a row
        foreach (var (repeater, movies) in _cardRows)
        {
            if (repeater.Layout is not UniformGridLayout layout) continue;
            var across = repeater.ActualWidth > 0 ? repeater.ActualWidth : width;
            var cards = Math.Max(1, (int)(across / (layout.MinItemWidth + layout.MinColumnSpacing)));   // a gap after each, as the grid counts
            if (repeater.Tag is int shown && shown == cards) continue;
            repeater.Tag = cards;
            repeater.ItemsSource = movies.Take(cards).ToList();
        }
    }

    private static void Arrange(Grid grid, int columns)
    {
        if (grid.ColumnDefinitions.Count == columns) return;
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        for (int c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rows = (grid.Children.Count + columns - 1) / columns;
        for (int r = 0; r < rows; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < grid.Children.Count; i++)
        {
            var child = (FrameworkElement)grid.Children[i];
            Grid.SetColumn(child, i % columns);
            Grid.SetRow(child, i / columns);
        }
    }

    // ── Text ─────────────────────────────────────────────────────────────────

    private static void Go(Action<MainWindow> navigate)
    {
        if (App.MainWindow is MainWindow mw) navigate(mw);
    }

    // the Browse page's names for its rating bands, so the library's title matches
    private static string BandTitle(string key) => key switch
    {
        "9" => "9+ Stars",
        "8" => "8–9 Stars",
        "7" => "7–8 Stars",
        "6" => "6–7 Stars",
        "5" => "5–6 Stars",
        _ => "Under 5",
    };

    private static string Brief(int n) => n >= 10_000 ? $"{n / 1000}k" : n >= 1000 ? $"{n / 1000.0:0.#}k" : n.ToString();

    private static string Share(int count, int total)
    {
        var p = 100.0 * count / total;
        return p > 0 && p < 1 ? "<1%" : $"{p:F0}%";
    }

    private static string Size(long bytes) =>
        bytes >= 1L << 40 ? $"{bytes / (double)(1L << 40):F1} TB"
        : bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):F0} GB"
        : $"{bytes / (double)(1L << 20):F0} MB";

    private static string Hm(int minutes) => minutes >= 60 ? $"{minutes / 60}h {minutes % 60:D2}m" : $"{minutes}m";

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "?" : parts.Length == 1 ? parts[0][..1].ToUpperInvariant()
            : (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
    }
}
