using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using CineLibraryCS.Models;
using CineLibraryCS.Services;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.DataTransfer;

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
    public event EventHandler<MovieListItem>? WatchlistToggleRequested;

    // ── Multi-select (v2.5) ────────────────────────────────────────────────
    // LibraryPage subscribes to these statics so it doesn't have to wire up
    // every recycled card. Same pattern as GlobalSizeChanged above.

    public record SelectionInteractionArgs(MovieListItem Movie, bool Ctrl, bool Shift);
    public static event EventHandler<SelectionInteractionArgs>? AnyCardSelectionInteraction;

    /// <summary>List view rows fire the same event so LibraryPage has one
    /// place to manage selection regardless of grid vs list mode.</summary>
    public static void RaiseSelectionFromRow(MovieListItem m, bool ctrl, bool shift)
        => AnyCardSelectionInteraction?.Invoke(null, new SelectionInteractionArgs(m, ctrl, shift));

    /// <summary>
    /// LibraryPage installs this so a drag carries every selected card's id
    /// (and selects the dragged card if it wasn't already selected). Falls
    /// back to a single-id drag when the host doesn't override.
    /// </summary>
    public static Func<MovieListItem, IEnumerable<int>>? ResolveSelectionForDrag;

    /// <summary>
    /// Host (LibraryPage) updates this whenever the selection set changes.
    /// Cards read it so a plain click in selection mode clears the set
    /// without also opening the detail dialog.
    /// </summary>
    public static int CurrentSelectionCount;

    [DllImport("user32.dll")] private static extern short GetKeyState(int nVirtKey);
    private const int VK_CONTROL = 0x11, VK_SHIFT = 0x10;
    private static bool IsCtrlDown() => (GetKeyState(VK_CONTROL) & 0x8000) != 0;
    private static bool IsShiftDown() => (GetKeyState(VK_SHIFT) & 0x8000) != 0;

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

        // Right-click → flyout with watched/favorite/watchlist + Add to list
        var flyout = new MenuFlyout();
        flyout.Opening += (_, _) => RebuildContextFlyout(flyout);
        ContextFlyout = flyout;

        PointerEntered += (_, _) =>
        {
            HoverOverlay.Visibility = Visibility.Visible;
            HoverOverlay.Opacity = 1;
            CardLift.Y = -4;
            CardScale.ScaleX = 1.025; CardScale.ScaleY = 1.025;
        };
        PointerExited += (_, _) =>
        {
            HoverOverlay.Opacity = 0;
            HoverOverlay.Visibility = Visibility.Collapsed;
            CardLift.Y = 0;
            CardScale.ScaleX = 1; CardScale.ScaleY = 1;
        };
        // Single tap → open details after a short delay (so a double-tap
        // gets a chance to suppress it). Double tap → play directly.
        // Ctrl/Shift+tap → route to LibraryPage for multi-select.
        Tapped += (_, e) =>
        {
            if (TapOriginatedInButton(e.OriginalSource as DependencyObject))
            {
                e.Handled = true; return;
            }
            if (Movie == null) return;
            bool ctrl = IsCtrlDown(), shift = IsShiftDown();
            if (ctrl || shift)
            {
                _pendingSingleTap?.Cancel();
                _pendingSingleTap = null;
                AnyCardSelectionInteraction?.Invoke(this,
                    new SelectionInteractionArgs(Movie, ctrl, shift));
                e.Handled = true;
                return;
            }
            // Plain tap. If a selection is active, exit selection mode
            // instead of opening details — user typically wants to "get
            // out of multi-select", not jump into a single movie.
            bool wasSelecting = CurrentSelectionCount > 0;
            AnyCardSelectionInteraction?.Invoke(this,
                new SelectionInteractionArgs(Movie, false, false));
            if (!wasSelecting) ScheduleSingleTapAction();
        };
        DoubleTapped += (_, e) =>
        {
            if (TapOriginatedInButton(e.OriginalSource as DependencyObject))
            {
                e.Handled = true; return;
            }
            _pendingSingleTap?.Cancel();
            _pendingSingleTap = null;
            _ = PlayMovieOrPromptOfflineAsync();
        };

        // Drag-and-drop source — every card is draggable so the user can
        // throw selected movies onto a sidebar list or onto Favorites /
        // Watchlist / Watched. Data payload is a comma-separated movie-id
        // list; LibraryPage decides whether to drag just this card or the
        // whole selected set via ResolveSelectionForDrag.
        CanDrag = true;
        DragStarting += OnCardDragStarting;
    }

    private void OnCardDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (Movie == null) { args.Cancel = true; return; }
        var ids = ResolveSelectionForDrag?.Invoke(Movie)?.ToList() ?? new List<int> { Movie.Id };
        if (ids.Count == 0) { args.Cancel = true; return; }
        args.Data.SetText(string.Join(",", ids));
        args.Data.Properties["cinelibrary/movie-ids"] = string.Join(",", ids);
        args.Data.RequestedOperation = DataPackageOperation.Link;
        args.AllowedOperations = DataPackageOperation.Link | DataPackageOperation.Copy;
        // Friendly drag glyph caption — "3 movies" when bulk, title when one.
        args.Data.Properties.Title = ids.Count == 1
            ? Movie.Title
            : $"{ids.Count} movies";
    }

    private void OnGlobalSizeChanged(object? s, EventArgs e) => ApplySize();

    private void ApplySize()
    {
        CardRoot.Width = GlobalCardWidth;
        CardRoot.Height = GlobalCardHeight;
    }

    private static void OnMovieChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not MovieCardControl c) return;
        // Detach from previous binding so recycled cards don't fire on stale items
        if (e.OldValue is MovieListItem prev)
            prev.PropertyChanged -= c.OnMoviePropertyChanged;
        if (e.NewValue is MovieListItem m)
        {
            c.Populate(m);
            m.PropertyChanged += c.OnMoviePropertyChanged;
        }
    }

    private void OnMoviePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (Movie == null) return;
        if (e.PropertyName == nameof(MovieListItem.IsWatched))
        {
            WatchedBadge.Visibility = Movie.IsWatched ? Visibility.Visible : Visibility.Collapsed;
            WatchedToggleBtn.Content = Movie.IsWatched ? "✓ Watched" : "○ Mark Watched";
        }
        else if (e.PropertyName == nameof(MovieListItem.IsFavorite))
        {
            FavBadge.Visibility = Movie.IsFavorite ? Visibility.Visible : Visibility.Collapsed;
        }
        else if (e.PropertyName == nameof(MovieListItem.IsWatchlist))
        {
            WatchlistToggleBtn.Content = Movie.IsWatchlist ? "📌 In Watchlist" : "📋 Watchlist";
        }
        else if (e.PropertyName == nameof(MovieListItem.IsSelected))
        {
            ApplySelectionVisual();
        }
    }

    private void ApplySelectionVisual()
    {
        bool on = Movie?.IsSelected == true;
        SelectedOutline.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        SelectedTick.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
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
        WatchlistToggleBtn.Content = m.IsWatchlist ? "📌 In Watchlist" : "📋 Watchlist";

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

        ApplySelectionVisual();
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

            // Decode at ~2× card width so the poster stays crisp on
            // HiDPI displays (150 / 175 / 200% scaling) without bloating
            // memory — we cap at 500 px which is well under typical
            // cached poster source size (~780).
            var decodeW = (int)Math.Min(500, Math.Max(240, GlobalCardWidth * 2));
            var bmp = new BitmapImage { DecodePixelWidth = decodeW };
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

    private void OnWatchlistToggle(object sender, RoutedEventArgs e)
    {
        if (Movie == null) return;
        WatchlistToggleRequested?.Invoke(this, Movie);
        WatchlistToggleBtn.Content = Movie.IsWatchlist ? "📌 In Watchlist" : "📋 Watchlist";
    }

    private void OpenDetail()
    {
        if (Movie == null) return;
        var win = new MovieDetailDialog(Movie.Id);
        win.WatchlistChanged += (s, e) => SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
        win.Activate();
    }

    /// <summary>
    /// Check if a routed tap origin is inside any Button — used to avoid
    /// double-open when the user clicks the embedded "View Details" etc.
    /// </summary>
    private static bool TapOriginatedInButton(DependencyObject? src)
    {
        var cur = src;
        while (cur != null)
        {
            if (cur is Button) return true;
            cur = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(cur);
        }
        return false;
    }

    // Single-tap deferral so a double-tap can cancel it before details open.
    // 220 ms matches Windows' default double-click threshold closely enough
    // that intentional double-taps reliably suppress the single-tap action,
    // while single-tap latency stays imperceptible.
    private CancellationTokenSource? _pendingSingleTap;

    private void ScheduleSingleTapAction()
    {
        _pendingSingleTap?.Cancel();
        var cts = new CancellationTokenSource();
        _pendingSingleTap = cts;
        var dq = DispatcherQueue;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(220, cts.Token); }
            catch (OperationCanceledException) { return; }
            if (cts.IsCancellationRequested) return;
            dq.TryEnqueue(() =>
            {
                if (cts.IsCancellationRequested) return;
                OpenDetail();
            });
        });
    }

    /// <summary>
    /// Play the movie via the OS default player. Offline → friendly dialog
    /// telling the user which drive to plug in.
    /// </summary>
    private async Task PlayMovieOrPromptOfflineAsync()
    {
        if (Movie == null) return;
        var connected = AppState.Instance.Connected;
        if (!connected.TryGetValue(Movie.VolumeSerial, out var letter))
        {
            await ShowOfflineDialog(Movie.Title, Movie.DriveLabel);
            return;
        }
        // Fetch detail row for video path
        var detail = AppState.Instance.Db.GetMovieDetail(Movie.Id, connected);
        if (detail == null || detail.VideoFileRelPath == null || !detail.IsOnline)
        {
            await ShowOfflineDialog(Movie.Title, Movie.DriveLabel);
            return;
        }
        var videoPath = System.IO.Path.Combine($"{letter}:\\",
            detail.VideoFileRelPath.Replace('/', '\\'));
        if (!System.IO.File.Exists(videoPath))
        {
            await ShowOfflineDialog(Movie.Title, Movie.DriveLabel);
            return;
        }
        try
        {
            AppState.Instance.Db.MarkPlayed(Movie.Id);
            await Windows.System.Launcher.LaunchUriAsync(new Uri(videoPath));
            // Bubble so sidebar Continue Watching count refreshes
            SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
        }
        catch { /* OS player launch failed — silent */ }
    }

    private async Task ShowOfflineDialog(string title, string? driveLabel)
    {
        var dlg = new ContentDialog
        {
            Title = "Can't play yet",
            Content = string.IsNullOrEmpty(driveLabel)
                ? $"\"{title}\" lives on a drive that isn't connected. Plug it in and try again."
                : $"\"{title}\" lives on \"{driveLabel}\", which isn't connected. Plug it in and try again.",
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
            RequestedTheme = MainWindow.CurrentTheme,
        };
        try { await dlg.ShowAsync(); } catch { }
    }

    /// <summary>
    /// Right-click flyout — built fresh on every open so list membership
    /// reflects current state. Mirrors what's in MovieDetailDialog so users
    /// don't have to open the detail window for common actions.
    /// </summary>
    private void RebuildContextFlyout(MenuFlyout flyout)
    {
        flyout.Items.Clear();
        if (Movie == null) return;

        // Watched / Favorite / Watchlist toggles
        var watchedItem = new ToggleMenuFlyoutItem
        {
            Text = "Watched",
            IsChecked = Movie.IsWatched,
        };
        watchedItem.Click += (_, _) => WatchedToggleRequested?.Invoke(this, Movie);
        flyout.Items.Add(watchedItem);

        var favItem = new ToggleMenuFlyoutItem
        {
            Text = "Favorite",
            IsChecked = Movie.IsFavorite,
        };
        favItem.Click += (_, _) =>
        {
            AppState.Instance.Db.ToggleFavorite(Movie.Id);
            Movie.IsFavorite = !Movie.IsFavorite;
            SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
        };
        flyout.Items.Add(favItem);

        var watchlistItem = new ToggleMenuFlyoutItem
        {
            Text = "Watchlist",
            IsChecked = Movie.IsWatchlist,
        };
        watchlistItem.Click += (_, _) => WatchlistToggleRequested?.Invoke(this, Movie);
        flyout.Items.Add(watchlistItem);

        flyout.Items.Add(new MenuFlyoutSeparator());

        // Add to list submenu
        var listsSub = new MenuFlyoutSubItem { Text = "📑 Add to list" };
        var allLists = AppState.Instance.Db.GetUserLists();
        var membership = AppState.Instance.Db.GetUserListsForMovie(Movie.Id);
        if (allLists.Count == 0)
        {
            listsSub.Items.Add(new MenuFlyoutItem { Text = "(no lists yet)", IsEnabled = false });
        }
        else
        {
            foreach (var ul in allLists)
            {
                var inList = membership.Contains(ul.Id);
                var item = new ToggleMenuFlyoutItem { Text = ul.Name, IsChecked = inList };
                var capturedUl = ul;
                item.Click += (_, _) =>
                {
                    if (item.IsChecked)
                        AppState.Instance.Db.AddMovieToUserList(capturedUl.Id, Movie.Id);
                    else
                        AppState.Instance.Db.RemoveMovieFromUserList(capturedUl.Id, Movie.Id);
                    SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
                };
                listsSub.Items.Add(item);
            }
        }
        listsSub.Items.Add(new MenuFlyoutSeparator());
        var newListItem = new MenuFlyoutItem { Text = "+ New list…" };
        newListItem.Click += async (_, _) =>
        {
            var name = await PromptNewListNameDialog();
            if (string.IsNullOrWhiteSpace(name) || Movie == null) return;
            try
            {
                var newId = AppState.Instance.Db.CreateUserList(name.Trim());
                AppState.Instance.Db.AddMovieToUserList(newId, Movie.Id);
                SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
            }
            catch (Microsoft.Data.Sqlite.SqliteException) { /* dup name */ }
        };
        listsSub.Items.Add(newListItem);
        flyout.Items.Add(listsSub);

        flyout.Items.Add(new MenuFlyoutSeparator());
        var openItem = new MenuFlyoutItem { Text = "Open details" };
        openItem.Click += (_, _) => OpenDetail();
        flyout.Items.Add(openItem);
    }

    private async Task<string?> PromptNewListNameDialog()
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
        box.Loaded += (_, _) => box.Focus(FocusState.Programmatic);
        var result = await dlg.ShowAsync();
        return result == ContentDialogResult.Primary ? box.Text : null;
    }
}
