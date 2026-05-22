using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using CineLibraryCS.Models;
using CineLibraryCS.Services;
using System.Runtime.InteropServices.WindowsRuntime;

namespace CineLibraryCS.Views;

public sealed partial class TvShowCard : UserControl
{
    public static readonly DependencyProperty ShowProperty =
        DependencyProperty.Register(nameof(Show), typeof(TvShowListItem), typeof(TvShowCard),
            new PropertyMetadata(null, OnShowChanged));

    public TvShowListItem? Show
    {
        get => (TvShowListItem?)GetValue(ShowProperty);
        set => SetValue(ShowProperty, value);
    }

    public TvShowCard()
    {
        InitializeComponent();
        PointerEntered += (_, _) => { CardLift.Y = -4; CardBorder.Translation = new System.Numerics.Vector3(0, 0, 24); };
        PointerExited  += (_, _) => { CardLift.Y = 0;  CardBorder.Translation = new System.Numerics.Vector3(0, 0, 0); };
    }

    private static void OnShowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TvShowCard c && e.NewValue is TvShowListItem s) c.Populate(s);
    }

    private void Populate(TvShowListItem s)
    {
        TitleText.Text = s.Title;
        MetaText.Text = s.YearText;
        ProgressText.Text = s.ProgressText;
        FavBadge.Visibility = s.IsFavorite ? Visibility.Visible : Visibility.Collapsed;

        if (s.IsMissing) { StatusText.Text = "MISSING"; StatusBadge.Visibility = Visibility.Visible; }
        else if (!s.IsOnline) { StatusText.Text = "OFFLINE"; StatusBadge.Visibility = Visibility.Visible; }
        else StatusBadge.Visibility = Visibility.Collapsed;

        // Progress bar fill — set once we know the card width (170 - padding).
        ProgressFill.Width = Math.Max(0, 150 * s.ProgressFraction);

        LoadPosterAsync(s.LocalPoster);
    }

    private int _token;
    private async void LoadPosterAsync(string? relPath)
    {
        var my = ++_token;
        PosterImage.Source = null;
        PosterPlaceholder.Visibility = Visibility.Visible;
        if (relPath == null) return;
        var full = AppState.Instance.Db.GetCachedImagePath(relPath);
        if (full == null) return;
        try
        {
            var bytes = await Task.Run(() => ImageCache.GetOrLoad(relPath, full));
            if (my != _token || bytes == null) return;
            var bmp = new BitmapImage { DecodePixelWidth = 340 };
            using var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await ms.WriteAsync(bytes.AsBuffer());
            if (my != _token) return;
            ms.Seek(0);
            await bmp.SetSourceAsync(ms);
            if (my != _token) return;
            PosterImage.Source = bmp;
            PosterPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch { }
    }
}
