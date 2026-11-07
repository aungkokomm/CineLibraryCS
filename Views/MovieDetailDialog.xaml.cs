using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using CineLibraryCS.Models;
using CineLibraryCS.Services;
using Windows.Graphics;
using Windows.System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;

namespace CineLibraryCS.Views;

// A resizable Window-based detail view (replaces the old fixed ContentDialog).
public sealed partial class MovieDetailDialog : Window
{
    private int _movieId;
    private MovieDetail? _movie;
    private bool _closed;

    // v4.5.0: the movies of the grid this window was opened from, in order, so
    // ← and → step through them in place. _browseIndex is the last of them shown.
    private IReadOnlyList<int>? _browseIds;
    private int _browseIndex = -1;
    // Each load of a movie takes a new token; an older load (or its pictures)
    // that finishes later stands down instead of showing the wrong movie.
    private int _loadToken;

    /// <summary>
    /// v4.5.0: set by a page whose grid loads in pages (All movies loads 60 at a
    /// time). Given the grid a window was opened from, it returns every movie of
    /// that view, or null when the grid is already whole or isn't its own.
    /// </summary>
    public static Func<System.Collections.IEnumerable, Task<List<int>>?>? WholeGrid { get; set; }

    public event EventHandler? WatchlistChanged;

    /// <param name="grid">The movies of the grid or list it was opened from, for ← and →.</param>
    public MovieDetailDialog(int movieId, System.Collections.IEnumerable? grid = null)
    {
        _movieId = movieId;
        var ids = grid?.OfType<MovieListItem>().Select(x => x.Id).ToList();
        if (ids is { Count: > 1 } && IndexOf(ids, movieId) is var at and >= 0)
        {
            _browseIds = ids;
            _browseIndex = at;
            if (WholeGrid?.Invoke(grid!) is { } whole) _ = UseWholeGridAsync(whole);
        }
        InitializeComponent();
        TrailerBtn.Content = ActionLabel.Trailer();
        FolderBtn.Content = ActionLabel.OpenFolder();
        ListsBtn.Content = ActionLabel.AddToList();

        // Custom titlebar drag region + Mica
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop();

        // v4.8.0 (#25): the window takes the main window's place, so a movie and
        // back reads as one window: same position and size (maximised when it
        // is), no opening animation, and the main window hidden meanwhile (one
        // taskbar button). Closing it puts the main window back in its place.
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        appWindow.Title = "CineLibrary";
        int noAnimation = 1;
        DwmSetWindowAttribute(hwnd, DwmwaTransitionsForceDisabled, ref noAnimation, sizeof(int));
        var main = MainHwnd;
        var place = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
        if (main != IntPtr.Zero && IsWindowVisible(main))
        {
            // Maximised: the same, restoring to where the main window does. Otherwise
            // its rectangle as it is (a snapped window's isn't its restore size).
            var mainAw = App.MainWindow!.AppWindow;
            if (IsZoomed(main) && GetWindowPlacement(main, ref place)) SetWindowPlacement(hwnd, ref place);
            else appWindow.MoveAndResize(new RectInt32(mainAw.Position.X, mainAw.Position.Y, mainAw.Size.Width, mainAw.Size.Height));
        }
        else if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            presenter.Maximize();
        s_open++;
        HideMainSoon();
        Closed += (_, _) => GiveBackPlace();

        // Esc closes the dialog
        var escAcc = new Microsoft.UI.Xaml.Input.KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.Escape
        };
        escAcc.Invoked += (_, a) => { a.Handled = true; Close(); };
        RootGrid.KeyboardAccelerators.Add(escAcc);
        // No floating "Esc" key tip following the pointer (same as MainWindow).
        RootGrid.KeyboardAcceleratorPlacementMode = Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden;
        // v4.5.0: ← / → for the previous / next movie (not while typing a note or tag)
        RootGrid.AddHandler(UIElement.KeyDownEvent,
            new Microsoft.UI.Xaml.Input.KeyEventHandler(OnRootKeyDown), handledEventsToo: true);
        // v4.8.0: the mouse's back button goes back to the library too
        RootGrid.AddHandler(UIElement.PointerPressedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) =>
            {
                if (!e.GetCurrentPoint(RootGrid).Properties.IsXButton1Pressed) return;
                e.Handled = true;
                Close();
            }), handledEventsToo: true);

        // Inherit theme from main window
        if (RootGrid is FrameworkElement fe)
            fe.RequestedTheme = MainWindow.CurrentTheme;

        // Track close so async load that finishes after Esc doesn't try to
        // touch the destroyed window (COMException "operation identifier is
        // not valid").
        Closed += (_, _) => _closed = true;
        // 5.3.0: back here after a film played in another player
        Activated += (_, e) => { if (e.WindowActivationState != WindowActivationState.Deactivated) OfferFinished(); };

        // Start DB load IMMEDIATELY (don't wait for Activated). Overlapping
        // with the window animation eliminates the visible "empty window"
        // delay that made the dialog feel slow.
        _ = LoadAsync();
    }

    // ── v4.8.0 (#25): one window to the eye ──────────────────────────────

    // Movie windows open; the main window comes back when the last one closes.
    private static int s_open;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _hideMainTimer;

    private static IntPtr MainHwnd => App.MainWindow is { } mw ? WinRT.Interop.WindowNative.GetWindowHandle(mw) : IntPtr.Zero;

    /// <summary>Hides the main window once this one has had a moment to draw over it.</summary>
    private void HideMainSoon()
    {
        var ui = DispatcherQueue;
        var timer = _hideMainTimer = ui.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(250);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => ui.Post(() =>
        {
            if (!_closed && MainHwnd is var main && main != IntPtr.Zero) ShowWindow(main, SwHide);
        });
        timer.Start();
    }

    /// <summary>Shows the main window where this one is (moved, resized or maximised as it was), without its opening animation.</summary>
    private void GiveBackPlace()
    {
        if (--s_open > 0 || App.MainWindow is not { } mainWindow) return;
        var main = MainHwnd;
        int noAnimation = 1;
        DwmSetWindowAttribute(main, DwmwaTransitionsForceDisabled, ref noAnimation, sizeof(int));
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var place = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
        var got = GetWindowPlacement(hwnd, ref place);
        // Closed from the taskbar while minimised: come back as it was before
        var minimised = got && place.ShowCmd == SwShowMinimized;
        if (minimised)
            place.ShowCmd = (place.Flags & WpfRestoreToMaximized) != 0 ? SwShowMaximized : SwShowNormal;
        if (got && (minimised || place.ShowCmd == SwShowMaximized))
            SetWindowPlacement(main, ref place);
        else
        {
            if (IsZoomed(main)) ShowWindow(main, SwShowNormal);   // un-maximised in here
            mainWindow.AppWindow.MoveAndResize(new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height));
            ShowWindow(main, SwShow);
        }
        mainWindow.Activate();
        AnimateAgainLater(main);
    }

    /// <summary>Once the main window is back, its minimise and restore animate again.</summary>
    private static async void AnimateAgainLater(IntPtr main)
    {
        await Task.Delay(1000);
        int animate = 0;
        DwmSetWindowAttribute(main, DwmwaTransitionsForceDisabled, ref animate, sizeof(int));
    }

    private const int DwmwaTransitionsForceDisabled = 3;
    private const int SwHide = 0, SwShowNormal = 1, SwShowMinimized = 2, SwShowMaximized = 3, SwShow = 5;
    private const int WpfRestoreToMaximized = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public int Length, Flags, ShowCmd;
        public int MinX, MinY, MaxX, MaxY;
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr hWnd, ref WindowPlacement placement);
    [DllImport("user32.dll")] private static extern bool SetWindowPlacement(IntPtr hWnd, ref WindowPlacement placement);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmdShow);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private async Task LoadAsync()
    {
        var token = ++_loadToken;
        var id = _movieId;
        var (movie, collection, byDirector) = await Task.Run(() =>
        {
            var db = AppState.Instance.Db;
            var mv = db.GetMovieDetail(id, AppState.Instance.Connected);
            var dir = mv?.Directors.FirstOrDefault();
            return (mv, db.GetCollectionStrip(id), dir == null ? new List<RelatedMovie>() : db.GetMoviesByDirector(dir, id));
        });

        if (movie == null || _closed || token != _loadToken) return;
        _movie = movie;
        try
        {
            PopulateUi(_movie);
            PopulateRelated(_movie, collection, byDirector);
            _ = ReadFileLanguagesAsync(_movie);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Window was closed mid-populate — silently bail.
        }
    }

    private int _finishedId;

    /// <summary>5.3.0: back from a film played in another player, after most of its length: was it finished?
    /// Asked once, about that film, whichever film this page shows now.</summary>
    private void OfferFinished()
    {
        if (_closed || ExternalPlay.TakeDue() is not { } due) return;
        if (AppState.Instance.Db.GetMovieDetail(due.Id, AppState.Instance.Connected) is not { IsWatched: false }) return;   // marked meanwhile
        _finishedId = due.Id;
        FinishedText.Text = $"Finished {due.Title}?";
        FinishedBar.Visibility = Visibility.Visible;
    }

    private void OnFinishedYes(object sender, RoutedEventArgs e)
    {
        FinishedBar.Visibility = Visibility.Collapsed;
        ExternalPlay.MarkFinished(_finishedId);
        if (_movie?.Id == _finishedId)
        {
            _movie.IsWatched = true;
            _movie.PlayCount = AppState.Instance.Db.GetPlayCount(_movie.Id);
            _movie.LastPlayed = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            WatchedBtn.Content = ActionLabel.Watched(true);
            PopulateFacts(_movie);
        }
        WatchlistChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnFinishedNo(object sender, RoutedEventArgs e) => FinishedBar.Visibility = Visibility.Collapsed;

    /// <summary>
    /// 5.3.0: the languages inside the film's file, read once for each file (known by its size) while
    /// the page is open and the drive connected, then shown instead of the .nfo's when the tracks say.
    /// </summary>
    private async Task ReadFileLanguagesAsync(MovieDetail m)
    {
        if (!m.Playable || m.CurrentLetter == null || m.VideoFileRelPath == null) return;
        if (m.FileLanguagesSize is long read && (m.FileSizeBytes is not long size || size == read)) return;
        var path = Path.Combine($"{m.CurrentLetter}:\\", m.VideoFileRelPath.Replace('/', '\\'));
        var found = await Task.Run(async () =>
        {
            try
            {
                if (!File.Exists(path)) return null;
                var length = new FileInfo(path).Length;
                if (await Services.Playback.FileLanguages.ReadAsync(path) is not { } languages) return null;
                AppState.Instance.Db.SetFileLanguages(m.Id, languages.Audio, languages.Subtitles, length);
                return ((string? Audio, string? Subtitles, long Size)?)(languages.Audio, languages.Subtitles, length);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            {
                return null;
            }
        });
        if (found is not { } f || _closed) return;
        (m.FileAudioLanguages, m.FileSubtitleLanguages, m.FileLanguagesSize) = (f.Audio, f.Subtitles, f.Size);
        if (ReferenceEquals(_movie, m)) PopulateFacts(m);
    }

    private void PopulateUi(MovieDetail m)
    {
        UpdateBrowseNav();
        DetailTitle.Text = m.Title;

        if (m.OriginalTitle != null && m.OriginalTitle != m.Title)
        {
            OriginalTitle.Text = m.OriginalTitle;
            OriginalTitle.Visibility = Visibility.Visible;
        }
        if (m.Tagline != null)
        {
            Tagline.Text = m.Tagline;
            TaglineWrap.Visibility = Visibility.Visible;
        }

        // Sticky bar title (v2.3) — shown when scrolled past hero
        StickyTitle.Text = m.Title;

        // IMDb / TMDb top-right pill buttons (v2.3)
        ImdbLinkBtn.Visibility = !string.IsNullOrWhiteSpace(m.ImdbId) ? Visibility.Visible : Visibility.Collapsed;
        TmdbLinkBtn.Visibility = !string.IsNullOrWhiteSpace(m.TmdbId) ? Visibility.Visible : Visibility.Collapsed;

        // Chips
        ChipsPanel.Children.Clear();
        void AddChip(string text, string? bg = null)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(bg != null
                    ? Windows.UI.Color.FromArgb(0xFF,
                        Convert.ToByte(bg[1..3], 16), Convert.ToByte(bg[3..5], 16), Convert.ToByte(bg[5..7], 16))
                    : Windows.UI.Color.FromArgb(0xCC, 0x1E, 0x1E, 0x2E)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 3, 8, 3),
            };
            border.Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xE0, 0xE0, 0xE0))
            };
            ChipsPanel.Children.Add(border);
        }

        if (m.Rating.HasValue)
        {
            // Prominent bright-yellow IMDB rating chip (first, before year/runtime)
            var ratingBorder = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFA, 0xCC, 0x15)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 3, 10, 3),
            };
            ratingBorder.Child = new TextBlock
            {
                Text = $"★ {m.Rating:F1}",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x1F, 0x14, 0x00))
            };
            ChipsPanel.Children.Add(ratingBorder);
        }
        if (m.Year.HasValue) AddChip(m.Year.ToString()!);
        if (m.Runtime.HasValue) AddChip($"{m.Runtime} min");
        if (m.Mpaa != null) AddChip(m.Mpaa);
        // v4.6.0: Director's Cut, Extended, IMAX…; after the age rating (#25)
        if (m.Edition != null) AddChip(m.Edition, "#4C1D95");
        if (m.IsMissing) AddChip("MISSING", "#3A1010");

        // Drive
        DriveStatusDot.Fill = new SolidColorBrush(m.IsOnline
            ? Windows.UI.Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E)
            : Windows.UI.Color.FromArgb(0xFF, 0x6B, 0x72, 0x80));
        DriveText.Text = m.DriveLabel + (m.CurrentLetter != null ? $" ({m.CurrentLetter}:)" : "");

        // Actions
        PlayBtn.IsEnabled = m.Playable;
        TrailerBtn.Visibility = TrailerUri(m.Trailer) != null ? Visibility.Visible : Visibility.Collapsed;
        FolderBtn.IsEnabled = m.IsOnline;
        FavBtn.Content = ActionLabel.Favorite(m.IsFavorite);
        WatchedBtn.Content = ActionLabel.Watched(m.IsWatched);
        WatchlistBtn.Content = ActionLabel.ToWatch(m.IsWatchlist);


        // Notes
        UpdateNoteUi(viewing: true);

        // Plot (fall back to outline — many MediaElch NFOs use <outline> only)
        var plotText = m.Plot ?? m.Outline;
        if (!string.IsNullOrWhiteSpace(plotText))
        {
            PlotText.Text = plotText;
            PlotSection.Visibility = Visibility.Visible;
        }

        // Genres — clickable, filters library on click. v4.5.0 (#24): chips right
        // under the rating / year / runtime line, where TMDb shows them too.
        GenreChips.Children.Clear();
        GenreChips.Visibility = m.Genres.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var g in m.Genres)
        {
            var captured = g;
            var chip = new Button
            {
                Content = g,
                FontSize = 12,
                MinHeight = 0,
                Padding = new Thickness(10, 3, 10, 3),
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0xA7, 0x8B, 0xFA)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x80, 0xA7, 0x8B, 0xFA)),
            };
            chip.Click += (_, _) => NavigateAndClose(mw => mw.NavigateLibraryByGenre(captured));
            GenreChips.Children.Add(chip);
        }

        // v5.3.0: people and production, coloured by kind; every name a link to the library filtered by it
        // (writers too now). v4.5.0 (#24): the first four writers, all of them on hover when there are more.
        void Fill(StackPanel field, DetailField.Kind kind, string label, IReadOnlyList<string> names,
                  Action<MainWindow, string>? go, int max = int.MaxValue)
        {
            field.Children.Clear();
            if (names.Count == 0) return;
            field.Children.Add(DetailField.Field(kind, label,
                DetailField.Names(names, go == null ? null : n => NavigateAndClose(mw => go(mw, n)), max: max, hover: DetailField.Brush(kind))));
            field.Visibility = Visibility.Visible;
        }
        Fill(DirectorField, DetailField.Kind.People, m.Directors.Count > 1 ? "Directors" : "Director", m.Directors,
             (mw, d) => mw.NavigateLibraryByDirector(d));
        Fill(WriterField, DetailField.Kind.People, m.Writers.Count > 1 ? "Writers" : "Writer", m.Writers,
             (mw, w) => mw.NavigateLibraryByWriter(w), max: 4);
        Fill(StudioField, DetailField.Kind.Production, m.Studios.Count > 1 ? "Studios" : "Studio", m.Studios,
             (mw, s) => mw.NavigateLibraryByStudio(s));
        Fill(CountryField, DetailField.Kind.Production, m.Countries.Count > 1 ? "Countries" : "Country", m.Countries, null);
        ArrangeFields(FieldsGrid.ActualWidth);

        // Tech badges (v2.2); v5.3.0: ratings, about the film, the file and the IDs in one card
        PopulateTechBadges(m);
        PopulateFacts(m);
        // Lists this movie is on (v2.5) — chip row with per-list ✕.
        RefreshListChips();
        // v2.9 — Free-form tags chip row + autocomplete.
        RefreshTagChips();

        // Cast — show the list right away; trickle thumbnail loads in
        // batches so the first paint isn't blocked by many parallel image
        // resolves. Pass the absolute movie folder so .actors/ thumbs are
        // discovered for online drives.
        if (m.Actors.Count > 0)
        {
            CastSection.Visibility = Visibility.Visible;
            CastDivider.Visibility = Visibility.Visible;
            // v4.4.1: the whole cast now; a very big one (up to a few hundred)
            // shows its first 24 until Show all, so the page and the photo
            // loading stay reasonable.
            FitCastCardHeight(m.Actors);   // all of them, so Show all keeps the card height
            string? movieFolderAbs = null;
            if (m.IsOnline && m.CurrentLetter != null && m.FolderRelPath != null)
                movieFolderAbs = Path.Combine($"{m.CurrentLetter}:\\",
                    m.FolderRelPath.Replace('/', '\\'));
            _castFolderAbs = movieFolderAbs;
            CastRepeater.ItemsSource = null;
            ShowCast();
        }

        // Images
        // v4.2.0: decoded for the bigger poster and the full-width banner (the
        // window opens maximised, so its width is about the banner's in pixels)
        LoadImageAsync(m.LocalPoster, PosterImage, PosterPlaceholder, 520);
        LoadImageAsync(m.LocalFanart, HeroImage, null, Math.Clamp(AppWindow.Size.Width, 1920, 3840));

        // v3.4 — "Fetch missing info from TMDB" (and, later, Sync to drive).
        UpdateTmdbActions(m);
    }

    // ── v3.4 TMDB enrichment ──────────────────────────────────────────────────

    /// <summary>True when the movie is missing something a TMDB fetch can fill.</summary>
    private static bool HasGaps(MovieDetail m) =>
        string.IsNullOrWhiteSpace(m.LocalPoster) ||
        string.IsNullOrWhiteSpace(m.LocalFanart) ||
        (string.IsNullOrWhiteSpace(m.Plot) && string.IsNullOrWhiteSpace(m.Outline)) ||
        m.Actors.Count == 0 ||
        !m.Year.HasValue || !m.Runtime.HasValue || !m.Rating.HasValue ||
        string.IsNullOrWhiteSpace(m.Studio);

    private bool IsArchiveRecord(MovieDetail m) => m.VolumeSerial == "__archive__";

    /// <summary>
    /// The Fetch button is shown on EVERY movie — partial scrapes are common and
    /// a heuristic kept hiding it on movies that really did have gaps. On a fully
    /// complete movie a fetch is simply a harmless no-op (everything is fill-only).
    /// </summary>
    private void UpdateTmdbActions(MovieDetail m)
    {
        FetchTmdbBtn.Visibility = Visibility.Visible;
        // Persisting fetched data to the drive is handled by the Drives page's
        // existing "Sync state to drive" action, so there's no per-movie Sync
        // button here (avoids a third button for the same kind of action).
        SyncDriveBtn.Visibility = Visibility.Collapsed;
        // v4.6.0: a Watched & Gone record has no folder to read
        ReloadNfoBtn.Visibility = IsArchiveRecord(m) ? Visibility.Collapsed : Visibility.Visible;
        ReloadNfoBtn.IsEnabled = m.IsOnline;
        TmdbActionsRow.Visibility = Visibility.Visible;
        TmdbStatus.Visibility = Visibility.Collapsed;
    }

    private void TmdbBusyState(bool busy, string? status = null)
    {
        TmdbBusy.IsActive = busy;
        FetchTmdbBtn.IsEnabled = !busy;
        SyncDriveBtn.IsEnabled = !busy;
        ReloadNfoBtn.IsEnabled = !busy && _movie?.IsOnline == true;
        if (status != null) { TmdbStatus.Text = status; TmdbStatus.Visibility = Visibility.Visible; }
    }

    // ── v4.6.0: reload from the .nfo, trailer ────────────────────────────────

    // Set once the folder has been read again: pictures then skip the image
    // cache, which would keep showing a replaced actor photo.
    private bool _freshImages;

    private async void OnReloadNfo(object sender, RoutedEventArgs e)
    {
        if (_movie == null) return;
        var m = _movie;
        TmdbBusyState(true, "Reading the folder again…");
        try
        {
            var ok = await AppState.Instance.ReloadFolderAsync(m.VolumeSerial, m.FolderRelPath);
            if (_closed) return;
            if (!ok)
            {
                TmdbBusyState(false, "The movie's folder isn't there. Is its drive connected?");
                return;
            }
            _freshImages = true;
            ResetForAnotherMovie();
            await LoadAsync();
            if (_closed) return;
            TmdbBusyState(false, "Reloaded from the .nfo.");
            WatchlistChanged?.Invoke(this, EventArgs.Empty);   // the grid shows the new title / poster
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reload from .nfo failed: {ex.Message}");
            if (!_closed) TmdbBusyState(false, "Couldn't read the folder. Please try again.");
        }
    }

    private async void OnTrailer(object sender, RoutedEventArgs e)
    {
        if (TrailerUri(_movie?.Trailer) is not { } uri) return;
        try { await Launcher.LaunchUriAsync(uri); }
        catch
        {
            if (App.MainWindow is MainWindow mw) mw.ShowToast("Couldn't open the trailer. Is a web browser installed?");
        }
    }

    /// <summary>
    /// A web address for an .nfo's &lt;trailer&gt;: itself when it's one, or the
    /// YouTube page for Kodi's YouTube add-on links
    /// (plugin://plugin.video.youtube/?action=play_video&amp;videoid=…). Null otherwise.
    /// </summary>
    internal static Uri? TrailerUri(string? trailer)
    {
        if (string.IsNullOrWhiteSpace(trailer)) return null;
        var t = trailer.Trim();
        if ((t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             t.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
            Uri.TryCreate(t, UriKind.Absolute, out var web))
            return web;
        var yt = System.Text.RegularExpressions.Regex.Match(t, @"video_?id=([\w-]{6,})",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return yt.Success ? new Uri("https://www.youtube.com/watch?v=" + yt.Groups[1].Value) : null;
    }

    private async void OnFetchMissing(object sender, RoutedEventArgs e)
    {
        if (_movie == null) return;
        var m = _movie;
        using var client = new Services.Tmdb.TmdbClient();

        TmdbBusyState(true, "Looking up TMDb…");
        try
        {
            // Match: stored tmdb_id is exact; otherwise let the user confirm.
            Services.Tmdb.TmdbMovie? d = null;
            if (int.TryParse(m.TmdbId, out var tid) && tid > 0)
            {
                d = await client.GetMovieDetailsAsync(tid);
            }
            else
            {
                var picker = new TmdbPickerDialog(client, m.Title, m.Year)
                {
                    XamlRoot = (Content as FrameworkElement)?.XamlRoot
                };
                var pick = await picker.ShowAsync();
                if (pick != ContentDialogResult.Primary || picker.Picked == null)
                {
                    TmdbBusyState(false);
                    UpdateTmdbActions(m);
                    return;
                }
                d = await client.GetMovieDetailsAsync(picker.Picked.TmdbId);
            }

            if (d == null)
            {
                TmdbBusyState(false, "Couldn't reach TMDb. Try again.");
                return;
            }

            // v4.8.0: the fill itself is shared with the selection bar's bulk fetch
            var saved = await Services.MovieFetch.ApplyAsync(client, d, m, status => TmdbBusyState(true, status));

            // Reload from the DB and re-render in place.
            TmdbBusyState(false, "Updated.");
            await LoadAsync();
            if (_closed) return;
            TmdbBusyState(false, saved switch
            {
                AppState.DriveSave.Saved   => "Updated, and saved to the movie's folder.",
                AppState.DriveSave.Waiting => "Updated. It goes onto the drive when you connect it.",
                _                          => "Updated.",
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Fetch missing info failed: {ex.Message}");
            TmdbBusyState(false, "Something went wrong. Please try again.");
        }
    }

    private void OnSyncToDrive(object sender, RoutedEventArgs e)
    {
        // Phase B — writes fetched NFO + poster + .actors back to the movie's
        // folder when its drive is online. Wired up in the next build.
    }

    /// <summary>
    /// v3.8.0: the cast grid gives every card the same height, so size it for
    /// the longest-wrapping name and role, measured with the user's text size.
    /// A fixed 280 cut off the role under a two-line name.
    /// </summary>
    private void FitCastCardHeight(IReadOnlyList<Models.Actor> actors)
    {
        var font = (FontFamily)Application.Current.Resources["ContentControlThemeFontFamily"];
        double TextHeight(string? text, double size, Windows.UI.Text.FontWeight weight)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var probe = new TextBlock
            {
                Text = text, FontFamily = font, FontSize = size, FontWeight = weight,
                TextWrapping = TextWrapping.WrapWholeWords, MaxLines = 2,
            };
            probe.Measure(new Windows.Foundation.Size(140, double.PositiveInfinity));
            return probe.DesiredSize.Height;
        }
        var name = actors.Max(a => TextHeight(a.Name, 13, Microsoft.UI.Text.FontWeights.SemiBold));
        var role = actors.Max(a => TextHeight(a.Role, 11, Microsoft.UI.Text.FontWeights.Normal));
        // 210 headshot + two 8 px gaps, plus a little slack for rounding.
        CastGridLayout.MinItemHeight = Math.Max(280, Math.Ceiling(210 + 8 + name + 8 + role + 4));
    }

    private static readonly string[] ActorThumbExts = { ".jpg", ".jpeg", ".png", ".tbn", ".webp" };

    /// <summary>
    /// Resolves an actor's thumbnail in priority order:
    ///   1. The movie folder's `.actors/` folder — MediaElch's standard
    ///      cache, named `First_Last.jpg` (underscores for spaces).
    ///   2. Inline `<thumb>` URL from the nfo (TMDb http URL or local path).
    /// Falls back to the initials TextBlock behind the Image when both miss.
    /// </summary>
    private static void LoadActorThumb(Models.Actor a, string? movieFolderAbs, bool fresh = false)
    {
        try
        {
            // 1) Local .actors folder
            if (!string.IsNullOrEmpty(movieFolderAbs))
            {
                var actorsDir = Path.Combine(movieFolderAbs, ".actors");
                if (Directory.Exists(actorsDir))
                {
                    var candidates = new[]
                    {
                        a.Name.Replace(' ', '_'),
                        a.Name,
                    };
                    foreach (var stem in candidates)
                    {
                        foreach (var ext in ActorThumbExts)
                        {
                            var p = Path.Combine(actorsDir, stem + ext);
                            if (File.Exists(p)) { ApplyBitmap(a, SafeFileUri(p), fresh); return; }
                        }
                    }
                }
            }

            // 2) Inline thumb — an http URL, an absolute file path, or (for
            //    manual Watched & Gone records) a path relative to the portable
            //    data folder, resolved the same way posters are.
            if (!string.IsNullOrWhiteSpace(a.Thumb))
            {
                var raw = a.Thumb!.Trim();
                Uri? uri = null;
                if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    uri = new Uri(raw);
                else if (File.Exists(raw))
                    uri = SafeFileUri(raw);
                else
                {
                    var cached = Services.AppState.Instance.Db.GetCachedImagePath(raw);
                    if (cached != null) uri = SafeFileUri(cached);
                }
                if (uri != null) ApplyBitmap(a, uri, fresh);
            }
        }
        catch { }
    }

    /// <summary>
    /// Build a file:// Uri that won't get its path lopped off by '#' or
    /// '?' in the file name. BitmapImage.UriSource treats those as URL
    /// fragment / query delimiters, so a poster called "Wall·E #1.jpg"
    /// silently fails to load otherwise.
    /// </summary>
    private static Uri SafeFileUri(string absolutePath)
    {
        // new Uri() already percent-encodes '#', '?' and spaces correctly for a
        // Windows path. Pre-escaping them ourselves double-encoded the '%' (a
        // "#Bollywood Movies" folder became %2523Bollywood…), which broke every
        // actor photo whose path contained '#' or '?'. Let .NET do the encoding.
        return new Uri(absolutePath);
    }

    private static void ApplyBitmap(Models.Actor a, Uri uri, bool fresh = false)
    {
        // 280 px decode = ~2× the 140-wide rendered slot for HiDPI crispness
        var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage { DecodePixelWidth = 280 };
        if (fresh) bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;   // v4.6.0: after a reload
        bmp.UriSource = uri;
        a.ThumbBitmap = bmp;
    }

    private const int CastPreview = 24;
    private string? _castFolderAbs;

    private const double LowWindow = 900;
    private bool _castAll;   // Show all was clicked, for this movie

    private void OnCastShowAll(object sender, RoutedEventArgs e)
    {
        _castAll = !_castAll;
        ShowCast();
        if (!_castAll) CastSection.StartBringIntoView();   // the page just got much shorter
    }

    /// <summary>
    /// 4.10.0 (#25): in a low window one row of cast, so what's under it is close. In
    /// a taller one the first 24 of a very big cast. Show all brings the rest in both.
    /// </summary>
    private void ShowCast()
    {
        if (_movie == null || _movie.Actors.Count == 0) return;
        var width = ContentScroller.ActualWidth - ContentScroller.Padding.Left - ContentScroller.Padding.Right;
        var across = (int)Math.Max(1, Math.Floor(width / (CastGridLayout.MinItemWidth + CastGridLayout.MinColumnSpacing)));
        var most = ContentScroller.ActualHeight < LowWindow ? across : CastPreview;
        var shown = _castAll || _movie.Actors.Count <= most ? _movie.Actors : _movie.Actors.Take(most).ToList();
        CastShowAllBtn.Content = _castAll ? "Show less" : $"Show all {_movie.Actors.Count}";
        CastShowAllBtn.Visibility = _movie.Actors.Count > most ? Visibility.Visible : Visibility.Collapsed;
        if (CastRepeater.ItemsSource is System.Collections.IList now && now.Count == shown.Count) return;
        CastRepeater.ItemsSource = shown;
        _ = LoadCastThumbsAsync(shown.Where(a => a.ThumbBitmap == null).ToList(), _castFolderAbs);
    }

    /// <summary>
    /// Trickle-loads actor thumbnails in small batches with a yield between
    /// each batch. Prevents many parallel image loads from blocking the
    /// first paint of the dialog.
    /// </summary>
    private async Task LoadCastThumbsAsync(IReadOnlyList<Models.Actor> actors, string? movieFolderAbs)
    {
        const int batchSize = 6;
        for (int i = 0; i < actors.Count; i++)
        {
            if (_closed) return;
            LoadActorThumb(actors[i], movieFolderAbs, _freshImages);
            if ((i + 1) % batchSize == 0)
                await Task.Delay(30);
        }
    }

    private async void LoadImageAsync(string? relPath, Image imgControl, FrameworkElement? placeholder, int decodeWidth)
    {
        var token = _loadToken;
        var bmp = await DecodeCachedAsync(relPath, decodeWidth);
        if (bmp == null || token != _loadToken) return;   // v4.5.0: or it's a movie ago
        imgControl.Source = bmp;
        if (placeholder != null) placeholder.Visibility = Visibility.Collapsed;
    }

    private static async Task<BitmapImage?> DecodeCachedAsync(string? relPath, int decodeWidth)
    {
        if (relPath == null) return null;
        var fullPath = AppState.Instance.Db.GetCachedImagePath(relPath);
        if (fullPath == null) return null;
        try
        {
            var bytes = await Task.Run(() => ImageCache.GetOrLoad(relPath!, fullPath));
            if (bytes == null) return null;
            var bmp = new BitmapImage { DecodePixelWidth = decodeWidth };
            using var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await ms.WriteAsync(bytes.AsBuffer());
            ms.Seek(0);
            await bmp.SetSourceAsync(ms);
            return bmp;
        }
        catch { return null; }
    }

    // ── v4.5.0: previous / next, and the films under the cast ──────────────

    private static int IndexOf(IReadOnlyList<int> ids, int id)
    {
        for (int i = 0; i < ids.Count; i++) if (ids[i] == id) return i;
        return -1;
    }

    // The grid's other pages arrive a moment after the window opens.
    private async Task UseWholeGridAsync(Task<List<int>> whole)
    {
        List<int> all;
        try { all = await whole; }
        catch { return; }
        if (_closed || _browseIds == null) return;
        var at = IndexOf(all, _browseIds[_browseIndex]);
        if (at < 0) return;
        _browseIds = all;
        _browseIndex = at;
        if (_movie != null) UpdateBrowseNav();
    }

    private void OnPrevMovie(object sender, RoutedEventArgs e) => StepMovie(-1);
    private void OnNextMovie(object sender, RoutedEventArgs e) => StepMovie(+1);

    private void OnRootKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key is not (VirtualKey.Left or VirtualKey.Right)) return;
        if (Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(RootGrid.XamlRoot)
            is TextBox or RichEditBox or AutoSuggestBox or ComboBox) return;
        e.Handled = true;
        StepMovie(e.Key == VirtualKey.Left ? -1 : +1);
    }

    private void StepMovie(int step)
    {
        if (_browseIds == null || TmdbBusy.IsActive) return;
        // Away from the grid (a film picked from a strip below), → goes on from
        // the last grid movie shown and ← goes back to it.
        var at = IndexOf(_browseIds, _movieId);
        var next = at >= 0 ? at + step : step > 0 ? _browseIndex + 1 : _browseIndex;
        if (next < 0 || next >= _browseIds.Count) return;
        ShowMovie(_browseIds[next]);
    }

    /// <summary>Shows another movie in this window.</summary>
    private void ShowMovie(int movieId)
    {
        if (movieId == _movieId || TmdbBusy.IsActive) return;
        _movieId = movieId;
        if (_browseIds != null && IndexOf(_browseIds, movieId) is var at and >= 0) _browseIndex = at;
        ResetForAnotherMovie();
        UpdateBrowseNav();
        _ = LoadAsync();
    }

    private void UpdateBrowseNav()
    {
        var show = _browseIds != null ? Visibility.Visible : Visibility.Collapsed;
        BrowseNav.Visibility = StickyBrowseNav.Visibility = show;
        if (_browseIds == null) return;
        var at = IndexOf(_browseIds, _movieId);
        BrowsePosPill.Visibility = at >= 0 ? Visibility.Visible : Visibility.Collapsed;
        BrowsePos.Text = $"{at + 1:N0} of {_browseIds.Count:N0}";
        bool canPrev = at >= 0 ? at > 0 : _browseIndex >= 0;
        bool canNext = (at >= 0 ? at : _browseIndex) + 1 < _browseIds.Count;
        PrevMovieBtn.IsEnabled = StickyPrevBtn.IsEnabled = canPrev;
        NextMovieBtn.IsEnabled = StickyNextBtn.IsEnabled = canNext;
        PrevMovieBtn.Opacity = StickyPrevBtn.Opacity = canPrev ? 1 : 0.4;   // the round style has no disabled look
        NextMovieBtn.Opacity = StickyNextBtn.Opacity = canNext ? 1 : 0.4;
    }

    // Puts back everything PopulateUi only ever switches on, so nothing of the
    // last movie stays on screen when the next one hasn't got it.
    private void ResetForAnotherMovie()
    {
        _movie = null;
        _castFolderAbs = null;
        _castAll = false;
        foreach (var e in new FrameworkElement[]
                 {
                     OriginalTitle, TaglineWrap, PlotSection, DirectorField, WriterField, StudioField, CountryField,
                     CastSection, CastDivider, CastShowAllBtn, TrailerBtn, CollectionSection, DirectorSection,
                     FactsCard, NotesCard, AddTagBox,
                 })
            e.Visibility = Visibility.Collapsed;
        AddTagBtn.Visibility = Visibility.Visible;
        foreach (var field in new[] { DirectorField, WriterField, StudioField, CountryField }) field.Children.Clear();
        FactsStack.Children.Clear();
        CastRepeater.ItemsSource = null;
        CollectionRepeater.ItemsSource = null;
        DirectorRepeater.ItemsSource = null;
        PosterImage.Source = null;
        PosterPlaceholder.Visibility = Visibility.Visible;
        HeroImage.Source = null;
        ContentScroller.ChangeView(null, 0, null, true);
    }

    private const int RelatedMax = 12;
    private string? _stripDirector;

    private void PopulateRelated(MovieDetail m, (string Name, List<RelatedMovie> Movies)? collection, List<RelatedMovie> byDirector)
    {
        var inCollection = new HashSet<int>();
        CollectionSection.Visibility = collection != null ? Visibility.Visible : Visibility.Collapsed;
        if (collection is { } c)
        {
            CollectionHeader.Text = c.Name.StartsWith("The ", StringComparison.OrdinalIgnoreCase)
                ? $"Part of {c.Name}" : $"Part of the {c.Name}";
            CollectionRepeater.ItemsSource = c.Movies;
            foreach (var x in c.Movies) inCollection.Add(x.Id);
            LoadRelatedPosters(c.Movies);
        }

        // Films already in the collection strip aren't repeated here
        var more = byDirector.Where(x => !inCollection.Contains(x.Id)).ToList();
        _stripDirector = m.Directors.FirstOrDefault();
        DirectorSection.Visibility = more.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (more.Count > 0)
        {
            DirectorHeader.Text = $"More by {_stripDirector}";
            var shown = more.Take(RelatedMax).ToList();
            DirectorRepeater.ItemsSource = shown;
            DirectorSeeAllBtn.Content = "See all";
            DirectorSeeAllBtn.Visibility = more.Count > shown.Count ? Visibility.Visible : Visibility.Collapsed;
            LoadRelatedPosters(shown);
        }
    }

    private async void LoadRelatedPosters(IReadOnlyList<RelatedMovie> movies)
    {
        var token = _loadToken;
        foreach (var r in movies)
        {
            if (token != _loadToken || _closed) return;
            var bmp = await DecodeCachedAsync(r.LocalPoster, 240);
            if (bmp != null && token == _loadToken) r.PosterBitmap = bmp;
        }
    }

    private void OnRelatedClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int id }) ShowMovie(id);
    }

    private void OnDirectorSeeAll(object sender, RoutedEventArgs e)
    {
        if (_stripDirector is { } d) NavigateAndClose(mw => mw.NavigateLibraryByDirector(d));
    }

    // ── Actions ───────────────────────────────────────────────────────────

    private void OnActorClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string actor)
            NavigateAndClose(mw => mw.NavigateLibraryByActor(actor));
    }

    /// <summary>
    /// Apply a library filter via MainWindow then close this detail window.
    /// </summary>
    private void NavigateAndClose(Action<MainWindow> nav)
    {
        if (App.MainWindow is MainWindow mw) nav(mw);
        Close();
    }

    // ── Tech badges, ratings, trailer, file info (v2.2) ──────────────────────

    private void PopulateTechBadges(MovieDetail m)
    {
        TechBadges.Items.Clear();
        // Resolution
        var resLabel = ResolutionLabel(m.VideoWidth, m.VideoHeight);
        if (resLabel != null) AddTechBadge(resLabel, "#3B82F6"); // blue
        // Aspect ratio
        var aspLabel = NormalizeAspect(m.VideoAspect);
        if (aspLabel != null) AddTechBadge(aspLabel, "#475569"); // slate
        // Video codec
        if (!string.IsNullOrWhiteSpace(m.VideoCodec))
            AddTechBadge(m.VideoCodec.ToUpperInvariant(), "#7C3AED"); // violet
        // HDR
        if (!string.IsNullOrWhiteSpace(m.HdrType))
            AddTechBadge(m.HdrType.ToUpperInvariant(), "#EAB308"); // amber
        // Audio codec + channels
        var aud = AudioLabel(m.AudioCodec, m.AudioChannels);
        if (aud != null) AddTechBadge(aud, "#10B981"); // emerald
    }

    private void AddTechBadge(string text, string hex)
    {
        // Gradient + thin top-highlight border for a polished embossed look
        var topColor = HexColor(hex);
        var bottomColor = DarkenColor(topColor, 0.85);
        var bg = new Microsoft.UI.Xaml.Media.LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(0, 1),
        };
        bg.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Color = topColor, Offset = 0 });
        bg.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Color = bottomColor, Offset = 1 });

        var border = new Border
        {
            CornerRadius = new CornerRadius(4),
            Background = bg,
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 3, 8, 3),
        };
        border.Child = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
        };
        TechBadges.Items.Add(border);
    }

    private static Windows.UI.Color HexColor(string hex)
    {
        if (hex.StartsWith("#")) hex = hex[1..];
        if (hex.Length == 6) hex = "FF" + hex;
        var a = Convert.ToByte(hex[..2], 16);
        var r = Convert.ToByte(hex.Substring(2, 2), 16);
        var g = Convert.ToByte(hex.Substring(4, 2), 16);
        var b = Convert.ToByte(hex.Substring(6, 2), 16);
        return Windows.UI.Color.FromArgb(a, r, g, b);
    }

    private static Windows.UI.Color DarkenColor(Windows.UI.Color c, double factor)
    {
        return Windows.UI.Color.FromArgb(c.A,
            (byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));
    }

    private static SolidColorBrush HexBrush(string hex)
    {
        if (hex.StartsWith("#")) hex = hex[1..];
        if (hex.Length == 6) hex = "FF" + hex;
        var a = Convert.ToByte(hex[..2], 16);
        var r = Convert.ToByte(hex.Substring(2, 2), 16);
        var g = Convert.ToByte(hex.Substring(4, 2), 16);
        var b = Convert.ToByte(hex.Substring(6, 2), 16);
        return new SolidColorBrush(Windows.UI.Color.FromArgb(a, r, g, b));
    }

    private static string? ResolutionLabel(int? w, int? h)
    {
        if (w == null && h == null) return null;
        // Some nfos record wrong values in <height> (especially for older
        // MediaElch scrapes). Use the larger dimension as the resolution
        // signal — for a landscape 1080p video, width=1920 is the source
        // of truth even if height got logged incorrectly.
        var px = Math.Max(w ?? 0, h ?? 0);
        // Width-based thresholds: 4K=3840, 1080p=1920, 720p=1280, SD<800
        return px switch
        {
            >= 3000 => "4K",
            >= 1700 => "HD 1080",
            >= 1100 => "HD 720",
            >= 600  => "SD",
            _       => null,
        };
    }

    private static string? NormalizeAspect(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!double.TryParse(raw, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v)) return raw;
        // Match well-known aspect ratios
        return v switch
        {
            >= 2.30 and <= 2.45 => "2.39:1",
            >= 2.20 and <= 2.30 => "2.21:1",
            >= 1.80 and <= 1.90 => "1.85:1",
            >= 1.75 and <= 1.80 => "16:9",
            >= 1.30 and <= 1.40 => "4:3",
            _ => $"{v:F2}:1",
        };
    }

    private static string? AudioLabel(string? codec, string? channels)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(codec)) parts.Add(codec.ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(channels))
        {
            var lbl = channels switch
            {
                "8" => "7.1",
                "7" => "6.1",
                "6" => "5.1",
                "2" => "Stereo",
                "1" => "Mono",
                _ => $"{channels} ch",
            };
            parts.Add(lbl);
        }
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    internal static string PrettySource(string src) => src.ToLowerInvariant() switch
    {
        "imdb" => "IMDb",
        "themoviedb" or "tmdb" => "TMDb",
        "rottentomatoes" or "tomatometerallcritics" => "Rotten Tomatoes",
        "tomatometerallaudience" => "RT Audience",
        "metacritic" => "Metacritic",
        "default" => "Rating",
        _ => char.ToUpper(src[0]) + src.Substring(1),
    };

    internal static string FormatVotes(int votes) => votes switch
    {
        >= 1_000_000 => $"{votes / 1_000_000.0:F1}M votes",
        >= 1_000     => $"{votes / 1_000.0:F1}K votes",
        _            => $"{votes} votes",
    };

    /// <summary>
    /// v5.3.0: a tile each for the ratings, about the film, the file and the IDs, where a strip mixed the
    /// film's facts with the file's. The length shows once: the file's own only when it differs from
    /// the .nfo's runtime by more than five minutes (another cut, or a wrong runtime).
    /// </summary>
    private void PopulateFacts(MovieDetail m)
    {
        FactsStack.Children.Clear();
        void Add(UIElement content) => FactsStack.Children.Add(DetailField.Tile(content));
        // v4.5.1 (#24): one source would only repeat the yellow chip
        if (m.AllRatings.Count >= 2 || m.UserRating != null)
            Add(DetailField.Ratings(m.AllRatings, m.UserRating));

        var film = new List<(string, string)>();
        if (m.Runtime is > 0) film.Add(("Runtime", $"{m.Runtime.Value} min"));
        if (!string.IsNullOrWhiteSpace(m.Premiered))
            film.Add(("Released", DateTime.TryParse(m.Premiered, out var dt) ? dt.ToString("MMM d, yyyy") : m.Premiered!));
        else if (m.Year.HasValue)
            film.Add(("Released", m.Year.Value.ToString()));
        if (!string.IsNullOrWhiteSpace(m.Mpaa)) film.Add(("Rated", m.Mpaa!));
        // 5.1.0 (#25): from the .nfo, and each time the player reaches the end
        if (m.PlayCount > 0) film.Add(("Played", m.PlayCount == 1 ? "1 time" : $"{m.PlayCount} times"));
        if (m.LastPlayed > 0) film.Add(("Last watched", DetailField.Day(m.LastPlayed)));   // 5.3.0: played or marked watched
        if (film.Count > 0) Add(DetailField.Section(DetailField.Kind.Story, "About the film", film));

        var file = new List<(string, string)>();
        if (m.DurationSeconds is > 0 && (m.Runtime is not > 0 || Math.Abs(m.Runtime.Value * 60 - m.DurationSeconds.Value) > 300))
            file.Add(("Length", FormatDuration(m.DurationSeconds.Value)));
        if (m.FileSizeBytes is > 0) file.Add(("Size", FormatBytes(m.FileSizeBytes.Value)));
        if (!string.IsNullOrWhiteSpace(m.ContainerExt)) file.Add(("Type", m.ContainerExt.ToUpperInvariant()));
        // 5.3.0: by name, and as VLC read them from the file when it could; else the .nfo's
        var audio = DetailField.LanguageNames(m.FileAudioLanguages ?? m.AudioLanguages);
        if (audio.Count > 0) file.Add(("Audio", string.Join(" · ", audio)));
        var subs = DetailField.LanguageNames(m.FileSubtitleLanguages ?? m.SubtitleLanguages);
        if (subs.Count > 0) file.Add(("Subtitles", subs.Count <= 3 ? string.Join(" · ", subs) : $"{subs.Count} languages"));
        if (file.Count > 0) Add(DetailField.Section(DetailField.Kind.File, "The file", file));

        var ids = new List<(string, string)>();
        if (!string.IsNullOrWhiteSpace(m.ImdbId)) ids.Add(("IMDb", m.ImdbId!));
        if (!string.IsNullOrWhiteSpace(m.TmdbId)) ids.Add(("TMDb", m.TmdbId!));
        if (ids.Count > 0) Add(DetailField.Ids(ids));
        FactsCard.Visibility = FactsStack.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string FormatBytes(long b)
    {
        const long KB = 1024L, MB = KB * 1024, GB = MB * 1024;
        if (b >= GB) return $"{b / (double)GB:F2} GB";
        if (b >= MB) return $"{b / (double)MB:F0} MB";
        if (b >= KB) return $"{b / (double)KB:F0} KB";
        return $"{b} B";
    }

    private static string FormatDuration(int seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}h {t.Minutes:D2}m"
            : $"{t.Minutes}m {t.Seconds:D2}s";
    }

    // v3.7.1: the content column gets an explicit width. With MaxWidth alone,
    // WinUI centred it by the width it asked for, so on a movie without fanart
    // (nothing asks for the full width) it was pushed right and cut off.
    // v4.2.0 (#14): no 1100 cap any more. The page fills the window and picks
    // an arrangement from its width, so a portrait tablet and a 4K screen both
    // use the space they have.
    private void OnContentScrollerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var inner = e.NewSize.Width - ContentScroller.Padding.Left - ContentScroller.Padding.Right;
        ContentStack.Width = Math.Max(0, inner);
        // 4.10.0 (#25): in a low window it gives way, so the poster, Play and the plot show without
        // scrolling. Not under 180: the poster rises 120 into it, up to the buttons on top of it.
        HeroBorder.Height = Math.Max(180, Math.Min(Math.Clamp(inner / 3.2, 220, 460), e.NewSize.Height - 480));   // 1100 wide = the old 340
        ApplyLayout(e.NewSize.Width < 900 ? DetailLayout.Narrow
                  : e.NewSize.Width >= 1600 ? DetailLayout.Wide
                  : DetailLayout.Standard);
        ShowCast();
    }

    private enum DetailLayout { Unset, Narrow, Standard, Wide }
    private DetailLayout _layout;

    private void ApplyLayout(DetailLayout layout)
    {
        if (layout == _layout) return;
        _layout = layout;
        bool narrow = layout == DetailLayout.Narrow, wide = layout == DetailLayout.Wide;

        PosterFrame.Width  = narrow ? 150 : wide ? 260 : 200;
        PosterFrame.Height = narrow ? 218 : wide ? 377 : 290;
        DetailTitle.FontSize   = narrow ? 26 : 34;
        DetailTitle.LineHeight = narrow ? 32 : 40;

        // Wide: everything up to the cast sits beside the poster. Buttons, plot and the people
        // go under the title; the fact tiles and notes get a column of their own. v5.3.0: otherwise,
        // under the poster, the tiles in a row where the strip was, then the plot, notes and people.
        foreach (var block in new FrameworkElement[] { ActionsBlock, FactsCard, PlotBlock, NotesCard, FieldsBlock })
            Detach(block);
        if (wide)
        {
            MetaStack.Children.Add(ActionsBlock);
            MetaStack.Children.Add(PlotBlock);
            MetaStack.Children.Add(FieldsBlock);
            SideStack.Children.Add(FactsCard);
            SideStack.Children.Add(NotesCard);
        }
        else
        {
            ContentStack.Children.Insert(ContentStack.Children.IndexOf(TopGrid) + 1, ActionsBlock);
            BodyMain.Children.Add(FactsCard);
            BodyMain.Children.Add(PlotBlock);
            BodyMain.Children.Add(NotesCard);
            BodyMain.Children.Add(FieldsBlock);
        }
        // At the top of the side column the tiles line up with the title's column
        FactsCard.Margin = new Thickness(0, wide ? 0 : 20, 0, 0);
        // The title column stops at a readable width; the details column takes the rest.
        var main = TopGrid.ColumnDefinitions[1];
        var side = TopGrid.ColumnDefinitions[2];
        main.Width = new GridLength(wide ? 3 : 1, GridUnitType.Star);
        main.MaxWidth = wide ? 900 : double.PositiveInfinity;
        side.Width = wide ? new GridLength(2, GridUnitType.Star) : new GridLength(0);
        side.MinWidth = wide ? 340 : 0;
    }

    private static void Detach(FrameworkElement element)
    {
        if (element.Parent is Panel parent) parent.Children.Remove(element);
    }

    private void OnFieldsGridSizeChanged(object sender, SizeChangedEventArgs e) => ArrangeFields(e.NewSize.Width);

    /// <summary>
    /// v5.3.0: the people and production fields in a row of four (director, writers, studios, country),
    /// two by two when the grid is narrower, one under another when narrow. Only those the film has,
    /// so none leaves a gap.
    /// </summary>
    private void ArrangeFields(double width)
    {
        int columns = width <= 0 || width >= 1000 ? 4 : width >= 520 ? 2 : 1;
        while (FieldsGrid.ColumnDefinitions.Count < columns) FieldsGrid.ColumnDefinitions.Add(new ColumnDefinition());
        while (FieldsGrid.ColumnDefinitions.Count > columns) FieldsGrid.ColumnDefinitions.RemoveAt(FieldsGrid.ColumnDefinitions.Count - 1);
        FieldsGrid.ColumnSpacing = columns > 1 ? 28 : 0;   // v4.5.0: a long Writer list ran into Studio
        var shown = FieldsGrid.Children.Cast<FrameworkElement>().Where(f => f.Visibility == Visibility.Visible).ToList();
        int rows = (shown.Count + columns - 1) / columns;
        while (FieldsGrid.RowDefinitions.Count < rows) FieldsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        while (FieldsGrid.RowDefinitions.Count > Math.Max(rows, 1)) FieldsGrid.RowDefinitions.RemoveAt(FieldsGrid.RowDefinitions.Count - 1);
        for (int i = 0; i < shown.Count; i++)
        {
            Grid.SetColumn(shown[i], i % columns);
            Grid.SetRow(shown[i], i / columns);
        }
        FieldsBlock.Visibility = shown.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Sticky action bar + external link buttons (v2.3) ─────────────────

    private void OnContentScrolled(object sender, ScrollViewerViewChangedEventArgs e)
    {
        // Bar appears once the user is past the hero block (its height follows the width)
        StickyBar.Visibility = ContentScroller.VerticalOffset > HeroBorder.Height - 60
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnBackToLibrary(object sender, RoutedEventArgs e) => Close();

    private async void OnOpenImdb(object sender, RoutedEventArgs e)
    {
        if (_movie?.ImdbId == null) return;
        try { await Launcher.LaunchUriAsync(new Uri($"https://www.imdb.com/title/{_movie.ImdbId}/")); }
        catch
        {
            if (App.MainWindow is MainWindow mw)
                mw.ShowToast("Couldn't open IMDb — no browser available?");
        }
    }

    private async void OnOpenTmdb(object sender, RoutedEventArgs e)
    {
        if (_movie?.TmdbId == null) return;
        try { await Launcher.LaunchUriAsync(new Uri($"https://www.themoviedb.org/movie/{_movie.TmdbId}")); }
        catch
        {
            if (App.MainWindow is MainWindow mw)
                mw.ShowToast("Couldn't open TMDb — no browser available?");
        }
    }

    private async void OnPlay(object sender, RoutedEventArgs e)
    {
        if (_movie?.CurrentLetter == null || _movie.VideoFileRelPath == null) return;
        var letter = _movie.CurrentLetter;
        var videoPath = Path.Combine($"{letter}:\\", _movie.VideoFileRelPath.Replace('/', '\\'));
        if (File.Exists(videoPath))
        {
            // Stamp Continue Watching: we can't see real playback position once
            // the OS player takes over, so the row simply moves to the top of
            // the Continue Watching list and stays until the user marks it Watched.
            AppState.Instance.Db.MarkPlayed(_movie.Id);
            _movie.LastPlayed = DateTimeOffset.UtcNow.ToUnixTimeSeconds();   // 5.3.0: Last watched
            PopulateFacts(_movie);
            // Tell the host so its sidebar badge updates
            WatchlistChanged?.Invoke(this, EventArgs.Empty);
            var movie = _movie;
            await VideoPlayer.PlayAsync(videoPath, new PlaybackItem(PlaybackKind.Movie, movie.Id, movie.Title), (_, watched) =>
            {
                // v4.10.0: the built-in player marks it watched near the end. v5.3.0: only on this film's
                // page; after stepping to another one, its count and buttons aren't this film's.
                if (_closed) return;
                var shown = ReferenceEquals(_movie, movie);
                movie.PlayCount = AppState.Instance.Db.GetPlayCount(movie.Id);
                if (shown) PopulateFacts(movie);
                if (!watched) return;
                movie.IsWatched = true;
                if (shown) WatchedBtn.Content = ActionLabel.Watched(true);
                WatchlistChanged?.Invoke(this, EventArgs.Empty);
            });
        }
    }

    private async void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        if (_movie?.CurrentLetter == null || _movie.FolderRelPath == null) return;
        var folderPath = Path.Combine($"{_movie.CurrentLetter}:\\", _movie.FolderRelPath.Replace('/', '\\'));
        if (Directory.Exists(folderPath))
            await Launcher.LaunchFolderPathAsync(folderPath);
    }

    private void OnToggleFav(object sender, RoutedEventArgs e)
    {
        if (_movie == null) return;
        AppState.Instance.Db.ToggleFavorite(_movie.Id);
        _movie.IsFavorite = !_movie.IsFavorite;
        FavBtn.Content = ActionLabel.Favorite(_movie.IsFavorite);
    }

         private void OnToggleWatched(object sender, RoutedEventArgs e)
         {
             if (_movie == null) return;
             AppState.Instance.Db.ToggleWatched(_movie.Id);
             _movie.IsWatched = !_movie.IsWatched;
             WatchedBtn.Content = ActionLabel.Watched(_movie.IsWatched);
             if (_movie.IsWatched) { _movie.LastPlayed = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); PopulateFacts(_movie); }   // 5.3.0: Last watched
         }

         private void OnToggleWatchlist(object sender, RoutedEventArgs e)
         {
             if (_movie == null) return;
             AppState.Instance.Db.SetWatchlist(_movie.Id, !_movie.IsWatchlist);
             _movie.IsWatchlist = !_movie.IsWatchlist;
             WatchlistBtn.Content = ActionLabel.ToWatch(_movie.IsWatchlist);
             WatchlistChanged?.Invoke(this, EventArgs.Empty);
         }

        // ── Lists chips (v2.5) — chip-per-list with ✕ to remove. Rebuilt
        //   on every list-membership change (flyout toggle / chip remove). ─

        private void RefreshListChips()
        {
            ListChipsRepeater.Items.Clear();
            if (_movie == null)
            {
                ListChipsRepeater.Visibility = Visibility.Collapsed;
                return;
            }
            var all = AppState.Instance.Db.GetUserLists().ToDictionary(u => u.Id, u => u.Name);
            var membership = AppState.Instance.Db.GetUserListsForMovie(_movie.Id);
            if (membership.Count == 0)
            {
                ListChipsRepeater.Visibility = Visibility.Collapsed;
                return;
            }
            ListChipsRepeater.Visibility = Visibility.Visible;
            foreach (var listId in membership)
            {
                if (!all.TryGetValue(listId, out var listName)) continue;
                ListChipsRepeater.Items.Add(BuildListChip(listId, listName));
            }
        }

        private UIElement BuildListChip(int listId, string listName)
        {
            var border = new Border
            {
                Background = CineLibraryCS.Services.ThemeBrushes.Get("CardBrush"),
                BorderBrush = CineLibraryCS.Services.ThemeBrushes.Get("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 4, 6, 4),
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            sp.Children.Add(new TextBlock
            {
                Text = $"📑 {listName}",
                FontSize = 12,
                Foreground = CineLibraryCS.Services.ThemeBrushes.Get("TextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            var x = new Button
            {
                Content = "✕",
                FontSize = 10,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Foreground = CineLibraryCS.Services.ThemeBrushes.Get("MutedBrush"),
                Padding = new Thickness(4, 0, 4, 0),
                MinWidth = 18,
                MinHeight = 18,
            };
            ToolTipService.SetToolTip(x, $"Remove from “{listName}”");
            x.Click += (_, _) =>
            {
                if (_movie == null) return;
                AppState.Instance.Db.RemoveMovieFromUserList(listId, _movie.Id);
                RefreshListChips();
                WatchlistChanged?.Invoke(this, EventArgs.Empty);
            };
            sp.Children.Add(x);
            border.Child = sp;
            return border;
        }

        // ── v2.9 Tags ────────────────────────────────────────────────────

        /// <summary>Rebuild the tag chip row from DB state. Mirrors RefreshListChips.</summary>
        private void RefreshTagChips()
        {
            TagChipsRepeater.Items.Clear();
            if (_movie == null) return;
            _movie.Tags = AppState.Instance.Db.GetTagNamesForMovie(_movie.Id);
            foreach (var name in _movie.Tags)
                TagChipsRepeater.Items.Add(BuildTagChip(name));
            // v4.6.0: then the .nfo's (less any that are also your own)
            NfoTagChipsRepeater.Items.Clear();
            _movie.NfoTags = AppState.Instance.Db.GetNfoTagNames(_movie.Id);
            foreach (var name in _movie.NfoTags)
                NfoTagChipsRepeater.Items.Add(BuildNfoTagChip(name, tag => NavigateAndClose(mw => mw.NavigateLibraryByTag(tag))));
        }

        /// <summary>
        /// v4.6.0: a tag from the .nfo, outlined and without a ✕ (it changes in
        /// the .nfo, e.g. in CineLibrary Essentials). A click filters by it.
        /// </summary>
        internal static UIElement BuildNfoTagChip(string tagName, Action<string> onClick,
            Microsoft.UI.Xaml.Media.Brush? border = null, Microsoft.UI.Xaml.Media.Brush? foreground = null)
        {
            var label = new HyperlinkButton
            {
                Content = tagName,
                FontSize = 12,
                Padding = new Thickness(0),
                MinHeight = 0,
            };
            if (foreground != null) label.Foreground = foreground;
            label.Click += (_, _) => onClick(tagName);
            var chip = new Border
            {
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderBrush = border ?? CineLibraryCS.Services.ThemeBrushes.Get("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 3, 10, 3),
                Child = label,
            };
            ToolTipService.SetToolTip(chip, "From the .nfo file");
            return chip;
        }

        private UIElement BuildTagChip(string tagName)
        {
            var border = new Border
            {
                Background = CineLibraryCS.Services.ThemeBrushes.Get("ChipBrush"),
                BorderBrush = CineLibraryCS.Services.ThemeBrushes.Get("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 3, 4, 3),
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            var label = new HyperlinkButton
            {
                Content = tagName,
                FontSize = 12,
                Padding = new Thickness(0),
                MinHeight = 0,
            };
            label.Click += (_, _) =>
            {
                if (_movie == null) return;
                NavigateAndClose(mw => mw.NavigateLibraryByTag(tagName));
            };
            sp.Children.Add(label);
            var x = new Button
            {
                Content = "✕",
                FontSize = 10,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Foreground = CineLibraryCS.Services.ThemeBrushes.Get("MutedBrush"),
                Padding = new Thickness(4, 0, 4, 0),
                MinWidth = 18,
                MinHeight = 18,
            };
            ToolTipService.SetToolTip(x, $"Remove tag “{tagName}”");
            x.Click += (_, _) =>
            {
                if (_movie == null) return;
                var tagId = AppState.Instance.Db.EnsureTag(tagName);  // safe lookup-or-create
                // RemoveMovieTag raises PersonalStateChanged → AppState
                // auto-writes the movie sidecar with current tags.
                AppState.Instance.Db.RemoveMovieTag(_movie.Id, tagId);
                RefreshTagChips();
                WatchlistChanged?.Invoke(this, EventArgs.Empty);
            };
            sp.Children.Add(x);
            border.Child = sp;
            return border;
        }

        private void OnAddTagClick(object sender, RoutedEventArgs e)
        {
            AddTagBtn.Visibility = Visibility.Collapsed;
            AddTagBox.Text = "";
            AddTagBox.Visibility = Visibility.Visible;
            AddTagBox.Focus(FocusState.Programmatic);
        }

        private void OnAddTagBlur(object sender, RoutedEventArgs e)
        {
            // Collapse the input when it loses focus without a submission.
            // Brief delay so a click on a suggestion has time to fire.
            DispatcherQueue.Post(() =>
            {
                if (!AddTagBox.FocusState.Equals(FocusState.Programmatic))
                {
                    AddTagBox.Visibility = Visibility.Collapsed;
                    AddTagBtn.Visibility = Visibility.Visible;
                }
            });
        }

        private void OnAddTagTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            var q = (sender.Text ?? "").Trim();
            if (q.Length == 0) { sender.ItemsSource = null; return; }
            var existing = AppState.Instance.Db.GetAllTags()
                .Where(t => t.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Name)
                .Take(8)
                .ToList();
            sender.ItemsSource = existing;
        }

        private void OnAddTagSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (_movie == null) return;
            var raw = args.ChosenSuggestion as string ?? sender.Text;
            var name = (raw ?? "").Trim();
            if (name.Length == 0) return;
            try
            {
                var tagId = AppState.Instance.Db.EnsureTag(name);
                // AddMovieTag raises PersonalStateChanged → AppState auto-
                // writes the movie sidecar with the new tag set.
                AppState.Instance.Db.AddMovieTag(_movie.Id, tagId);
                RefreshTagChips();
                WatchlistChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Add tag failed: {ex.Message}");
            }
            sender.Text = "";
            AddTagBox.Visibility = Visibility.Collapsed;
            AddTagBtn.Visibility = Visibility.Visible;
        }

        // ── Lists (v1.9.2) — add/remove this movie to/from any user list ─────

        private void OnListsFlyoutOpening(object sender, object e)
        {
            ListsFlyout.Items.Clear();
            if (_movie == null) return;

            var lists = AppState.Instance.Db.GetUserLists();
            var membership = AppState.Instance.Db.GetUserListsForMovie(_movie.Id);

            if (lists.Count == 0)
            {
                var none = new MenuFlyoutItem { Text = "(no lists yet)", IsEnabled = false };
                ListsFlyout.Items.Add(none);
            }
            else
            {
                foreach (var ul in lists)
                {
                    var inList = membership.Contains(ul.Id);
                    var item = new ToggleMenuFlyoutItem
                    {
                        Text = ul.Name,
                        IsChecked = inList,
                    };
                    var capturedUl = ul;
                    item.Click += (_, _) =>
                    {
                        if (item.IsChecked)
                            AppState.Instance.Db.AddMovieToUserList(capturedUl.Id, _movie.Id);
                        else
                            AppState.Instance.Db.RemoveMovieFromUserList(capturedUl.Id, _movie.Id);
                        // Tell host so MY LISTS counts refresh
                        WatchlistChanged?.Invoke(this, EventArgs.Empty);
                        RefreshListChips();
                    };
                    ListsFlyout.Items.Add(item);
                }
            }

            ListsFlyout.Items.Add(new MenuFlyoutSeparator());
            var newItem = new MenuFlyoutItem { Text = "+ New list…" };
            newItem.Click += async (_, _) =>
            {
                var name = await PromptNewListName();
                if (string.IsNullOrWhiteSpace(name) || _movie == null) return;
                try
                {
                    var listId = AppState.Instance.Db.CreateUserList(name.Trim());
                    AppState.Instance.Db.AddMovieToUserList(listId, _movie.Id);
                    WatchlistChanged?.Invoke(this, EventArgs.Empty);
                    RefreshListChips();
                }
                catch (Microsoft.Data.Sqlite.SqliteException)
                {
                    if (App.MainWindow is MainWindow mw)
                        mw.ShowToast($"A list named “{name.Trim()}” already exists");
                }
            };
            ListsFlyout.Items.Add(newItem);
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
                XamlRoot = RootGrid.XamlRoot,
                RequestedTheme = MainWindow.CurrentTheme,
            };
            box.Loaded += (_, _) => box.Focus(FocusState.Programmatic);
            var result = await dlg.ShowAsync();
            return result == ContentDialogResult.Primary ? box.Text : null;
        }

        // ── Notes (v1.9, hybrid DB + sidecar) ────────────────────────────────

        /// <summary>
        /// Renders the Notes panel in either viewing or editing mode based on
        /// _movie.Note. Called after load and after save/cancel.
        /// </summary>
        private void UpdateNoteUi(bool viewing)
        {
            if (_movie == null) return;
            var hasNote = !string.IsNullOrWhiteSpace(_movie.Note);
            // v5.3.0: "Add note" waits in the button row; the card is there with a note, or while writing one
            NoteAddBtn.Content ??= ActionLabel.Note("Add note");
            NotesCard.Visibility = viewing && !hasNote ? Visibility.Collapsed : Visibility.Visible;

            if (viewing)
            {
                NoteText.Text = _movie.Note ?? "";
                NoteText.Visibility = hasNote ? Visibility.Visible : Visibility.Collapsed;
                NoteEditor.Visibility = Visibility.Collapsed;
                NoteAddBtn.Visibility = hasNote ? Visibility.Collapsed : Visibility.Visible;
                NoteEditBtn.Visibility = hasNote ? Visibility.Visible : Visibility.Collapsed;
                NoteSaveBtn.Visibility = Visibility.Collapsed;
                NoteCancelBtn.Visibility = Visibility.Collapsed;
                NoteOfflineHint.Visibility = Visibility.Collapsed;
            }
            else
            {
                // Editing mode
                NoteEditor.Text = _movie.Note ?? "";
                NoteText.Visibility = Visibility.Collapsed;
                NoteEditor.Visibility = Visibility.Visible;
                NoteAddBtn.Visibility = Visibility.Collapsed;
                NoteEditBtn.Visibility = Visibility.Collapsed;
                NoteSaveBtn.Visibility = Visibility.Visible;
                NoteCancelBtn.Visibility = Visibility.Visible;
                NoteOfflineHint.Visibility = _movie.IsOnline ? Visibility.Collapsed : Visibility.Visible;
                NoteEditor.Focus(FocusState.Programmatic);
            }
        }

        private void OnNoteEditStart(object sender, RoutedEventArgs e) => UpdateNoteUi(viewing: false);

        private void OnNoteCancel(object sender, RoutedEventArgs e) => UpdateNoteUi(viewing: true);

        private void OnNoteSave(object sender, RoutedEventArgs e)
        {
            if (_movie == null) return;
            var newNote = (NoteEditor.Text ?? "").Trim();

            // 1) DB always succeeds (even when drive is offline)
            AppState.Instance.Db.SetNote(_movie.Id, newNote);
            _movie.Note = string.IsNullOrEmpty(newNote) ? null : newNote;

            // 2) Best-effort sidecar write next to the .nfo. Skipped silently
            //    when drive offline / read-only / network glitch.
            TryWriteSidecar(_movie, newNote);

            UpdateNoteUi(viewing: true);
        }

        private static void TryWriteSidecar(MovieDetail m, string note)
        {
            if (!m.IsOnline || m.CurrentLetter == null || m.FolderRelPath == null) return;
            try
            {
                var folder = Path.Combine($"{m.CurrentLetter}:\\", m.FolderRelPath.Replace('/', '\\'));
                if (!Directory.Exists(folder)) return;
                var sidecar = Path.Combine(folder, ScannerService.NoteSidecarFileName);
                if (string.IsNullOrEmpty(note))
                {
                    if (File.Exists(sidecar)) File.Delete(sidecar);
                }
                else
                {
                    File.WriteAllText(sidecar, note, System.Text.Encoding.UTF8);
                }
            }
            catch { /* sidecar is best-effort; DB is the source of truth */ }
        }
    }
