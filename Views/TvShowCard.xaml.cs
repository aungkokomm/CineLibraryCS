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

    /// <summary>v4.8.0: the title, year and progress under the poster (the XAML's caption block).</summary>
    public const double CaptionHeight = 54;

    /// <summary>v4.8.0: a 2:3 poster with the caption under it (Collections uses these too).
    /// v4.8.1 (#25): as wide as a movie card, so S, M, L and XL are one size on every
    /// page; shows were a step wider and fitted one card fewer in a row.</summary>
    public static (double Width, double Height) SizeFor(string tag)
    {
        var width = MovieCardControl.SizeFor(tag).Width;
        return (width, width * 1.5 + CaptionHeight);
    }

    // v4.5.1 (#24): the shows row on a page that mixes them with movies
    // (Favorites, To Watch, lists, search) follows that page's S / M / L / XL,
    // not All TV shows'. LibraryPage sets the size and marks its cards.
    public bool UsesLibrarySize { get; set; }

    /// <summary>v4.9.0 (#25): a Watched &amp; Gone record. No drive badge, no Play or
    /// toggles, and the menu offers Restore / Delete record, as movie records do.</summary>
    public bool ArchiveMode { get; set; }

    /// <summary>v4.9.0: raised after a show is sent to Watched &amp; Gone, restored or its
    /// record deleted, so All TV shows and the Watched &amp; Gone page re-list.</summary>
    public static event Action? ShowsArchived;
    private static (double Width, double Height) _librarySize = SizeFor("M");
    public static void SetLibraryDensity(string tag) => _librarySize = SizeFor(tag);

    /// <summary>v4.9.0 (#25): a row of shows above a grid of movie cards takes the size the
    /// grid draws them at (it widens them to fill its row), so the shows are as big as the
    /// movies; they kept the plain S / M / L / XL size and looked a step smaller. The poster
    /// is the movie poster's size; the caption under it is the show card's. Cards already in
    /// the rows are measured again.</summary>
    public static void FitRowsToGrid(UniformGridLayout grid, double gridAvailableWidth, params ItemsRepeater[] rows)
    {
        if (gridAvailableWidth <= 0) return;
        var (width, height) = Controls.CardCell.StretchedSize(grid, gridAvailableWidth);
        _librarySize = (width, height - MovieCardControl.CaptionHeight + CaptionHeight);
        foreach (var row in rows)
        {
            for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(row); i++)
                (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(row, i) as UIElement)?.InvalidateMeasure();
            row.InvalidateMeasure();
        }
    }

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
        Loaded += (_, _) =>
        {
            UiSettings.Changed -= OnUiSettingsChanged;
            UiSettings.Changed += OnUiSettingsChanged;
            ApplyStandIn();
        };
        Unloaded += (_, _) => UiSettings.Changed -= OnUiSettingsChanged;

        // v4.9.0 (#25): a right-click menu, as movie cards have. Built on opening, so
        // the ticks and the lists are the show's as it is now.
        var menu = new MenuFlyout();
        menu.Opening += (_, _) => BuildContextMenu(menu);
        ContextFlyout = menu;

        // v4.8.0: the keyboard, as on movie cards: arrows move, Enter opens the
        // show, Space plays its next episode
        GotFocus += (_, _) =>
        {
            if (FocusState == FocusState.Pointer) return;
            FocusRing.Visibility = Visibility.Visible;
            StartBringIntoView(new BringIntoViewOptions { AnimationDesired = !UiSettings.ReduceMotion });
        };
        LostFocus += (_, _) => FocusRing.Visibility = Visibility.Collapsed;
        KeyDown += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, this) || Show is not { } s) return;   // a button in the hover panel
            switch (e.Key)
            {
                case Windows.System.VirtualKey.Enter:
                    e.Handled = true;
                    (App.MainWindow as MainWindow)?.OpenTvShow(s.Id);
                    break;
                case Windows.System.VirtualKey.Space when !ArchiveMode:   // a record doesn't play
                    e.Handled = true;
                    LoadHoverInfo(s);
                    OnHoverPlay(this, new RoutedEventArgs());
                    break;
                default: e.Handled = Controls.CardCell.MoveFocus(this, e.Key); break;
            }
        };
    }

    // v4.8.0: the panel covers the poster only; the title under it stays.
    private void SetHoverPanel(bool open)
    {
        HoverOverlay.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        PlaceholderText.Opacity = open ? 0 : 1;   // the hover panel's text would sit on the stand-in's
    }

    private void OnTitlePointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => ShowFullTitle(true);
    private void OnTitlePointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => ShowFullTitle(false);

    /// <summary>v4.8.0: the whole title over a title cut short (see MovieCardControl).</summary>
    private void ShowFullTitle(bool show)
    {
        if (show && !TitleText.IsTextTrimmed) return;
        if (show)
        {
            FullTitleText.Text = TitleText.Text;
            var titleBottom = TitleText.TransformToVisual(CardRoot)
                .TransformPoint(new Windows.Foundation.Point(0, TitleText.ActualHeight)).Y;
            FullTitleShift.Y = titleBottom + FullTitle.Padding.Bottom + FullTitle.BorderThickness.Bottom - CardBorder.ActualHeight;
        }
        FullTitle.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    // v4.8.0: Settings' stand-in posters, live
    private void OnUiSettingsChanged() => DispatcherQueue.Post(ApplyStandIn);
    private void ApplyStandIn() => PosterPlaceholder.Opacity = UiSettings.StandInPosters ? 1 : 0;

    private static void OnShowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TvShowCard c && e.NewValue is TvShowListItem s) c.Populate(s);
    }

    private void Populate(TvShowListItem s)
    {
        SetHoverPanel(false);   // a recycled card may still show the last show's
        ShowFullTitle(false);
        TitleText.Text = s.Title;
        PlaceholderTitle.Text = s.Title;
        PlaceholderTitle.FontSize = Controls.PlaceholderArt.FontSizeFor(CardSize.Width);
        PlaceholderYear.Text = s.YearText;
        PosterPlaceholder.Background = Controls.PlaceholderArt.BrushFor(s.Title);
        // v4.1.0: on Continue Watching the card says which episode is next.
        MetaText.Text = s.NextEpisode == null ? s.YearText : s.NextEpisodeLeft != null ? $"{s.NextEpisode} · {s.NextEpisodeLeft}" : $"Next: {s.NextEpisode}";
        MetaText.Foreground = s.NextEpisode != null ? NextBrush : ThemeBrushes.Get("MutedBrush");
        FavBadge.Visibility = s.IsFavorite ? Visibility.Visible : Visibility.Collapsed;

        // v4.9.0: as on movie cards, MISSING stays loud and OFFLINE is a quiet dot:
        // an unplugged drive is the normal state of every show on it. A record has neither.
        if (ArchiveMode) StatusBadge.Visibility = Visibility.Collapsed;
        else if (s.IsMissing)
        {
            StatusDot.Fill = MissingDotBrush;
            StatusText.Text = "MISSING";
            StatusText.Visibility = Visibility.Visible;
            StatusBadge.Background = LoudBadgeBrush;
            StatusBadge.Visibility = Visibility.Visible;
        }
        else if (!s.IsOnline)
        {
            StatusDot.Fill = OfflineDotBrush;
            StatusText.Visibility = Visibility.Collapsed;
            StatusBadge.Background = QuietBadgeBrush;
            StatusBadge.Visibility = Visibility.Visible;
        }
        else StatusBadge.Visibility = Visibility.Collapsed;

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
        // v4.9.0 (#25): the whole show, green when every episode is watched
        WatchedIcon.Foreground = s.FullyWatched ? WhiteBrush : IconOffBrush;
        if (s.FullyWatched) HoverWatchedBtn.Background = WatchedOnBrush;   // the green of the ✓ badge
        else HoverWatchedBtn.ClearValue(Control.BackgroundProperty);
        HoverWatchedBtn.Visibility = s.EpisodeCount > 0 ? Visibility.Visible : Visibility.Collapsed;

        // v4.9.0: a record shows what it is, nothing to play or change
        if (ArchiveMode)
        {
            HoverPlayBtn.Visibility = HoverNote.Visibility = Visibility.Collapsed;
            HoverWatchlistBtn.Visibility = HoverWatchedBtn.Visibility = HoverFavBtn.Visibility = Visibility.Collapsed;
        }
    }

    private readonly Microsoft.UI.Xaml.Media.SolidColorBrush IconOffBrush =
        new(Windows.UI.Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    private readonly Microsoft.UI.Xaml.Media.SolidColorBrush WhiteBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
    private readonly Microsoft.UI.Xaml.Media.SolidColorBrush WatchedOnBrush =
        new(Windows.UI.Color.FromArgb(0xCC, 0x22, 0xC5, 0x5E));
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

        PlayedWith how;
        try
        {
            how = await VideoPlayer.PlayAsync(path, new PlaybackItem(PlaybackKind.Episode, next.Id, $"{s.Title} {next.Code}"),
                (_, watched) => { if (watched) EpisodeWatched(s); else mw?.RefreshSidebar(); });
        }
        catch { how = PlayedWith.Nothing; }
        if (how == PlayedWith.Nothing) { mw?.ShowToast("Couldn't launch the video player"); return; }

        try
        {
            AppState.Instance.Db.MarkEpisodePlayed(next.Id);
            // v4.10.0: the built-in player marks it watched near the end; another player never tells
            if (how == PlayedWith.External)
            {
                AppState.Instance.Db.SetEpisodeWatched(next.Id, true);
                EpisodeWatched(s);
            }
            else mw?.RefreshSidebar();
        }
        catch { }
    }

    private void EpisodeWatched(TvShowListItem s)
    {
        s.WatchedCount++;
        // The card may show another show by the time the built-in player says so
        if (Show == s)
        {
            ShowProgress(s);
            LoadHoverInfo(s);
            UpdateHoverActions(s);
            if (s.NextEpisode != null && _next is { } now)
            {
                s.NextEpisode = now.Code;
                MetaText.Text = $"Next: {now.Code}";
            }
        }
        (App.MainWindow as MainWindow)?.RefreshSidebar();
        ShowChanged?.Invoke(s);
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

    private async void OnHoverWatched(object sender, RoutedEventArgs e)
    {
        if (Show is { } s) await ToggleShowWatchedAsync(s);
    }

    /// <summary>v4.9.0 (#25): every episode watched, or none, after asking: one click
    /// for what took Mark all watched on each season.</summary>
    private async Task ToggleShowWatchedAsync(TvShowListItem s)
    {
        if (s.EpisodeCount == 0) return;
        var watch = !s.FullyWatched;
        var count = watch ? s.EpisodeCount - s.WatchedCount : s.WatchedCount;
        var episodes = count == 1 ? "1 episode" : $"{count:N0} episodes";
        var dlg = new ContentDialog
        {
            Title = watch ? $"Mark “{s.Title}” watched?" : $"Mark “{s.Title}” unwatched?",
            Content = watch
                ? $"{episodes} not yet watched will be marked watched, every season, as Mark all watched does on the show page."
                : $"All {episodes} will be marked unwatched, every season.",
            PrimaryButtonText = watch ? "Mark watched" : "Mark unwatched",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = MainWindow.CurrentTheme,
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

        AppState.Instance.Db.SetShowWatched(s.Id, watch);
        s.WatchedCount = watch ? s.EpisodeCount : 0;
        ShowProgress(s);
        LoadHoverInfo(s);
        AfterHoverChange(s);
    }

    private void BuildContextMenu(MenuFlyout menu)
    {
        menu.Items.Clear();
        if (Show is not { } s) return;
        var mw = App.MainWindow as MainWindow;
        if (ArchiveMode) { BuildRecordMenu(menu, s); return; }
        LoadHoverInfo(s);   // the next episode, for Play

        var online = AppState.Instance.Connected.ContainsKey(s.VolumeSerial);
        var play = new MenuFlyoutItem
        {
            Text = _next is { } n ? $"▶ Play {n.Code}" : "▶ Play",
            IsEnabled = online && _next is { File: not null },
        };
        play.Click += (_, _) => OnHoverPlay(this, new RoutedEventArgs());
        menu.Items.Add(play);
        var open = new MenuFlyoutItem { Text = "Open show" };
        open.Click += (_, _) => mw?.OpenTvShow(s.Id);
        menu.Items.Add(open);
        menu.Items.Add(new MenuFlyoutSeparator());

        var watched = new ToggleMenuFlyoutItem { Text = "Watched", IsChecked = s.FullyWatched, IsEnabled = s.EpisodeCount > 0 };
        watched.Click += async (_, _) => await ToggleShowWatchedAsync(s);
        menu.Items.Add(watched);
        var fav = new ToggleMenuFlyoutItem { Text = "Favorite", IsChecked = s.IsFavorite };
        fav.Click += (_, _) => OnHoverFavorite(this, new RoutedEventArgs());
        menu.Items.Add(fav);
        var watchlist = new ToggleMenuFlyoutItem { Text = "Watchlist", IsChecked = s.IsWatchlist };
        watchlist.Click += (_, _) => OnHoverWatchlist(this, new RoutedEventArgs());
        menu.Items.Add(watchlist);
        menu.Items.Add(new MenuFlyoutSeparator());

        // the movie card's Add to list, for the show
        var lists = new MenuFlyoutSubItem { Text = "📑 Add to list" };
        var allLists = AppState.Instance.Db.GetUserLists();
        var membership = AppState.Instance.Db.GetUserListsForShow(s.Id);
        if (allLists.Count == 0)
            lists.Items.Add(new MenuFlyoutItem { Text = "(no lists yet)", IsEnabled = false });
        foreach (var ul in allLists)
        {
            var item = new ToggleMenuFlyoutItem { Text = ul.Name, IsChecked = membership.Contains(ul.Id) };
            var listId = ul.Id;
            item.Click += (_, _) =>
            {
                if (item.IsChecked) AppState.Instance.Db.AddShowToUserList(listId, s.Id);
                else AppState.Instance.Db.RemoveShowFromUserList(listId, s.Id);
                mw?.RefreshSidebar();
                ShowChanged?.Invoke(s);
            };
            lists.Items.Add(item);
        }
        lists.Items.Add(new MenuFlyoutSeparator());
        var newList = new MenuFlyoutItem { Text = "+ New list…" };
        newList.Click += async (_, _) =>
        {
            var name = await MovieCardControl.PromptNewListNameDialog(XamlRoot);
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                var id = AppState.Instance.Db.CreateUserList(name.Trim());
                AppState.Instance.Db.AddShowToUserList(id, s.Id);
                mw?.RefreshSidebar();
                ShowChanged?.Invoke(s);
            }
            catch (Microsoft.Data.Sqlite.SqliteException)
            {
                mw?.ShowToast($"A list named “{name.Trim()}” already exists");
            }
        };
        lists.Items.Add(newList);
        menu.Items.Add(lists);
        menu.Items.Add(new MenuFlyoutSeparator());

        // the show page's Open Folder
        var detail = online ? AppState.Instance.Db.GetTvShowDetail(s.Id) : null;
        var folder = detail is { FolderRelPath: { Length: > 0 } rel } && AppState.Instance.Connected.TryGetValue(s.VolumeSerial, out var letter)
            ? Path.Combine($"{letter}:\\", rel.Replace('/', '\\')) : null;
        var openFolder = new MenuFlyoutItem { Text = "📂 Open folder", IsEnabled = folder != null };
        openFolder.Click += async (_, _) =>
        {
            if (folder != null && Directory.Exists(folder))
                await Windows.System.Launcher.LaunchFolderPathAsync(folder);
        };
        menu.Items.Add(openFolder);

        // v4.9.0 (#25): Watched & Gone, as movie cards have it
        menu.Items.Add(new MenuFlyoutSeparator());
        var archive = new MenuFlyoutItem
        {
            Text = "Send to Watched & Gone",
            Icon = new FontIcon { Glyph = "", FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons,Segoe MDL2 Assets") },
        };
        archive.Click += async (_, _) =>
        {
            var dlg = new ContentDialog
            {
                Title = "Send to Watched & Gone?",
                Content = $"“{s.Title}” moves out of your library into Watched & Gone. Its poster, details, " +
                          "episodes, your notes and what you've watched are all kept as a record. " +
                          "The files on your drive are not touched.\n\n" +
                          "You can restore it from the Watched & Gone page anytime, and it comes back by " +
                          "itself if the show turns up in another folder.",
                PrimaryButtonText = "Send",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
                RequestedTheme = MainWindow.CurrentTheme,
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            AppState.Instance.Db.ArchiveShows(new[] { s.Id });
            mw?.ShowToast($"“{s.Title}” sent to Watched & Gone");
            AfterArchiveChange(s);
        };
        menu.Items.Add(archive);
    }

    /// <summary>v4.9.0: a record's menu on the Watched &amp; Gone page, as for movie records.</summary>
    private void BuildRecordMenu(MenuFlyout menu, TvShowListItem s)
    {
        var mw = App.MainWindow as MainWindow;
        var open = new MenuFlyoutItem { Text = "Open show" };
        open.Click += (_, _) => mw?.OpenTvShow(s.Id);
        menu.Items.Add(open);
        menu.Items.Add(new MenuFlyoutSeparator());

        var restore = new MenuFlyoutItem { Text = "Restore to library" };
        restore.Click += (_, _) =>
        {
            AppState.Instance.Db.RestoreArchivedShow(s.Id);
            mw?.ShowToast($"“{s.Title}” restored to your library");
            AfterArchiveChange(s);
        };
        menu.Items.Add(restore);

        var delete = new MenuFlyoutItem
        {
            Text = "Delete record permanently",
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xEF, 0x44, 0x44)),
        };
        delete.Click += async (_, _) =>
        {
            var dlg = new ContentDialog
            {
                Title = "Delete this record?",
                Content = $"“{s.Title}”: its record, episodes, cached pictures, notes and watch history " +
                          "will be permanently removed from CineLibrary. This can't be undone.",
                PrimaryButtonText = "Delete forever",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
                RequestedTheme = MainWindow.CurrentTheme,
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            AppState.Instance.Db.DeleteArchivedShow(s.Id);
            mw?.ShowToast("Record deleted");
            AfterArchiveChange(s);
        };
        menu.Items.Add(delete);
    }

    private static void AfterArchiveChange(TvShowListItem s)
    {
        (App.MainWindow as MainWindow)?.RefreshSidebar();
        ShowChanged?.Invoke(s);   // the pages with a shows row re-list
        ShowsArchived?.Invoke();
    }

    // The pages open a show on Tapped; a click on a panel button mustn't also do that.
    private void OnHoverButtonTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        => e.Handled = true;

    // Per card, not static: a brush belongs to the thread that made it.
    private readonly Microsoft.UI.Xaml.Media.SolidColorBrush NextBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0xA7, 0x8B, 0xFA));
    private readonly Microsoft.UI.Xaml.Media.SolidColorBrush MissingDotBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0xEF, 0x44, 0x44));
    private readonly Microsoft.UI.Xaml.Media.SolidColorBrush OfflineDotBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0x9C, 0xA3, 0xAF));
    private readonly Microsoft.UI.Xaml.Media.SolidColorBrush LoudBadgeBrush =
        new(Windows.UI.Color.FromArgb(0xCC, 0, 0, 0));
    private readonly Microsoft.UI.Xaml.Media.SolidColorBrush QuietBadgeBrush =
        new(Windows.UI.Color.FromArgb(0x66, 0, 0, 0));

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

    // v4.8.0: the whole cell. The grids scale a cell's height with its width
    // (CardCell.FitGrid), so the poster stays 2:3 at any width.
    // Rows don't stretch their cards (no width to fill), so they get the size asked for.
    private double DrawnWidth(double cellWidth)
        => double.IsInfinity(cellWidth) ? CardSize.Width : cellWidth;

    private int _token;
    private async void LoadPosterAsync(string? relPath)
    {
        var my = ++_token;
        PosterImage.Source = null;
        // v4.8.0: the stand-in only when there's no poster to show (see MovieCardControl)
        PosterPlaceholder.Visibility = Visibility.Collapsed;
        if (relPath == null) { PosterPlaceholder.Visibility = Visibility.Visible; return; }
        var full = AppState.Instance.Db.GetCachedImagePath(relPath);
        if (full == null) { PosterPlaceholder.Visibility = Visibility.Visible; return; }
        try
        {
            var bytes = await Task.Run(() => ImageCache.GetOrLoad(relPath, full));
            if (my != _token) return;
            if (bytes == null) { PosterPlaceholder.Visibility = Visibility.Visible; return; }
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
        catch { if (my == _token) PosterPlaceholder.Visibility = Visibility.Visible; }
    }
}
