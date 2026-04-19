using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using CineLibraryCS.Models;
using CineLibraryCS.Services;
using System.Runtime.InteropServices.WindowsRuntime;

namespace CineLibraryCS.Views;

public sealed partial class MovieRowControl : UserControl
{
    public static readonly DependencyProperty MovieProperty =
        DependencyProperty.Register(nameof(Movie), typeof(MovieListItem), typeof(MovieRowControl),
            new PropertyMetadata(null, OnMovieChanged));

    public MovieListItem? Movie
    {
        get => (MovieListItem?)GetValue(MovieProperty);
        set => SetValue(MovieProperty, value);
    }

    public MovieRowControl()
    {
        InitializeComponent();
    }

    private static void OnMovieChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MovieRowControl c && e.NewValue is MovieListItem m)
            c.Populate(m);
    }

    private void Populate(MovieListItem m)
    {
        RowTitle.Text = m.Title;
        RowMeta.Text = $"{m.Year?.ToString() ?? "—"}{(m.Runtime.HasValue ? $" · {m.Runtime}m" : "")}{(m.GenresCsv != null ? $" · {m.GenresCsv.Split(',')[0].Trim()}" : "")}";
        RowFav.Visibility = m.IsFavorite ? Visibility.Visible : Visibility.Collapsed;
        RowRating.Text = m.RatingText;

        DriveLabel.Text = m.DriveLabel ?? "";
        DriveDot.Fill = new SolidColorBrush(m.IsOnline
            ? Windows.UI.Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E)
            : Windows.UI.Color.FromArgb(0xFF, 0x6B, 0x72, 0x80));

        // Status badge
        if (m.IsMissing)
        {
            RowStatusBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0xEF, 0x44, 0x44));
            RowStatusText.Text = "MISSING";
            RowStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xEF, 0x44, 0x44));
        }
        else if (m.IsOnline)
        {
            RowStatusBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0x22, 0xC5, 0x5E));
            RowStatusText.Text = "ONLINE";
            RowStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E));
        }
        else
        {
            RowStatusBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0x6B, 0x72, 0x80));
            RowStatusText.Text = "OFFLINE";
            RowStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x6B, 0x72, 0x80));
        }

        WatchedBtn.Content = m.IsWatched ? "✓" : "○";
        WatchedBtn.Foreground = m.IsWatched
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E))
            : new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x90, 0x90, 0xA0));
        FavBtn.Content = m.IsFavorite ? "★" : "☆";
        FavBtn.Foreground = m.IsFavorite
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xF5, 0x9E, 0x0B))
            : new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x90, 0x90, 0xA0));

        LoadThumbAsync(m.LocalPoster);
    }

    // See MovieCardControl.LoadPosterAsync — guards against recycled rows
    // receiving a stale late-completing image.
    private int _thumbLoadToken;

    private async void LoadThumbAsync(string? relPath)
    {
        var myToken = ++_thumbLoadToken;

        ThumbImage.Source = null;
        ThumbPlaceholder.Visibility = Visibility.Visible;
        if (relPath == null) return;

        var fullPath = AppState.Instance.Db.GetCachedImagePath(relPath);
        if (fullPath == null) return;

        try
        {
            var bytes = await Task.Run(() => File.ReadAllBytes(fullPath));
            if (myToken != _thumbLoadToken) return;

            var bmp = new BitmapImage { DecodePixelWidth = 44 };
            var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await ms.WriteAsync(bytes.AsBuffer());
            if (myToken != _thumbLoadToken) return;
            ms.Seek(0);
            await bmp.SetSourceAsync(ms);
            if (myToken != _thumbLoadToken) return;

            ThumbImage.Source = bmp;
            ThumbPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch { }
    }

    private void OnTapped(object sender, TappedRoutedEventArgs e) => OpenDetail();

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
        => RowBorder.Background = new SolidColorBrush(ActualTheme == ElementTheme.Light
            ? Windows.UI.Color.FromArgb(0xFF, 0xEB, 0xEB, 0xFF)
            : Windows.UI.Color.FromArgb(0xFF, 0x1E, 0x1E, 0x2E));

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
        => RowBorder.Background = new SolidColorBrush(ActualTheme == ElementTheme.Light
            ? Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
            : Windows.UI.Color.FromArgb(0xFF, 0x14, 0x14, 0x1F));

    private void OnToggleWatched(object sender, RoutedEventArgs e)
    {
        if (Movie == null) return;
        AppState.Instance.Db.ToggleWatched(Movie.Id);
        Movie.IsWatched = !Movie.IsWatched;
        WatchedBtn.Content = Movie.IsWatched ? "✓" : "○";
        WatchedBtn.Foreground = Movie.IsWatched
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E))
            : new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x90, 0x90, 0xA0));
    }

    private void OnToggleFav(object sender, RoutedEventArgs e)
    {
        if (Movie == null) return;
        AppState.Instance.Db.ToggleFavorite(Movie.Id);
        Movie.IsFavorite = !Movie.IsFavorite;
        FavBtn.Content = Movie.IsFavorite ? "★" : "☆";
        FavBtn.Foreground = Movie.IsFavorite
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xF5, 0x9E, 0x0B))
            : new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x90, 0x90, 0xA0));
    }

    private void OpenDetail()
    {
        if (Movie == null) return;
        var win = new MovieDetailDialog(Movie.Id);
        win.Activate();
    }
}
