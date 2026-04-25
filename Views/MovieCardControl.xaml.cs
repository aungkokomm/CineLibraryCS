using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using CineLibraryCS.Models;
using CineLibraryCS.Services;
using System.Runtime.InteropServices.WindowsRuntime;

namespace CineLibraryCS.Views;

public sealed partial class MovieCardControl : UserControl
{
    public static readonly DependencyProperty MovieProperty =
        DependencyProperty.Register(nameof(Movie), typeof(MovieListItem), typeof(MovieCardControl),
            new PropertyMetadata(null, OnMovieChanged));

    public MovieListItem? Movie
    {
        get => (MovieListItem?)GetValue(MovieProperty);
        set => SetValue(MovieProperty, value);
    }

    public event EventHandler? SidebarRefreshRequested;
    public event EventHandler<MovieListItem>? WatchedToggleRequested;

    // ── Global card size (all cards resize together when density changes) ──

    public static double GlobalCardWidth { get; private set; } = 150;
    public static double GlobalCardHeight { get; private set; } = 280;
    public static event EventHandler? GlobalSizeChanged;

    public static void SetGlobalSize(double w, double h)
    {
        GlobalCardWidth = w;
        GlobalCardHeight = h;
        GlobalSizeChanged?.Invoke(null, EventArgs.Empty);
    }

    public MovieCardControl()
    {
        InitializeComponent();
        ApplySize();
        GlobalSizeChanged += OnGlobalSizeChanged;
        Unloaded += (_, _) => GlobalSizeChanged -= OnGlobalSizeChanged;

        PointerEntered += (_, _) =>
        {
            HoverOverlay.Visibility = Visibility.Visible;
            HoverOverlay.Opacity = 1;
            CardLift.Y = -3;
        };
        PointerExited += (_, _) =>
        {
            HoverOverlay.Opacity = 0;
            HoverOverlay.Visibility = Visibility.Collapsed;
            CardLift.Y = 0;
        };
        Tapped += (_, e) =>
        {
            // Don't re-open if the tap originated inside the "View Details" button
            // (its Click already opens the dialog — without this guard we'd get two windows).
            // NOTE: we must walk the VISUAL tree (VisualTreeHelper.GetParent), not the logical
            // tree (.Parent). The TextBlock inside Button's ContentPresenter has Parent = null
            // across the template boundary, so .Parent-walking missed the Button.
            if (e.OriginalSource is DependencyObject src)
            {
                DependencyObject? cur = src;
                while (cur != null)
                {
                    if (cur is Button) { e.Handled = true; return; }
                    cur = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(cur);
                }
            }
            OpenDetail();
        };
    }

    private void OnGlobalSizeChanged(object? s, EventArgs e) => ApplySize();

    private void ApplySize()
    {
        CardRoot.Width = GlobalCardWidth;
        CardRoot.Height = GlobalCardHeight;
    }

    private static void OnMovieChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MovieCardControl c && e.NewValue is MovieListItem m)
            c.Populate(m);
    }

    private void Populate(MovieListItem m)
    {
        TitleText.Text = m.Title;
        MetaText.Text = $"{m.Year?.ToString() ?? "—"}{(m.Runtime.HasValue ? $" · {m.Runtime}m" : "")}";
        PlaceholderTitle.Text = m.Title;

        // Status badge — compact dot + label
        if (m.IsMissing)
        {
            StatusDot.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xEF, 0x44, 0x44));
            StatusText.Text = "MISSING";
            StatusBadge.Visibility = Visibility.Visible;
        }
        else if (!m.IsOnline)
        {
            StatusDot.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x9C, 0xA3, 0xAF));
            StatusText.Text = "OFFLINE";
            StatusBadge.Visibility = Visibility.Visible;
        }
        else
        {
            StatusBadge.Visibility = Visibility.Collapsed;
        }

        FavBadge.Visibility = m.IsFavorite ? Visibility.Visible : Visibility.Collapsed;
        WatchedBadge.Visibility = m.IsWatched ? Visibility.Visible : Visibility.Collapsed;
        WatchedToggleBtn.Content = m.IsWatched ? "✓ Watched" : "○ Mark Watched";

        if (m.Rating.HasValue)
        {
            RatingText.Text = $"★ {m.Rating:F1}";
            RatingBadge.Visibility = Visibility.Visible;
            RatingInline.Text = $"★ {m.Rating:F1}";
            RatingInline.Visibility = Visibility.Visible;
        }
        else
        {
            RatingBadge.Visibility = Visibility.Collapsed;
            RatingInline.Visibility = Visibility.Collapsed;
        }

        GenresText.Text = string.Join(" · ",
            (m.GenresCsv ?? "").Split(',').Select(g => g.Trim()).Where(g => g.Length > 0).Take(3));

        LoadPosterAsync(m.LocalPoster);
    }

    private int _posterLoadToken;

    private async void LoadPosterAsync(string? relPath)
    {
        var myToken = ++_posterLoadToken;

        PosterPlaceholder.Visibility = Visibility.Visible;
        PosterImage.Source = null;

        if (relPath == null) return;

        var fullPath = AppState.Instance.Db.GetCachedImagePath(relPath);
        if (fullPath == null) return;

        try
        {
            var bytes = await Task.Run(() => ImageCache.GetOrLoad(relPath!, fullPath));
            if (myToken != _posterLoadToken) return;
            if (bytes == null)
            {
                PosterPlaceholder.Visibility = Visibility.Visible;
                return;
            }

            var bmp = new BitmapImage { DecodePixelWidth = (int)Math.Max(120, GlobalCardWidth) };
            var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await ms.WriteAsync(bytes.AsBuffer());
            if (myToken != _posterLoadToken) return;
            ms.Seek(0);
            await bmp.SetSourceAsync(ms);
            if (myToken != _posterLoadToken) return;

            PosterImage.Source = bmp;
            PosterPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch
        {
            if (myToken == _posterLoadToken)
                PosterPlaceholder.Visibility = Visibility.Visible;
        }
    }

    private void OnViewDetails(object sender, RoutedEventArgs e) => OpenDetail();

    private void OnWatchedToggle(object sender, RoutedEventArgs e)
    {
        if (Movie == null) return;
        WatchedToggleRequested?.Invoke(this, Movie);
        // Movie.IsWatched already flipped by VM handler — refresh visuals
        WatchedBadge.Visibility = Movie.IsWatched ? Visibility.Visible : Visibility.Collapsed;
        WatchedToggleBtn.Content = Movie.IsWatched ? "✓ Watched" : "○ Mark Watched";
    }

    private void OpenDetail()
    {
        if (Movie == null) return;
        var win = new MovieDetailDialog(Movie.Id);
        win.WatchlistChanged += (s, e) => SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
        win.Activate();
    }
}
