using Microsoft.UI;
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

public sealed partial class MovieRowControl : UserControl
{
    public static readonly DependencyProperty MovieProperty =
        DependencyProperty.Register(nameof(Movie), typeof(MovieListItem), typeof(MovieRowControl),
            new PropertyMetadata(null, OnMovieChanged));

    public MovieListItem? Movie
    {
        get => (MovieListItem?)GetValue(MovieProperty);
        set => SetValue(MovieProperty, value);
    }

    public event EventHandler? SidebarRefreshRequested;

    [DllImport("user32.dll")] private static extern short GetKeyState(int nVirtKey);
    private const int VK_CONTROL = 0x11, VK_SHIFT = 0x10;
    private static bool IsCtrlDown() => (GetKeyState(VK_CONTROL) & 0x8000) != 0;
    private static bool IsShiftDown() => (GetKeyState(VK_SHIFT) & 0x8000) != 0;

    public MovieRowControl()
    {
        InitializeComponent();
        var flyout = new MenuFlyout();
        flyout.Opening += (_, _) => RebuildContextFlyout(flyout);
        ContextFlyout = flyout;

        // v2.5 — draggable for bulk add-to-list (matches MovieCardControl).
        CanDrag = true;
        DragStarting += OnRowDragStarting;
    }

    private void OnRowDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (Movie == null) { args.Cancel = true; return; }
        var ids = MovieCardControl.ResolveSelectionForDrag?.Invoke(Movie)?.ToList()
                  ?? new List<int> { Movie.Id };
        if (ids.Count == 0) { args.Cancel = true; return; }
        args.Data.SetText(string.Join(",", ids));
        args.Data.Properties["cinelibrary/movie-ids"] = string.Join(",", ids);
        args.Data.RequestedOperation = DataPackageOperation.Link;
        args.AllowedOperations = DataPackageOperation.Link | DataPackageOperation.Copy;
        args.Data.Properties.Title = ids.Count == 1 ? Movie.Title : $"{ids.Count} movies";
    }

    private void RebuildContextFlyout(MenuFlyout flyout)
    {
        flyout.Items.Clear();
        if (Movie == null) return;

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
                var item = new ToggleMenuFlyoutItem { Text = ul.Name, IsChecked = membership.Contains(ul.Id) };
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
        flyout.Items.Add(listsSub);
    }

    private static void OnMovieChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not MovieRowControl c) return;
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
            WatchedBtn.Content = Movie.IsWatched ? "✓" : "○";
            WatchedBtn.Foreground = Movie.IsWatched
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x90, 0x90, 0xA0));
        }
        else if (e.PropertyName == nameof(MovieListItem.IsFavorite))
        {
            RowFav.Visibility = Movie.IsFavorite ? Visibility.Visible : Visibility.Collapsed;
            FavBtn.Content = Movie.IsFavorite ? "★" : "☆";
            FavBtn.Foreground = Movie.IsFavorite
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xF5, 0x9E, 0x0B))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x90, 0x90, 0xA0));
        }
        else if (e.PropertyName == nameof(MovieListItem.IsSelected))
        {
            ApplySelectionVisual();
        }
    }

    private void ApplySelectionVisual()
    {
        bool on = Movie?.IsSelected == true;
        RowBorder.BorderBrush = on
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BrandPurpleBrush"]
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    private void Populate(MovieListItem m)
    {
        RowTitle.Text = m.Title;
        RowMeta.Text = $"{m.Year?.ToString() ?? "—"}{(m.Runtime.HasValue ? $" · {m.Runtime}m" : "")}{(m.GenresCsv != null ? $" · {m.GenresCsv.Split(',')[0].Trim()}" : "")}";
        RowFav.Visibility = m.IsFavorite ? Visibility.Visible : Visibility.Collapsed;
        RowRating.Text = m.RatingText;

        DriveLabel.Text = m.DriveLabel ?? "";
        DriveDot.Fill = new SolidColorBrush(m.IsOnline
            ? Windows.UI.Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E)
            : Windows.UI.Color.FromArgb(0xFF, 0x6B, 0x72, 0x80));

        // Status badge
        if (m.IsMissing)
        {
            RowStatusBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0xEF, 0x44, 0x44));
            RowStatusText.Text = "MISSING";
            RowStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xEF, 0x44, 0x44));
        }
        else if (m.IsOnline)
        {
            RowStatusBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0x22, 0xC5, 0x5E));
            RowStatusText.Text = "ONLINE";
            RowStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E));
        }
        else
        {
            RowStatusBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0x6B, 0x72, 0x80));
            RowStatusText.Text = "OFFLINE";
            RowStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x6B, 0x72, 0x80));
        }

        WatchedBtn.Content = m.IsWatched ? "✓" : "○";
        WatchedBtn.Foreground = m.IsWatched
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E))
            : new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x90, 0x90, 0xA0));
        FavBtn.Content = m.IsFavorite ? "★" : "☆";
        FavBtn.Foreground = m.IsFavorite
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xF5, 0x9E, 0x0B))
            : new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x90, 0x90, 0xA0));

        ApplySelectionVisual();
        LoadThumbAsync(m.LocalPoster);
    }

    // See MovieCardControl.LoadPosterAsync — guards against recycled rows
    // receiving a stale late-completing image.
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
            if (myToken != _thumbLoadToken) return;
            if (bytes == null) return;

            var bmp = new BitmapImage { DecodePixelWidth = 44 };
            var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
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

    private void OnTapped(object sender, TappedRoutedEventArgs e)
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
            MovieCardControl.RaiseSelectionFromRow(Movie, ctrl, shift);
            e.Handled = true;
            return;
        }
        bool wasSelecting = MovieCardControl.CurrentSelectionCount > 0;
        MovieCardControl.RaiseSelectionFromRow(Movie, false, false);
        if (!wasSelecting) ScheduleSingleTapAction();
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (TapOriginatedInButton(e.OriginalSource as DependencyObject))
        {
            e.Handled = true; return;
        }
        _pendingSingleTap?.Cancel();
        _pendingSingleTap = null;
        _ = PlayMovieOrPromptOfflineAsync();
    }

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

    private async Task PlayMovieOrPromptOfflineAsync()
    {
        if (Movie == null) return;
        var connected = AppState.Instance.Connected;
        if (!connected.TryGetValue(Movie.VolumeSerial, out var letter))
        {
            await ShowOfflineDialog(Movie.Title, Movie.DriveLabel);
            return;
        }
        var detail = AppState.Instance.Db.GetMovieDetail(Movie.Id, connected);
        if (detail == null || detail.VideoFileRelPath == null || !detail.IsOnline)
        {
            await ShowOfflineDialog(Movie.Title, Movie.DriveLabel);
            return;
        }
        var videoPath = Path.Combine($"{letter}:\\",
            detail.VideoFileRelPath.Replace('/', '\\'));
        if (!File.Exists(videoPath))
        {
            await ShowOfflineDialog(Movie.Title, Movie.DriveLabel);
            return;
        }
        try
        {
            AppState.Instance.Db.MarkPlayed(Movie.Id);
            await Windows.System.Launcher.LaunchUriAsync(new Uri(videoPath));
            SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
        }
        catch { }
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

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
        => RowBorder.Background = new SolidColorBrush(ActualTheme == ElementTheme.Light
            ? Windows.UI.Color.FromArgb(0xFF, 0xEB, 0xEB, 0xFF)
            : Windows.UI.Color.FromArgb(0xFF, 0x1E, 0x1E, 0x2E));

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
        => RowBorder.Background = new SolidColorBrush(ActualTheme == ElementTheme.Light
            ? Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
            : Windows.UI.Color.FromArgb(0xFF, 0x14, 0x14, 0x1F));

    private void OnToggleWatched(object sender, RoutedEventArgs e)
    {
        if (Movie == null) return;
        AppState.Instance.Db.ToggleWatched(Movie.Id);
        Movie.IsWatched = !Movie.IsWatched;
        WatchedBtn.Content = Movie.IsWatched ? "✓" : "○";
        WatchedBtn.Foreground = Movie.IsWatched
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E))
            : new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x90, 0x90, 0xA0));
    }

    private void OnToggleFav(object sender, RoutedEventArgs e)
    {
        if (Movie == null) return;
        AppState.Instance.Db.ToggleFavorite(Movie.Id);
        Movie.IsFavorite = !Movie.IsFavorite;
        FavBtn.Content = Movie.IsFavorite ? "★" : "☆";
        FavBtn.Foreground = Movie.IsFavorite
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xF5, 0x9E, 0x0B))
            : new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x90, 0x90, 0xA0));
    }

    private void OpenDetail()
    {
        if (Movie == null) return;
        var win = new MovieDetailDialog(Movie.Id);
        win.WatchlistChanged += (s, e) => SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
        win.Activate();
    }
}
