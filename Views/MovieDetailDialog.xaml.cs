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

    public event EventHandler? WatchlistChanged;

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

        // Ensure window is resizable and maximizable
        if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }

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
        WatchedBtn.Content = m.IsWatched ? "✓ Watched" : "○ Mark Watched";
        WatchlistBtn.Content = m.IsWatchlist ? "📌 In Watchlist" : "☐ Add to Watchlist";


        // Notes
        UpdateNoteUi(viewing: true);

        // Plot (fall back to outline — many MediaElch NFOs use <outline> only)
        var plotText = m.Plot ?? m.Outline;
        if (!string.IsNullOrWhiteSpace(plotText))
        {
            PlotText.Text = plotText;
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
            var bytes = await Task.Run(() => ImageCache.GetOrLoad(relPath!, fullPath));
            if (bytes == null) return;
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
        {
            // Stamp Continue Watching: we can't see real playback position once
            // the OS player takes over, so the row simply moves to the top of
            // the Continue Watching list and stays until the user marks it Watched.
            AppState.Instance.Db.MarkPlayed(_movie.Id);
            // Tell the host so its sidebar badge updates
            WatchlistChanged?.Invoke(this, EventArgs.Empty);
            await Launcher.LaunchUriAsync(new Uri(videoPath));
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
        FavBtn.Content = _movie.IsFavorite ? "★ Favorited" : "☆ Favorite";
    }

         private void OnToggleWatched(object sender, RoutedEventArgs e)
         {
             if (_movie == null) return;
             AppState.Instance.Db.ToggleWatched(_movie.Id);
             _movie.IsWatched = !_movie.IsWatched;
             WatchedBtn.Content = _movie.IsWatched ? "✓ Watched" : "○ Mark Watched";
         }

         private void OnToggleWatchlist(object sender, RoutedEventArgs e)
         {
             if (_movie == null) return;
             AppState.Instance.Db.SetWatchlist(_movie.Id, !_movie.IsWatchlist);
             _movie.IsWatchlist = !_movie.IsWatchlist;
             WatchlistBtn.Content = _movie.IsWatchlist ? "📌 In Watchlist" : "☐ Add to Watchlist";
             WatchlistChanged?.Invoke(this, EventArgs.Empty);
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
