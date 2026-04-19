using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using CineLibraryCS.Models;
using CineLibraryCS.Services;
using Windows.Graphics;
using Windows.System;
using System.Runtime.InteropServices.WindowsRuntime;

namespace CineLibraryCS.Views;

// A resizable Window-based detail view (replaces the old fixed ContentDialog).
public sealed partial class MovieDetailDialog : Window
{
    private readonly int _movieId;
    private MovieDetail? _movie;

    public MovieDetailDialog(int movieId)
    {
        _movieId = movieId;
        InitializeComponent();

        // Custom titlebar drag region + Mica
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop();

        // Default window size — big but not fullscreen; user can resize freely
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(1100, 800));
        appWindow.Title = "Movie Details";

        // Inherit theme from main window
        if (RootGrid is FrameworkElement fe)
            fe.RequestedTheme = MainWindow.CurrentTheme;

        Activated += async (_, _) =>
        {
            if (_movie == null) await LoadAsync();
        };
    }

    private async Task LoadAsync()
    {
        _movie = await Task.Run(() =>
            AppState.Instance.Db.GetMovieDetail(_movieId, AppState.Instance.Connected));

        if (_movie == null) return;
        PopulateUi(_movie);
    }

    private void PopulateUi(MovieDetail m)
    {
        Title = m.Title;
        TitleBarText.Text = m.Title;
        DetailTitle.Text = m.Title;

        if (m.OriginalTitle != null && m.OriginalTitle != m.Title)
        {
            OriginalTitle.Text = m.OriginalTitle;
            OriginalTitle.Visibility = Visibility.Visible;
        }
        if (m.Tagline != null)
        {
            Tagline.Text = $"\"{m.Tagline}\"";
            Tagline.Visibility = Visibility.Visible;
        }

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

        if (m.Year.HasValue) AddChip(m.Year.ToString()!);
        if (m.Runtime.HasValue) AddChip($"{m.Runtime} min");
        if (m.Mpaa != null) AddChip(m.Mpaa);
        if (m.Rating.HasValue) AddChip($"★ {m.Rating:F1}", "#1A1A00");
        if (m.IsMissing) AddChip("MISSING", "#3A1010");

        // Drive
        DriveStatusDot.Fill = new SolidColorBrush(m.IsOnline
            ? Windows.UI.Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E)
            : Windows.UI.Color.FromArgb(0xFF, 0x6B, 0x72, 0x80));
        DriveText.Text = m.DriveLabel + (m.CurrentLetter != null ? $" ({m.CurrentLetter}:)" : "");

        // Actions
        PlayBtn.IsEnabled = m.Playable;
        FolderBtn.IsEnabled = m.IsOnline;
        FavBtn.Content = m.IsFavorite ? "★ Favorited" : "☆ Favorite";
        WatchedBtn.Content = m.IsWatched ? "✓ Watched" : "○ Unwatched";

        // Plot
        if (m.Plot != null)
        {
            PlotText.Text = m.Plot;
            PlotText.Visibility = Visibility.Visible;
        }

        // Genres
        if (m.Genres.Count > 0)
        {
            GenresField.Visibility = Visibility.Visible;
            GenreLinks.Children.Clear();
            foreach (var g in m.Genres)
            {
                var btn = new HyperlinkButton { Content = g, Padding = new Thickness(0), FontSize = 13 };
                GenreLinks.Children.Add(btn);
            }
        }

        // Directors
        if (m.Directors.Count > 0)
        {
            DirectorField.Visibility = Visibility.Visible;
            DirectorLinks.Children.Clear();
            foreach (var d in m.Directors)
            {
                var btn = new HyperlinkButton { Content = d, Padding = new Thickness(0), FontSize = 13 };
                DirectorLinks.Children.Add(btn);
            }
        }

        // Studio
        if (m.Studio != null)
        {
            StudioLabel.Visibility = Visibility.Visible;
            StudioText.Text = m.Studio;
            StudioText.Visibility = Visibility.Visible;
        }

        // Cast
        if (m.Actors.Count > 0)
        {
            CastSection.Visibility = Visibility.Visible;
            CastRepeater.ItemsSource = m.Actors;
        }

        // Images
        LoadImageAsync(m.LocalPoster, PosterImage, PosterPlaceholder, 220);
        LoadImageAsync(m.LocalFanart, HeroImage, null, 1200);
    }

    private async void LoadImageAsync(string? relPath, Image imgControl, FrameworkElement? placeholder, int decodeWidth)
    {
        if (relPath == null) return;
        var fullPath = AppState.Instance.Db.GetCachedImagePath(relPath);
        if (fullPath == null) return;
        try
        {
            var bytes = await Task.Run(() => File.ReadAllBytes(fullPath));
            var bmp = new BitmapImage { DecodePixelWidth = decodeWidth };
            var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await ms.WriteAsync(bytes.AsBuffer());
            ms.Seek(0);
            await bmp.SetSourceAsync(ms);
            imgControl.Source = bmp;
            if (placeholder != null) placeholder.Visibility = Visibility.Collapsed;
        }
        catch { }
    }

    // ── Actions ───────────────────────────────────────────────────────────

    private async void OnPlay(object sender, RoutedEventArgs e)
    {
        if (_movie?.CurrentLetter == null || _movie.VideoFileRelPath == null) return;
        var letter = _movie.CurrentLetter;
        var videoPath = Path.Combine($"{letter}:\\", _movie.VideoFileRelPath.Replace('/', '\\'));
        if (File.Exists(videoPath))
            await Launcher.LaunchUriAsync(new Uri(videoPath));
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
        FavBtn.Content = _movie.IsFavorite ? "★ Favorited" : "☆ Favorite";
    }

    private void OnToggleWatched(object sender, RoutedEventArgs e)
    {
        if (_movie == null) return;
        AppState.Instance.Db.ToggleWatched(_movie.Id);
        _movie.IsWatched = !_movie.IsWatched;
        WatchedBtn.Content = _movie.IsWatched ? "✓ Watched" : "○ Unwatched";
    }
}
