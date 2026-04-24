using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using CineLibraryCS.Models;
using CineLibraryCS.Services;

namespace CineLibraryCS.Views;

public sealed partial class StatisticsPage : Page
{
    public StatisticsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    public void Refresh()
    {
        var db = AppState.Instance.Db;

        // Summary tiles
        var stats = db.GetStats();
        TileTotalMovies.Text   = stats.TotalMovies.ToString("N0");
        TileTotalRuntime.Text  = FormatRuntime(stats.TotalRuntime);
        TileAvgRating.Text     = stats.AvgRating.HasValue ? $"★ {stats.AvgRating:F1}" : "—";
        TileTotalDrives.Text   = stats.TotalDrives.ToString();

        if (stats.TotalMissing > 0)
        {
            MissingHint.Text = $"⚠ {stats.TotalMissing} movie{(stats.TotalMissing == 1 ? "" : "s")} marked missing. Clean up in the Drives page.";
            MissingHint.Visibility = Visibility.Visible;
        }
        else
        {
            MissingHint.Visibility = Visibility.Collapsed;
        }

        // Watch progress
        var (watched, total, percent) = db.GetWatchProgress();
        WatchProgressBar.Value = percent;
        WatchProgressText.Text = $"{watched:N0} / {total:N0} ({percent:F0}%)";

        var watchlist = db.GetWatchlistCount();
        WatchlistCountText.Text = watchlist > 0
            ? $"📌 {watchlist} on your watchlist"
            : "Tip: add movies to your watchlist from the movie detail dialog.";

        // Decades — simple horizontal bars
        var decades = db.GetMoviesByDecade();
        DecadesPanel.Children.Clear();
        if (decades.Count == 0)
        {
            DecadesEmpty.Visibility = Visibility.Visible;
        }
        else
        {
            DecadesEmpty.Visibility = Visibility.Collapsed;
            int max = 1;
            foreach (var d in decades) if (d.count > max) max = d.count;
            foreach (var d in decades)
            {
                DecadesPanel.Children.Add(BuildBarRow(
                    label: $"{d.decade}s",
                    count: d.count,
                    barFraction: (double)d.count / max,
                    hint: d.avgRating > 0 ? $"★ {d.avgRating:F1}" : null));
            }
        }

        // Top genres (reuse sidebar genres)
        var topGenres = db.GetTopGenres(10);
        GenresPanel.Children.Clear();
        if (topGenres.Count == 0)
        {
            GenresEmpty.Visibility = Visibility.Visible;
        }
        else
        {
            GenresEmpty.Visibility = Visibility.Collapsed;
            int max = 1;
            foreach (var g in topGenres) if (g.Count > max) max = g.Count;
            foreach (var g in topGenres)
                GenresPanel.Children.Add(BuildBarRow(g.Name, g.Count, (double)g.Count / max, null));
        }

        // Top directors
        var dirs = db.GetTopDirectors(10);
        DirectorsPanel.Children.Clear();
        if (dirs.Count == 0)
        {
            DirectorsEmpty.Visibility = Visibility.Visible;
        }
        else
        {
            DirectorsEmpty.Visibility = Visibility.Collapsed;
            int max = 1;
            foreach (var d in dirs) if (d.Count > max) max = d.Count;
            foreach (var d in dirs)
                DirectorsPanel.Children.Add(BuildBarRow(d.Name, d.Count, (double)d.Count / max, null));
        }

        // Top actors
        var actors = db.GetTopActors(10);
        ActorsPanel.Children.Clear();
        if (actors.Count == 0)
        {
            ActorsEmpty.Visibility = Visibility.Visible;
        }
        else
        {
            ActorsEmpty.Visibility = Visibility.Collapsed;
            int max = 1;
            foreach (var a in actors) if (a.Count > max) max = a.Count;
            foreach (var a in actors)
                ActorsPanel.Children.Add(BuildBarRow(a.Name, a.Count, (double)a.Count / max, null));
        }
    }

    private static string FormatRuntime(long minutes)
    {
        if (minutes <= 0) return "—";
        var hours = minutes / 60;
        if (hours < 24) return $"{hours}h";
        var days = hours / 24;
        return $"{days}d {hours % 24}h";
    }

    // Build a row: [label on left] [bar that fills to fraction] [count on right]
    private Grid BuildBarRow(string label, int count, double barFraction, string? hint)
    {
        if (barFraction < 0) barFraction = 0;
        if (barFraction > 1) barFraction = 1;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelTb = new TextBlock
        {
            Text = label,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        labelTb.SetValue(Grid.ColumnProperty, 0);
        labelTb.SetValue(ToolTipService.ToolTipProperty, label);
        grid.Children.Add(labelTb);

        // Bar track
        var track = new Border
        {
            Height = 10,
            CornerRadius = new CornerRadius(5),
            Background = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(8, 0, 8, 0),
        };

        var barHost = new Grid();
        barHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(barFraction, GridUnitType.Star) });
        barHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - barFraction, GridUnitType.Star) });

        var filled = new Border
        {
            Height = 10,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xA7, 0x8B, 0xFA)),
        };
        filled.SetValue(Grid.ColumnProperty, 0);
        barHost.Children.Add(filled);

        track.Child = barHost;
        track.SetValue(Grid.ColumnProperty, 1);
        grid.Children.Add(track);

        var countTb = new TextBlock
        {
            Text = hint != null ? $"{count}  ·  {hint}" : count.ToString("N0"),
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 36,
            TextAlignment = TextAlignment.Right,
        };
        countTb.SetValue(Grid.ColumnProperty, 2);
        grid.Children.Add(countTb);

        return grid;
    }
}
