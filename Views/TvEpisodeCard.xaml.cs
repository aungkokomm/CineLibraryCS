using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using CineLibraryCS.Models;
using CineLibraryCS.Services;
using System.Runtime.InteropServices.WindowsRuntime;

namespace CineLibraryCS.Views;

public sealed partial class TvEpisodeCard : UserControl
{
    public static readonly DependencyProperty EpisodeProperty =
        DependencyProperty.Register(nameof(Episode), typeof(TvEpisodeItem), typeof(TvEpisodeCard),
            new PropertyMetadata(null, OnEpisodeChanged));

    public TvEpisodeItem? Episode
    {
        get => (TvEpisodeItem?)GetValue(EpisodeProperty);
        set => SetValue(EpisodeProperty, value);
    }

    // Static so the host page wires once, regardless of recycling.
    public static event Action<TvEpisodeItem>? AnyPlay;
    public static event Action<TvEpisodeItem>? AnyWatchedToggle;
    public static event Action<TvEpisodeItem>? AnyFavoriteToggle;   // v4.7.0 (#25)
    public static event Action<TvEpisodeItem>? AnyDetails;

    public TvEpisodeCard()
    {
        InitializeComponent();
        CardRoot.SizeChanged += (_, e) => ApplyWidth(e.NewSize.Width);
        // v4.6.0: a card for an episode you don't have can't be played or opened
        PointerEntered += (_, _) =>
        {
            if (Episode?.NotInLibrary == true) return;
            HoverOverlay.Visibility = Visibility.Visible; HoverOverlay.Opacity = 1;
            CardBorder.Background = ThemeBrushes.Get("CardHoverBrush");   // v4.8.0: the row lights up
        };
        PointerExited  += (_, _) =>
        {
            HoverOverlay.Opacity = 0; HoverOverlay.Visibility = Visibility.Collapsed;
            CardBorder.ClearValue(Border.BackgroundProperty);   // back to CardSurfaceStyle's theme colour
        };
        DoubleTapped   += (_, _) => { if (Episode is { NotInLibrary: false }) AnyPlay?.Invoke(Episode); };
        // Single tap on the card body (not its buttons) → episode details.
        Tapped += (_, e) =>
        {
            if (Episode == null || Episode.NotInLibrary) return;
            if (TapOnButton(e.OriginalSource as DependencyObject)) return;
            AnyDetails?.Invoke(Episode);
        };
    }

    // v4.8.0: a narrower list (a small window, the sidebar open) gets a smaller
    // picture, no number and the two buttons one above the other, so the title
    // and the plot keep their room.
    private int _widthTier = -1;
    private void ApplyWidth(double width)
    {
        var tier = width >= 760 ? 2 : width >= 520 ? 1 : 0;
        if (tier == _widthTier) return;
        _widthTier = tier;
        NumberText.Visibility = tier == 2 ? Visibility.Visible : Visibility.Collapsed;
        ThumbFrame.Width = tier == 2 ? 256 : tier == 1 ? 192 : 128;
        ThumbFrame.Height = ThumbFrame.Width * 9 / 16;
        ToggleButtons.Orientation = tier == 2 ? Orientation.Horizontal : Orientation.Vertical;
        PlotText.MaxLines = tier == 2 ? 3 : 2;
        CardRoot.Height = tier == 2 ? 170 : 134;
    }

    private static bool TapOnButton(DependencyObject? src)
    {
        var cur = src;
        while (cur != null)
        {
            if (cur is Button) return true;
            cur = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(cur);
        }
        return false;
    }

    private static void OnEpisodeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TvEpisodeCard c) return;
        if (e.OldValue is TvEpisodeItem prev) prev.PropertyChanged -= c.OnEpPropChanged;
        if (e.NewValue is TvEpisodeItem ep) { c.Populate(ep); ep.PropertyChanged += c.OnEpPropChanged; }
    }

    private void OnEpPropChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (Episode == null) return;
        if (e.PropertyName == nameof(TvEpisodeItem.IsWatched))
            ApplyWatchedVisual(Episode.IsWatched);
        else if (e.PropertyName == nameof(TvEpisodeItem.IsFavorite))
            ApplyFavoriteVisual(Episode.IsFavorite);
    }

    private void Populate(TvEpisodeItem ep)
    {
        NumberText.Text = ep.Episode.ToString();
        TitleText.Text = ep.Title;
        PlotText.Text = ep.Plot ?? "";
        PlotText.Visibility = string.IsNullOrWhiteSpace(ep.Plot) ? Visibility.Collapsed : Visibility.Visible;
        // v4.6.0: an aired episode you don't have: greyed, with its air date.
        // Cards are reused while scrolling, so every branch sets everything.
        CardRoot.Opacity = ep.NotInLibrary ? 0.55 : 1;
        NotInLibraryBadge.Visibility = ep.NotInLibrary ? Visibility.Visible : Visibility.Collapsed;
        ToggleButtons.Visibility = ep.NotInLibrary ? Visibility.Collapsed : Visibility.Visible;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this,
            ep.NotInLibrary ? $"{ep.Code} {ep.Title}, not in your library" : $"{ep.Code} {ep.Title}");
        if (ep.NotInLibrary)
        {
            MetaText.Text = AiredText(ep.Aired);
            ApplyWatchedVisual(false);
            ApplyFavoriteVisual(false);
            LoadThumbAsync(null);
            return;
        }
        MetaText.Text = string.Join("  ·  ", new[] { DateText(ep.Aired), ep.RuntimeText, ep.QualityText, ep.RatingText }
            .Where(s => !string.IsNullOrEmpty(s)));
        ApplyWatchedVisual(ep.IsWatched);
        ApplyFavoriteVisual(ep.IsFavorite);
        LoadThumbAsync(ep.LocalThumb);
    }

    /// <summary>"Aired 27 Jun 2020" from TMDb's yyyy-MM-dd.</summary>
    private static string AiredText(string? aired)
        => DateText(aired) is { Length: > 0 } d ? $"Aired {d}" : "";

    /// <summary>"27 Jun 2020" from yyyy-MM-dd, or "" when there's no date.</summary>
    private static string DateText(string? aired)
        => DateTime.TryParseExact(aired, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
               System.Globalization.DateTimeStyles.None, out var d)
            ? d.ToString("d MMM yyyy", System.Globalization.CultureInfo.CurrentCulture)
            : "";

    // v4.7.0 (#25): the toggles are coloured when on, muted (the style's theme
    // colour) when off. Per card, not static: a brush belongs to its thread.
    private readonly Microsoft.UI.Xaml.Media.SolidColorBrush _favOnBrush = new(Windows.UI.Color.FromArgb(0xFF, 0xF4, 0x3F, 0x5E));
    private readonly Microsoft.UI.Xaml.Media.SolidColorBrush _watchedOnBrush = new(Windows.UI.Color.FromArgb(0xCC, 0x22, 0xC5, 0x5E));
    private readonly Microsoft.UI.Xaml.Media.SolidColorBrush _whiteBrush = new(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));

    private void ApplyWatchedVisual(bool watched)
    {
        WatchedDim.Visibility = watched ? Visibility.Visible : Visibility.Collapsed;
        if (watched)
        {
            WatchedIcon.Foreground = _whiteBrush;
            WatchedBtn.Background = _watchedOnBrush;
        }
        else
        {
            WatchedIcon.ClearValue(IconElement.ForegroundProperty);
            WatchedBtn.ClearValue(Control.BackgroundProperty);
        }
    }

    private void ApplyFavoriteVisual(bool fav)
    {
        FavIcon.Glyph = fav ? "" : "";   // HeartFill / Heart
        if (fav) FavIcon.Foreground = _favOnBrush; else FavIcon.ClearValue(IconElement.ForegroundProperty);
    }

    private void OnPlay(object sender, RoutedEventArgs e)
    {
        if (Episode != null) AnyPlay?.Invoke(Episode);
    }

    private void OnToggleWatched(object sender, RoutedEventArgs e)
    {
        if (Episode != null) AnyWatchedToggle?.Invoke(Episode);
    }

    private void OnToggleFavorite(object sender, RoutedEventArgs e)
    {
        if (Episode != null) AnyFavoriteToggle?.Invoke(Episode);
    }

    private int _token;
    private async void LoadThumbAsync(string? relPath)
    {
        var my = ++_token;
        ThumbImage.Source = null;
        ThumbPlaceholder.Visibility = Visibility.Visible;
        if (relPath == null) return;
        var full = AppState.Instance.Db.GetCachedImagePath(relPath);
        if (full == null) return;
        try
        {
            var bytes = await Task.Run(() => ImageCache.GetOrLoad(relPath, full));
            if (my != _token || bytes == null) return;
            var bmp = new BitmapImage { DecodePixelWidth = 512 };   // 256 wide, sharp up to 200%
            using var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await ms.WriteAsync(bytes.AsBuffer());
            if (my != _token) return;
            ms.Seek(0);
            await bmp.SetSourceAsync(ms);
            if (my != _token) return;
            ThumbImage.Source = bmp;
            ThumbPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch { }
    }
}
