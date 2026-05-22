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

        // Episode cards raise these statics. Wire on Loaded / unwire on
        // Unloaded — the page is cached and reused by MainWindow, so doing
        // this in the constructor would leave the events unsubscribed after
        // the first time the user navigates away (Unloaded fires once),
        // which is why Play stopped working after switching pages.
        Loaded += (_, _) =>
        {
            TvEpisodeCard.AnyPlay -= OnEpisodePlay;
            TvEpisodeCard.AnyPlay += OnEpisodePlay;
            TvEpisodeCard.AnyWatchedToggle -= OnEpisodeWatchedToggle;
            TvEpisodeCard.AnyWatchedToggle += OnEpisodeWatchedToggle;
            TvEpisodeCard.AnyDetails -= OnEpisodeDetails;
            TvEpisodeCard.AnyDetails += OnEpisodeDetails;
        };
        Unloaded += (_, _) =>
        {
            TvEpisodeCard.AnyPlay -= OnEpisodePlay;
            TvEpisodeCard.AnyWatchedToggle -= OnEpisodeWatchedToggle;
            TvEpisodeCard.AnyDetails -= OnEpisodeDetails;
        };
    }

    /// <summary>
    /// v2.8.2 — single-tap an episode card to open a details dialog that
    /// surfaces everything the .nfo carries: plot, air date, rating,
    /// runtime, resolution / codec / HDR / audio / subtitles, container,
    /// and file size. Previously parsed and stored but never shown.
    /// </summary>
    private async void OnEpisodeDetails(TvEpisodeItem ep)
    {
        var d = AppState.Instance.Db.GetEpisodeDetail(ep.Id);
        if (d == null) return;

        var root = new StackPanel { Spacing = 12 };

        // Header line: code · aired · runtime · rating
        var meta = new List<string>();
        if (!string.IsNullOrEmpty(d.AiredText)) meta.Add(d.AiredText);
        if (!string.IsNullOrEmpty(d.RuntimeText)) meta.Add(d.RuntimeText);
        if (!string.IsNullOrEmpty(d.RatingText)) meta.Add(d.RatingText);
        root.Children.Add(new TextBlock
        {
            Text = $"{d.Code}   ·   {string.Join("   ·   ", meta)}",
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["MutedBrush"],
        });

        // Plot
        if (!string.IsNullOrWhiteSpace(d.Plot))
            root.Children.Add(new TextBlock
            {
                Text = d.Plot,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextBrush"],
                LineHeight = 21,
            });

        // Tech badges
        var badges = new List<string>();
        if (d.Resolution != null) badges.Add(d.Resolution);
        if (!string.IsNullOrEmpty(d.HdrType)) badges.Add(d.HdrType!.ToUpperInvariant());
        if (!string.IsNullOrEmpty(d.VideoCodec)) badges.Add(d.VideoCodec!.ToUpperInvariant());
        if (!string.IsNullOrEmpty(d.AudioCodec))
            badges.Add(d.AudioCodec!.ToUpperInvariant() + (string.IsNullOrEmpty(d.AudioChannels) ? "" : $" {d.AudioChannels}"));
        if (!string.IsNullOrEmpty(d.ContainerExt)) badges.Add(d.ContainerExt!.ToUpperInvariant());
        if (badges.Count > 0)
        {
            var wrap = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            foreach (var b in badges)
                wrap.Children.Add(new Border
                {
                    Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ChipBrush"],
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 3, 8, 3),
                    Child = new TextBlock { Text = b, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextBrush"] },
                });
            root.Children.Add(wrap);
        }

        // Detail rows (audio langs, subs, duration, file size)
        void AddRow(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var l = new TextBlock { Text = label, FontSize = 12,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["MutedBrush"] };
            var v = new TextBlock { Text = value, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextBrush"] };
            Grid.SetColumn(v, 1);
            g.Children.Add(l); g.Children.Add(v);
            root.Children.Add(g);
        }
        AddRow("Audio", d.AudioLanguages?.Replace(",", " · "));
        AddRow("Subtitles", d.SubtitleLanguages?.Replace(",", " · "));
        AddRow("Duration", d.DurationText);
        AddRow("File size", d.FileSizeText);

        var dlg = new ContentDialog
        {
            Title = $"{d.ShowTitle} — {d.Title}",
            Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 460 },
            PrimaryButtonText = "▶ Play",
            SecondaryButtonText = d.IsWatched ? "Mark unwatched" : "Mark watched",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = MainWindow.CurrentTheme,
        };
        var result = await dlg.ShowAsync();
        if (result == ContentDialogResult.Primary) OnEpisodePlay(ep);
        else if (result == ContentDialogResult.Secondary) OnEpisodeWatchedToggle(ep);
    }

    public void Load()
    {
        _level = Level.Shows;
        ShowLevel();
    }

    /// <summary>Open straight onto a specific show's page (deep-link from a list).</summary>
    public void OpenShow(int showId)
    {
        var show = AppState.Instance.Db.GetTvShows(AppState.Instance.Connected)
            .FirstOrDefault(s => s.Id == showId);
        if (show == null) { Load(); return; }
        _currentShow = show;
        _level = Level.Show;
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

            // Section header — label + episode/watched counts + actions.
            SeasonSectionsHost.Children.Add(BuildSeasonHeader(season, eps));

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

    /// <summary>
    /// Richer season bar: a "Season N" title, a "watched / total · runtime"
    /// sub-line, and Play-season + Mark-all-watched actions on the right.
    /// </summary>
    private FrameworkElement BuildSeasonHeader(TvSeason season, List<TvEpisodeItem> eps)
    {
        var muted = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["MutedBrush"];
        var text = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextBrush"];

        var grid = new Grid { Margin = new Thickness(24, 4, 24, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Left: title + sub-line
        var left = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock
        {
            Text = season.Season == 0 ? "Specials" : $"Season {season.Season}",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = text,
        });
        var totalMins = eps.Where(e => e.Runtime.HasValue).Sum(e => e.Runtime!.Value);
        var subBits = new List<string> { $"{season.EpisodeCount} episodes", $"{season.WatchedCount} watched" };
        if (totalMins > 0) subBits.Add(totalMins >= 60 ? $"{totalMins / 60}h {totalMins % 60}m" : $"{totalMins}m");
        left.Children.Add(new TextBlock
        {
            Text = string.Join("  ·  ", subBits),
            FontSize = 12,
            Foreground = muted,
        });
        grid.Children.Add(left);

        // Right: actions
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(actions, 1);
        bool anyUnwatched = eps.Any(e => !e.IsWatched);

        var playSeason = new Button
        {
            Content = anyUnwatched ? "▶ Play season" : "✓ Season watched",
            IsEnabled = anyUnwatched,
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BrandPurpleBrush"],
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6, 12, 6),
            FontSize = 12,
        };
        playSeason.Click += (_, _) =>
        {
            var next = eps.Where(e => !e.IsWatched).OrderBy(e => e.Episode).FirstOrDefault();
            if (next != null) OnEpisodePlay(next);
        };
        actions.Children.Add(playSeason);

        var markAll = new Button
        {
            Content = anyUnwatched ? "Mark all watched" : "Mark all unwatched",
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBrush"],
            Foreground = text,
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6, 12, 6),
            FontSize = 12,
        };
        markAll.Click += (_, _) =>
        {
            bool target = anyUnwatched;  // mark all watched if any unwatched, else unmark all
            foreach (var e in eps)
            {
                if (e.IsWatched != target)
                {
                    AppState.Instance.Db.SetEpisodeWatched(e.Id, target);
                    e.IsWatched = target;
                }
            }
            SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
            // Rebuild so the header counts + button labels refresh.
            BuildSeasonSections();
        };
        actions.Children.Add(markAll);

        grid.Children.Add(actions);
        return grid;
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

    // "📑 Add to list" — mirrors the movie flyout: toggle membership of any
    // list, plus "+ New list…". Lists are independent buckets, so a show
    // and a movie can share a list.
    private void OnShowListsFlyoutOpening(object sender, object e)
    {
        ShowListsFlyout.Items.Clear();
        if (_detail == null) return;
        var db = AppState.Instance.Db;
        var lists = db.GetUserLists();
        var membership = db.GetUserListsForShow(_detail.Id);

        if (lists.Count == 0)
            ShowListsFlyout.Items.Add(new MenuFlyoutItem { Text = "(no lists yet)", IsEnabled = false });
        else
            foreach (var ul in lists)
            {
                var item = new ToggleMenuFlyoutItem { Text = ul.Name, IsChecked = membership.Contains(ul.Id) };
                var captured = ul;
                item.Click += (_, _) =>
                {
                    if (item.IsChecked) db.AddShowToUserList(captured.Id, _detail!.Id);
                    else db.RemoveShowFromUserList(captured.Id, _detail!.Id);
                    SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
                };
                ShowListsFlyout.Items.Add(item);
            }

        ShowListsFlyout.Items.Add(new MenuFlyoutSeparator());
        var newItem = new MenuFlyoutItem { Text = "+ New list…" };
        newItem.Click += async (_, _) =>
        {
            var name = await PromptNewListName();
            if (string.IsNullOrWhiteSpace(name) || _detail == null) return;
            try
            {
                var listId = db.CreateUserList(name.Trim());
                db.AddShowToUserList(listId, _detail.Id);
                SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
            }
            catch (Microsoft.Data.Sqlite.SqliteException)
            {
                if (App.MainWindow is MainWindow mw) mw.ShowToast($"A list named “{name.Trim()}” already exists");
            }
        };
        ShowListsFlyout.Items.Add(newItem);
    }

    private async Task<string?> PromptNewListName()
    {
        var box = new TextBox { PlaceholderText = "List name" };
        var dlg = new ContentDialog
        {
            Title = "New list",
            Content = box,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = MainWindow.CurrentTheme,
        };
        var r = await dlg.ShowAsync();
        return r == ContentDialogResult.Primary ? box.Text : null;
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

        // Launch FIRST so a DB hiccup can never stop playback.
        bool launched = false;
        try { launched = await Windows.System.Launcher.LaunchUriAsync(new Uri(path)); }
        catch { launched = false; }
        if (!launched)
        {
            if (App.MainWindow is MainWindow mw) mw.ShowToast("Couldn't launch the video player");
            return;
        }

        // Bookkeeping — best effort, never blocks the user.
        try
        {
            AppState.Instance.Db.MarkEpisodePlayed(ep.Id);
            if (!ep.IsWatched)
            {
                AppState.Instance.Db.SetEpisodeWatched(ep.Id, true);
                ep.IsWatched = true;
            }
            RefreshHeaderProgress();
            SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
        }
        catch { }
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
