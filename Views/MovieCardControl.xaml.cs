using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using CineLibraryCS.Models;
using CineLibraryCS.Services;
using System.Runtime.InteropServices.WindowsRuntime;

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
        Tapped += (_, e) =>
        {
            // Don't re-open if the tap originated inside the "View Details" button
            // (its Click already opens the dialog — without this guard we'd get two windows).
            // NOTE: we must walk the VISUAL tree (VisualTreeHelper.GetParent), not the logical
            // tree (.Parent). The TextBlock inside Button's ContentPresenter has Parent = null
            // across the template boundary, so .Parent-walking missed the Button.
            if (e.OriginalSource is DependencyObject src)
            {
                DependencyObject? cur = src;
                while (cur != null)
                {
                    if (cur is Button) { e.Handled = true; return; }
                    cur = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(cur);
                }
            }
            OpenDetail();
        };
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

            var bmp = new BitmapImage { DecodePixelWidth = (int)Math.Max(120, GlobalCardWidth) };
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
