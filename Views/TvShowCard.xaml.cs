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

    // v3.7.0: card size follows the All TV shows S / M / L / XL picker.
    // TvShowsPage reloads the list after a change, so every card re-applies it.
    // v4.0.0: starts from the saved choice, so show rows on other pages (lists,
    // Favorites, To Watch) match it before All TV shows has been opened.
    public static double CardWidth { get; private set; }
    public static double CardHeight { get; private set; }
    static TvShowCard()
    {
        // A throwing static constructor would break every show card for the session.
        try { SetDensity(AppState.Instance.GetPref("tvDensity", "M")); }
        catch { SetDensity("M"); }
    }

    public static (double Width, double Height) SetDensity(string tag)
    {
        (CardWidth, CardHeight) = SizeFor(tag);
        return (CardWidth, CardHeight);
    }

    private static (double Width, double Height) SizeFor(string tag) => tag switch
    {
        "S"  => (130.0, 235.0),
        "L"  => (210.0, 365.0),
        "XL" => (250.0, 430.0),
        _    => (170.0, 300.0),   // M: the size before v3.7
    };

    // v4.5.1 (#24): the shows row on a page that mixes them with movies
    // (Favorites, To Watch, lists, search) follows that page's S / M / L / XL,
    // not All TV shows'. LibraryPage sets the size and marks its cards.
    public bool UsesLibrarySize { get; set; }
    private static (double Width, double Height) _librarySize = SizeFor("M");
    public static void SetLibraryDensity(string tag) => _librarySize = SizeFor(tag);
    private (double Width, double Height) CardSize => UsesLibrarySize ? _librarySize : (CardWidth, CardHeight);

    public TvShowCard()
    {
        InitializeComponent();
        PointerEntered += (_, _) =>
        {
            CardLift.Y = -4; CardBorder.Translation = new System.Numerics.Vector3(0, 0, 24);
            if (Show is { } s) { LoadHoverInfo(s); UpdateHoverActions(s); SetHoverPanel(true); }
        };
        PointerExited += (_, _) =>
        {
            CardLift.Y = 0; CardBorder.Translation = new System.Numerics.Vector3(0, 0, 0);
            SetHoverPanel(false);
        };
    }

    // The title strip would show through the panel behind its buttons.
    private void SetHoverPanel(bool open)
    {
        HoverOverlay.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        InfoStrip.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void OnShowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TvShowCard c && e.NewValue is TvShowListItem s) c.Populate(s);
    }

    private void Populate(TvShowListItem s)
    {
        SetHoverPanel(false);   // a recycled card may still show the last show's
        TitleText.Text = s.Title;
        // v4.1.0: on Continue Watching the card says which episode is next.
        MetaText.Text = s.NextEpisode != null ? $"Next: {s.NextEpisode}" : s.YearText;
        MetaText.Foreground = s.NextEpisode != null ? NextBrush : MetaBrush;
        FavBadge.Visibility = s.IsFavorite ? Visibility.Visible : Visibility.Collapsed;

        if (s.IsMissing) { StatusText.Text = "MISSING"; StatusBadge.Visibility = Visibility.Visible; }
        else if (!s.IsOnline) { StatusText.Text = "OFFLINE"; StatusBadge.Visibility = Visibility.Visible; }
        else StatusBadge.Visibility = Visibility.Collapsed;

        CardRoot.Height = CardSize.Height;
        InvalidateMeasure();   // the S / M / L / XL size may have changed
        ShowProgress(s);

        LoadPosterAsync(s.LocalPoster);
    }

    private void ShowProgress(TvShowListItem s)
    {
        ProgressText.Text = s.ProgressText;
        WatchedBadge.Visibility = s.FullyWatched ? Visibility.Visible : Visibility.Collapsed;
        ProgressDoneColumn.Width = new GridLength(s.ProgressFraction, GridUnitType.Star);
        ProgressLeftColumn.Width = new GridLength(1 - s.ProgressFraction, GridUnitType.Star);
    }

    // ── v4.1.0 hover panel ───────────────────────────────────────────────────

    /// <summary>
    /// Raised after the panel plays an episode or changes Favorite / Watchlist,
    /// so Favorites, To Watch and Continue Watching re-list their shows.
    /// </summary>
    public static event Action<TvShowListItem>? ShowChanged;

    private (int Id, string Code, string? File)? _next;

    private readonly Microsoft.UI.Xaml.Media.SolidColorBrush DoneBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0x4A, 0xDE, 0x80));
    private readonly Microsoft.UI.Xaml.Media.SolidColorBrush NoteBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0xF0, 0x99, 0x7B));

    private void LoadHoverInfo(TvShowListItem s)
    {
        var info = AppState.Instance.Db.GetShowHoverInfo(s.Id);
        HoverGenres.Text = info.Genres;
        HoverGenres.Visibility = info.Genres.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var watched = $"{s.WatchedCount}/{s.EpisodeCount} watched";
        HoverSeasons.Text = info.Seasons > 0
            ? $"{info.Seasons} season{(info.Seasons == 1 ? "" : "s")} · {watched}"
            : watched;
        _next = info.NextId is int id ? (id, info.NextCode!, info.NextFile) : null;
    }

    private void UpdateHoverActions(TvShowListItem s)
    {
        var online = AppState.Instance.Connected.ContainsKey(s.VolumeSerial);
        var canPlay = online && _next is { File: not null };
        HoverPlayBtn.Visibility = canPlay ? Visibility.Visible : Visibility.Collapsed;
        if (canPlay) HoverPlayBtn.Content = $"▶ Play {_next!.Value.Code}";

        // Can't play: say where the next episode is, or that the show is done.
        string? note = _next is { } n
            ? canPlay ? null
              : online ? $"Next: {n.Code}"
              : $"Next: {n.Code}\non {s.DriveLabel ?? "a drive that isn't connected"}"
            : s.EpisodeCount > 0 ? "✓ All watched" : null;
        HoverNote.Text = note ?? "";
        HoverNote.Foreground = _next == null ? DoneBrush : NoteBrush;
        HoverNote.Visibility = note != null ? Visibility.Visible : Visibility.Collapsed;

        // v4.5.0 (#23): icons, bright when on and dim when off
        FavIcon.Glyph = s.IsFavorite ? "" : "";            // HeartFill / Heart
        FavIcon.Foreground = s.IsFavorite ? FavOnBrush : IconOffBrush;
        WatchlistIcon.Glyph = s.IsWatchlist ? "" : "";     // PinFill / Pin
        WatchlistIcon.Foreground = s.IsWatchlist ? WatchlistOnBrush : IconOffBrush;
    }

    private readonly Microsoft.UI.Xaml.Media.SolidColorBrush IconOffBrush =
        new(Windows.UI.Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    private readonly Microsoft.UI.Xaml.Media.SolidColorBrush FavOnBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0xF4, 0x3F, 0x5E));
    private readonly Microsoft.UI.Xaml.Media.SolidColorBrush WatchlistOnBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0xA7, 0x8B, 0xFA));

    // Same as ▶ on the show page: start the player first, then record the play
    // and mark the episode watched.
    private async void OnHoverPlay(object sender, RoutedEventArgs e)
    {
        if (Show is not { } s || _next is not { File: { } file } next) return;
        var mw = App.MainWindow as MainWindow;
        if (!AppState.Instance.Connected.TryGetValue(s.VolumeSerial, out var letter)) return;
        var path = Path.Combine($"{letter}:\\", file.Replace('/', '\\'));
        if (!File.Exists(path)) { mw?.ShowToast($"Couldn't find {next.Code} on the drive"); return; }

        bool launched;
        try { launched = await VideoPlayer.PlayAsync(path); }
        catch { launched = false; }
        if (!launched) { mw?.ShowToast("Couldn't launch the video player"); return; }

        try
        {
            AppState.Instance.Db.MarkEpisodePlayed(next.Id);
            AppState.Instance.Db.SetEpisodeWatched(next.Id, true);
            s.WatchedCount++;
            ShowProgress(s);
            LoadHoverInfo(s);
            UpdateHoverActions(s);
            if (s.NextEpisode != null && _next is { } now)
            {
                s.NextEpisode = now.Code;
                MetaText.Text = $"Next: {now.Code}";
            }
            mw?.RefreshSidebar();
            ShowChanged?.Invoke(s);
        }
        catch { }
    }

    private void OnHoverFavorite(object sender, RoutedEventArgs e)
    {
        if (Show is not { } s) return;
        s.IsFavorite = !s.IsFavorite;
        AppState.Instance.Db.SetTvShowFavorite(s.Id, s.IsFavorite);
        FavBadge.Visibility = s.IsFavorite ? Visibility.Visible : Visibility.Collapsed;
        AfterHoverChange(s);
    }

    private void OnHoverWatchlist(object sender, RoutedEventArgs e)
    {
        if (Show is not { } s) return;
        s.IsWatchlist = !s.IsWatchlist;
        AppState.Instance.Db.SetTvShowWatchlist(s.Id, s.IsWatchlist);
        AfterHoverChange(s);
    }

    private void AfterHoverChange(TvShowListItem s)
    {
        UpdateHoverActions(s);
        (App.MainWindow as MainWindow)?.RefreshSidebar();
        ShowChanged?.Invoke(s);
    }

    // The pages open a show on Tapped; a click on a panel button mustn't also do that.
    private void OnHoverButtonTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        => e.Handled = true;

    // Per card, not static: a brush belongs to the thread that made it.
    private readonly Microsoft.UI.Xaml.Media.SolidColorBrush MetaBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0xD8, 0xD8, 0xD8));
    private readonly Microsoft.UI.Xaml.Media.SolidColorBrush NextBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0xA7, 0x8B, 0xFA));

    // v4.1.0: ask for the S / M / L / XL size but fill whatever width the layout
    // gives. Rows place the card at this size; the All TV shows grid stretches its
    // cells to fill the row, and the card now fills its cell instead of sitting in
    // the middle of it, so the grid's left edge lines up with the page title.
    // v4.5.0 (#22): the inside is laid out at the width it's drawn at, or a title
    // that fit on one line of the wider card still took two and sat a line too high.
    protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size availableSize)
    {
        var size = new Windows.Foundation.Size(CardSize.Width, CardSize.Height);
        base.MeasureOverride(new Windows.Foundation.Size(DrawnWidth(availableSize.Width), CardSize.Height));
        return size;
    }

    protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size finalSize)
    {
        base.ArrangeOverride(new Windows.Foundation.Size(DrawnWidth(finalSize.Width), finalSize.Height));
        return finalSize;
    }

    // Grow no wider than a 2:3 poster at the card's height, so the poster is never
    // cropped top and bottom (one XL column in a narrow window would be 500 wide).
    // Past that the card keeps to the left of its cell, in line with the title.
    // Rows don't stretch their cards (no width to fill), so they get the size asked for.
    private double DrawnWidth(double cellWidth)
        => double.IsInfinity(cellWidth) ? CardSize.Width
         : Math.Max(CardSize.Width, Math.Min(cellWidth, Math.Max(CardSize.Width, CardSize.Height * 2 / 3)));

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
