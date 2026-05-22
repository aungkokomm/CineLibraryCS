using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using CineLibraryCS.Models;
using CineLibraryCS.Services;

namespace CineLibraryCS.Views;

/// <summary>
/// v2.8 — TV Shows browser. Two levels: a grid of shows, and a single
/// show page (header + inline season sections of episode cards — no
/// per-season drill-down). Double-click / Play launches an episode.
/// </summary>
public sealed partial class TvShowsPage : Page
{
    private enum Level { Shows, Show }
    private Level _level = Level.Shows;

    private readonly ObservableCollection<TvShowListItem> _shows = new();
    private TvShowListItem? _currentShow;

    // All episodes currently on the show page, for Play-next + event routing.
    private readonly List<TvEpisodeItem> _showEpisodes = new();

    public event EventHandler? SidebarRefreshRequested;

    public TvShowsPage()
    {
        InitializeComponent();
        ShowsRepeater.ItemsSource = _shows;
        ShowsRepeater.Tapped += OnShowsTapped;

        // Episode cards raise these statics; wire once for the page lifetime.
        TvEpisodeCard.AnyPlay += OnEpisodePlay;
        TvEpisodeCard.AnyWatchedToggle += OnEpisodeWatchedToggle;
        Unloaded += (_, _) =>
        {
            TvEpisodeCard.AnyPlay -= OnEpisodePlay;
            TvEpisodeCard.AnyWatchedToggle -= OnEpisodeWatchedToggle;
        };
    }

    public void Load()
    {
        _level = Level.Shows;
        ShowLevel();
    }

    private void ShowLevel()
    {
        BackBtn.Visibility      = _level == Level.Shows ? Visibility.Collapsed : Visibility.Visible;
        ShowsRepeater.Visibility = _level == Level.Shows ? Visibility.Visible : Visibility.Collapsed;
        SeasonsPanel.Visibility  = _level == Level.Show  ? Visibility.Visible : Visibility.Collapsed;

        if (_level == Level.Shows) LoadShows();
        else LoadShow();
    }

    private void LoadShows()
    {
        TitleText.Text = "ALL TV SHOWS";
        BackLabel.Text = "Back";
        var shows = AppState.Instance.Db.GetTvShows(AppState.Instance.Connected);
        _shows.Clear();
        foreach (var s in shows) _shows.Add(s);
        SubText.Text = shows.Count == 1 ? "1 show" : $"{shows.Count} shows";
        EmptyState.Visibility = shows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private TvShowDetail? _detail;

    private void LoadShow()
    {
        if (_currentShow == null) { _level = Level.Shows; ShowLevel(); return; }
        TitleText.Text = _currentShow.Title.ToUpperInvariant();
        BackLabel.Text = "All TV Shows";
        EmptyState.Visibility = Visibility.Collapsed;

        PopulateShowHeader();
        BuildSeasonSections();
    }

    /// <summary>Build a section per season: header + a wrap of episode cards.</summary>
    private void BuildSeasonSections()
    {
        SeasonSectionsHost.Children.Clear();
        _showEpisodes.Clear();
        if (_currentShow == null) return;

        var connected = AppState.Instance.Connected;
        var seasons = AppState.Instance.Db.GetSeasons(_currentShow.Id);
        SubText.Text = $"{seasons.Count} season{(seasons.Count == 1 ? "" : "s")}";

        foreach (var season in seasons)
        {
            var eps = AppState.Instance.Db.GetEpisodes(_currentShow.Id, season.Season, connected);
            _showEpisodes.AddRange(eps);

            // Section header
            var header = new TextBlock
            {
                Text = $"{(season.Season == 0 ? "SPECIALS" : "SEASON " + season.Season)}   ·   {season.WatchedCount}/{season.EpisodeCount} watched",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                CharacterSpacing = 150,
                Opacity = 0.7,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["MutedBrush"],
                Margin = new Thickness(24, 0, 24, 8),
            };
            SeasonSectionsHost.Children.Add(header);

            // Episode cards in a HORIZONTAL row (Netflix/Disney+ style).
            // Critical for performance: a vertical UniformGridLayout nested
            // in a vertical StackPanel inside a ScrollViewer can't virtualize
            // (it's measured with infinite height), so every card realizes
            // and re-measures on each scroll tick → the UI freezes on big
            // shows. A horizontal row has a bounded height and virtualizes
            // along its scrolling axis, so only the visible cards exist.
            var rowScroller = new ScrollViewer
            {
                Margin = new Thickness(24, 0, 24, 4),
                Height = 244,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollMode = ScrollMode.Disabled,
                HorizontalScrollMode = ScrollMode.Enabled,
            };
            var repeater = new ItemsRepeater
            {
                Layout = new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 14,
                },
                ItemTemplate = (DataTemplate)Resources["EpisodeCardTemplate"],
                ItemsSource = eps,
            };
            rowScroller.Content = repeater;
            SeasonSectionsHost.Children.Add(rowScroller);
        }

        // Header progress + Play-next label depend on the full episode set.
        RefreshHeaderProgress();
    }

    private void PopulateShowHeader()
    {
        if (_currentShow == null) return;
        _detail = AppState.Instance.Db.GetTvShowDetail(_currentShow.Id);
        if (_detail == null) return;

        ShowTitle.Text = _detail.Title;
        ShowYear.Text = _detail.Year?.ToString() ?? "";
        ShowYear.Visibility = _detail.Year.HasValue ? Visibility.Visible : Visibility.Collapsed;
        ShowRating.Text = _detail.Rating.HasValue ? $"★ {_detail.Rating:F1}" : "";
        ShowRating.Visibility = _detail.Rating.HasValue ? Visibility.Visible : Visibility.Collapsed;
        ShowStatus.Text = _detail.Status ?? "";
        ShowStatus.Visibility = string.IsNullOrEmpty(_detail.Status) ? Visibility.Collapsed : Visibility.Visible;
        ShowProgress.Text = $"{_detail.WatchedCount}/{_detail.EpisodeCount} watched";
        ShowPlot.Text = _detail.Plot ?? "";
        ShowPlot.Visibility = string.IsNullOrWhiteSpace(_detail.Plot) ? Visibility.Collapsed : Visibility.Visible;

        // Genre chips
        ShowGenres.Children.Clear();
        foreach (var g in _detail.Genres.Take(5))
        {
            ShowGenres.Children.Add(new Border
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2, 8, 2),
                Child = new TextBlock { Text = g, FontSize = 11, Foreground =
                    new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White) },
            });
        }

        UpdateShowButtons();

        // Cast
        CastRepeater.ItemsSource = _detail.Actors;
        var folderAbs = ResolveShowFolderAbs(_detail);
        if (folderAbs != null) _ = LoadCastThumbsAsync(_detail.Actors, folderAbs);

        LoadShowImage(ShowPoster, _detail.LocalPoster, 260);
        LoadShowImage(ShowFanart, _detail.LocalFanart, 900);
    }

    private void UpdateShowButtons()
    {
        if (_detail == null) return;
        ShowFavBtn.Content = _detail.IsFavorite ? "★ Favorited" : "☆ Favorite";
        ShowWatchlistBtn.Content = _detail.IsWatchlist ? "📌 In Watchlist" : "📋 Watchlist";
    }

    private void OnToggleShowFavorite(object sender, RoutedEventArgs e)
    {
        if (_detail == null) return;
        _detail.IsFavorite = !_detail.IsFavorite;
        AppState.Instance.Db.SetTvShowFavorite(_detail.Id, _detail.IsFavorite);
        if (_currentShow != null) _currentShow.IsFavorite = _detail.IsFavorite;
        UpdateShowButtons();
        SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnToggleShowWatchlist(object sender, RoutedEventArgs e)
    {
        if (_detail == null) return;
        _detail.IsWatchlist = !_detail.IsWatchlist;
        AppState.Instance.Db.SetTvShowWatchlist(_detail.Id, _detail.IsWatchlist);
        if (_currentShow != null) _currentShow.IsWatchlist = _detail.IsWatchlist;
        UpdateShowButtons();
        SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private static string? ResolveShowFolderAbs(TvShowDetail d)
    {
        if (string.IsNullOrEmpty(d.FolderRelPath)) return null;
        if (!AppState.Instance.Connected.TryGetValue(d.VolumeSerial, out var letter)) return null;
        return Path.Combine($"{letter}:\\", d.FolderRelPath.Replace('/', '\\'));
    }

    // Cast thumbnails — mirror MovieDetailDialog: look in the show's
    // .actors folder first, then any inline thumb URL/path.
    private static readonly string[] ActorThumbExts = { ".jpg", ".jpeg", ".png", ".tbn", ".webp" };

    private async Task LoadCastThumbsAsync(IReadOnlyList<Models.Actor> actors, string showFolderAbs)
    {
        await Task.Run(() =>
        {
            var actorsDir = Path.Combine(showFolderAbs, ".actors");
            foreach (var a in actors)
            {
                Uri? uri = null;
                try
                {
                    if (Directory.Exists(actorsDir))
                    {
                        foreach (var stem in new[] { a.Name.Replace(' ', '_'), a.Name })
                        foreach (var ext in ActorThumbExts)
                        {
                            var p = Path.Combine(actorsDir, stem + ext);
                            if (File.Exists(p)) { uri = new Uri(p.Replace("#", "%23").Replace("?", "%3F")); break; }
                        }
                    }
                    if (uri == null && !string.IsNullOrWhiteSpace(a.Thumb))
                    {
                        var raw = a.Thumb!.Trim();
                        if (raw.StartsWith("http", StringComparison.OrdinalIgnoreCase)) uri = new Uri(raw);
                        else if (File.Exists(raw)) uri = new Uri(raw);
                    }
                }
                catch { }
                if (uri == null) continue;
                var capturedUri = uri;
                DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage { DecodePixelWidth = 128 };
                        bmp.UriSource = capturedUri;
                        a.ThumbBitmap = bmp;
                    }
                    catch { }
                });
            }
        });
    }

    private async void LoadShowImage(Image target, string? relPath, int decodeWidth)
    {
        target.Source = null;
        if (relPath == null) return;
        var full = AppState.Instance.Db.GetCachedImagePath(relPath);
        if (full == null) return;
        try
        {
            var bytes = await Task.Run(() => ImageCache.GetOrLoad(relPath, full));
            if (bytes == null) return;
            var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage { DecodePixelWidth = decodeWidth };
            using var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await ms.WriteAsync(bytes.AsBuffer());
            ms.Seek(0);
            await bmp.SetSourceAsync(ms);
            target.Source = bmp;
        }
        catch { }
    }

    private void OnShowsTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        // Walk up from the tapped element to find the TvShowCard.
        var d = e.OriginalSource as DependencyObject;
        while (d != null && d is not TvShowCard)
            d = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(d);
        if (d is TvShowCard card && card.Show != null)
        {
            _currentShow = card.Show;
            _level = Level.Show;
            ShowLevel();
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        _level = Level.Shows;
        ShowLevel();
    }

    private void OnEpisodeWatchedToggle(TvEpisodeItem ep)
    {
        var newState = !ep.IsWatched;
        AppState.Instance.Db.SetEpisodeWatched(ep.Id, newState);
        ep.IsWatched = newState;            // card updates via PropertyChanged
        RefreshHeaderProgress();
        SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void OnEpisodePlay(TvEpisodeItem ep)
    {
        if (ep.VideoFileRelPath == null) return;
        var connected = AppState.Instance.Connected;
        if (!connected.TryGetValue(ep.VolumeSerial, out var letter)) { await ShowOfflineDialog(ep.Title); return; }
        var path = Path.Combine($"{letter}:\\", ep.VideoFileRelPath.Replace('/', '\\'));
        if (!File.Exists(path)) { await ShowOfflineDialog(ep.Title); return; }
        try
        {
            AppState.Instance.Db.MarkEpisodePlayed(ep.Id);
            if (!ep.IsWatched)
            {
                AppState.Instance.Db.SetEpisodeWatched(ep.Id, true);
                ep.IsWatched = true;
            }
            RefreshHeaderProgress();
            await Windows.System.Launcher.LaunchUriAsync(new Uri(path));
            SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            if (App.MainWindow is MainWindow mw) mw.ShowToast("Couldn't launch the video player");
        }
    }

    /// <summary>"▶ Play next unwatched" — first unwatched by season then episode.</summary>
    private void OnPlayNext(object sender, RoutedEventArgs e)
    {
        var next = _showEpisodes
            .Where(ep => !ep.IsWatched)
            .OrderBy(ep => ep.Season).ThenBy(ep => ep.Episode)
            .FirstOrDefault();
        if (next != null) OnEpisodePlay(next);
        else if (App.MainWindow is MainWindow mw) mw.ShowToast("All episodes watched 🎉");
    }

    private void RefreshHeaderProgress()
    {
        var total = _showEpisodes.Count;
        var watched = _showEpisodes.Count(x => x.IsWatched);
        ShowProgress.Text = $"{watched}/{total} watched";
        UpdatePlayNextLabel(watched, total);
    }

    private void UpdatePlayNextLabel(int watched, int total)
    {
        bool any = watched < total;
        PlayNextBtn.Content = watched == 0 ? "▶ Play S01E01" : (any ? "▶ Play next" : "✓ All watched");
        PlayNextBtn.IsEnabled = any;
    }

    private async Task ShowOfflineDialog(string title)
    {
        var dlg = new ContentDialog
        {
            Title = "Can't play yet",
            Content = $"\"{title}\" is on a drive that isn't connected. Plug it in and try again.",
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
            RequestedTheme = MainWindow.CurrentTheme,
        };
        try { await dlg.ShowAsync(); } catch { }
    }
}
