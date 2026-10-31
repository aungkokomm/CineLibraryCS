using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using CineLibraryCS.Models;
using CineLibraryCS.Services;
using System.Runtime.InteropServices.WindowsRuntime;

namespace CineLibraryCS.Views;

/// <summary>
/// v4.7.0 (#25): a TV show as a list row, for the list view of the pages that
/// mix shows with movies, so the whole page is rows (shows had only cards).
/// </summary>
public sealed partial class TvShowRowControl : UserControl
{
    public static readonly DependencyProperty ShowProperty =
        DependencyProperty.Register(nameof(Show), typeof(TvShowListItem), typeof(TvShowRowControl),
            new PropertyMetadata(null, OnShowChanged));

    public TvShowListItem? Show
    {
        get => (TvShowListItem?)GetValue(ShowProperty);
        set => SetValue(ShowProperty, value);
    }

    public TvShowRowControl()
    {
        InitializeComponent();
    }

    private static void OnShowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TvShowRowControl c && e.NewValue is TvShowListItem s) c.Populate(s);
    }

    private void Populate(TvShowListItem s)
    {
        RowTitle.Text = s.Title;
        // Continue Watching: which episode is next, as the card says
        var episodes = s.EpisodeCount == 1 ? "1 episode" : $"{s.EpisodeCount} episodes";
        RowMeta.Text = s.NextEpisode != null
            ? s.NextEpisodeLeft != null ? $"{s.YearText} · {s.NextEpisode} · {s.NextEpisodeLeft}" : $"{s.YearText} · Next: {s.NextEpisode}"
            : $"{s.YearText} · {episodes}{(s.GenresCsv != null ? $" · {s.GenresCsv.Split(',')[0].Trim()}" : "")}";
        RowFav.Visibility = s.IsFavorite ? Visibility.Visible : Visibility.Collapsed;
        RowRating.Text = s.RatingText;

        DriveLabel.Text = s.DriveLabel ?? "";
        DriveDot.Fill = new SolidColorBrush(s.IsOnline
            ? Windows.UI.Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E)
            : Windows.UI.Color.FromArgb(0xFF, 0x6B, 0x72, 0x80));

        // Status badge, as on a movie row
        var (text, color) = s.IsMissing ? ("MISSING", Windows.UI.Color.FromArgb(0xFF, 0xEF, 0x44, 0x44))
                          : s.IsOnline  ? ("ONLINE",  Windows.UI.Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E))
                          :               ("OFFLINE", Windows.UI.Color.FromArgb(0xFF, 0x6B, 0x72, 0x80));
        RowStatusText.Text = text;
        RowStatusText.Foreground = new SolidColorBrush(color);
        RowStatusBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, color.R, color.G, color.B));

        RowProgress.Text = s.ProgressText;
        RowProgress.Foreground = s.FullyWatched
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E))
            : ThemeBrushes.Get("BrandPurpleBrush");

        LoadThumbAsync(s.LocalPoster);
    }

    // See MovieRowControl.LoadThumbAsync: a recycled row mustn't get a late image.
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
            var bytes = await Task.Run(() => ImageCache.GetOrLoad(relPath!, fullPath));
            if (myToken != _thumbLoadToken || bytes == null) return;

            var bmp = new BitmapImage { DecodePixelWidth = 112 };
            using var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
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

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
        => RowBorder.Background = new SolidColorBrush(ActualTheme == ElementTheme.Light
            ? Windows.UI.Color.FromArgb(0xFF, 0xEB, 0xEB, 0xFF)
            : Windows.UI.Color.FromArgb(0xFF, 0x1E, 0x1E, 0x2E));

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
        => RowBorder.ClearValue(Border.BackgroundProperty);
}
