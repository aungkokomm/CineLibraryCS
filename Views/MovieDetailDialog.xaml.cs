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
    private bool _closed;

    public event EventHandler? WatchlistChanged;

    public MovieDetailDialog(int movieId)
    {
        _movieId = movieId;
        InitializeComponent();

        // Custom titlebar drag region + Mica
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop();

        // Window size — restore the last user-resized size if we have one,
        // otherwise fall back to a comfortable default (1100×800). Saved on
        // SizeChanged into prefs so resize sticks across sessions.
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

        int width = 1100, height = 800;
        var savedW = AppState.Instance.GetPref("detailDialogW", "");
        var savedH = AppState.Instance.GetPref("detailDialogH", "");
        if (int.TryParse(savedW, out var w) && w >= 700) width = w;
        if (int.TryParse(savedH, out var h) && h >= 500) height = h;
        appWindow.Resize(new SizeInt32(width, height));
        appWindow.Title = "Movie Details";

        // Ensure window is resizable and maximizable, then open maximized
        if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.Maximize();
        }

        // Esc closes the dialog
        var escAcc = new Microsoft.UI.Xaml.Input.KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.Escape
        };
        escAcc.Invoked += (_, a) => { a.Handled = true; Close(); };
        RootGrid.KeyboardAccelerators.Add(escAcc);

        // Persist size on close — using AppWindow.Changed catches the final
        // resized state regardless of how the window was dismissed.
        appWindow.Changed += (s, args) =>
        {
            if (!args.DidSizeChange) return;
            try
            {
                AppState.Instance.SetPref("detailDialogW", s.Size.Width.ToString());
                AppState.Instance.SetPref("detailDialogH", s.Size.Height.ToString());
            }
            catch { }
        };

        // Inherit theme from main window
        if (RootGrid is FrameworkElement fe)
            fe.RequestedTheme = MainWindow.CurrentTheme;

        // Track close so async load that finishes after Esc doesn't try to
        // touch the destroyed window (COMException "operation identifier is
        // not valid").
        Closed += (_, _) => _closed = true;

        // Start DB load IMMEDIATELY (don't wait for Activated). Overlapping
        // with the window animation eliminates the visible "empty window"
        // delay that made the dialog feel slow.
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _movie = await Task.Run(() =>
            AppState.Instance.Db.GetMovieDetail(_movieId, AppState.Instance.Connected));

        if (_movie == null || _closed) return;
        try { PopulateUi(_movie); }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Window was closed mid-populate — silently bail.
        }
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
            PlotSection.Visibility = Visibility.Visible;
            PlotDivider.Visibility = Visibility.Visible;
        }

        // Genres — clickable, filters library on click
        if (m.Genres.Count > 0)
        {
            GenresField.Visibility = Visibility.Visible;
            GenreLinks.Children.Clear();
            foreach (var g in m.Genres)
            {
                var captured = g;
                var btn = new HyperlinkButton { Content = g, Padding = new Thickness(0), FontSize = 13 };
                btn.Click += (_, _) => NavigateAndClose(mw => mw.NavigateLibraryByGenre(captured));
                GenreLinks.Children.Add(btn);
            }
        }

        // Directors — clickable
        if (m.Directors.Count > 0)
        {
            DirectorField.Visibility = Visibility.Visible;
            DirectorLinks.Children.Clear();
            foreach (var d in m.Directors)
            {
                var captured = d;
                var btn = new HyperlinkButton { Content = d, Padding = new Thickness(0), FontSize = 13 };
                btn.Click += (_, _) => NavigateAndClose(mw => mw.NavigateLibraryByDirector(captured));
                DirectorLinks.Children.Add(btn);
            }
        }

        // Tech badges + ratings + file info (v2.2)
        PopulateTechBadges(m);
        PopulateRatingsPanel(m);
        PopulateFileInfo(m);

        // Studio — clickable HyperlinkButton (was a plain TextBlock)
        if (m.Studio != null)
        {
            StudioLabel.Visibility = Visibility.Visible;
            StudioText.Visibility = Visibility.Collapsed;
            StudioLink.Visibility = Visibility.Visible;
            StudioLink.Content = m.Studio;
            // Detach prior handler in case dialog is reused
            StudioLink.Click -= OnStudioClick;
            StudioLink.Click += OnStudioClick;
        }

        // Cast — show the list right away; trickle thumbnail loads in
        // batches so the first paint isn't blocked by many parallel image
        // resolves. Pass the absolute movie folder so .actors/ thumbs are
        // discovered for online drives.
        if (m.Actors.Count > 0)
        {
            CastSection.Visibility = Visibility.Visible;
            CastDivider.Visibility = Visibility.Visible;
            CastRepeater.ItemsSource = m.Actors;
            string? movieFolderAbs = null;
            if (m.IsOnline && m.CurrentLetter != null && m.FolderRelPath != null)
                movieFolderAbs = Path.Combine($"{m.CurrentLetter}:\\",
                    m.FolderRelPath.Replace('/', '\\'));
            _ = LoadCastThumbsAsync(m.Actors, movieFolderAbs);
        }

        // Images
        LoadImageAsync(m.LocalPoster, PosterImage, PosterPlaceholder, 220);
        LoadImageAsync(m.LocalFanart, HeroImage, null, 1200);
    }

    private static readonly string[] ActorThumbExts = { ".jpg", ".jpeg", ".png", ".tbn", ".webp" };

    /// <summary>
    /// Resolves an actor's thumbnail in priority order:
    ///   1. The movie folder's `.actors/` folder — MediaElch's standard
    ///      cache, named `First_Last.jpg` (underscores for spaces).
    ///   2. Inline `<thumb>` URL from the nfo (TMDb http URL or local path).
    /// Falls back to the initials TextBlock behind the Image when both miss.
    /// </summary>
    private static void LoadActorThumb(Models.Actor a, string? movieFolderAbs)
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
                            if (File.Exists(p)) { ApplyBitmap(a, new Uri(p)); return; }
                        }
                    }
                }
            }

            // 2) Inline thumb (URL or absolute file path)
            if (!string.IsNullOrWhiteSpace(a.Thumb))
            {
                var raw = a.Thumb!.Trim();
                Uri? uri = null;
                if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    uri = new Uri(raw);
                else if (File.Exists(raw))
                    uri = new Uri(raw);
                if (uri != null) ApplyBitmap(a, uri);
            }
        }
        catch { }
    }

    private static void ApplyBitmap(Models.Actor a, Uri uri)
    {
        // 280 px decode = ~2× the 140-wide rendered slot for HiDPI crispness
        var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage { DecodePixelWidth = 280 };
        bmp.UriSource = uri;
        a.ThumbBitmap = bmp;
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
            LoadActorThumb(actors[i], movieFolderAbs);
            if ((i + 1) % batchSize == 0)
                await Task.Delay(30);
        }
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

    private void OnStudioClick(object sender, RoutedEventArgs e)
    {
        if (_movie?.Studio == null) return;
        var s = _movie.Studio;
        NavigateAndClose(mw => mw.NavigateLibraryByStudio(s));
    }

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

    private void PopulateRatingsPanel(MovieDetail m)
    {
        RatingsPanel.Children.Clear();
        if (m.AllRatings.Count == 0)
        {
            RatingsPanel.Visibility = Visibility.Collapsed;
            return;
        }
        RatingsPanel.Visibility = Visibility.Visible;
        foreach (var rt in m.AllRatings)
        {
            var sourceLabel = PrettySource(rt.Source);
            var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(new TextBlock
            {
                Text = sourceLabel.ToUpperInvariant(),
                FontSize = 9, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                CharacterSpacing = 150, Opacity = 0.65,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["MutedBrush"],
            });
            stack.Children.Add(new TextBlock
            {
                Text = rt.Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                       + (rt.Source.Equals("rottentomatoes", StringComparison.OrdinalIgnoreCase) ? "%" : ""),
                FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextBrush"],
            });
            if (rt.Votes.HasValue && rt.Votes.Value > 0)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = FormatVotes(rt.Votes.Value),
                    FontSize = 10,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["MutedBrush"],
                });
            }
            RatingsPanel.Children.Add(stack);
        }
    }

    private static string PrettySource(string src) => src.ToLowerInvariant() switch
    {
        "imdb" => "IMDb",
        "themoviedb" or "tmdb" => "TMDb",
        "rottentomatoes" => "Rotten Tomatoes",
        "metacritic" => "Metacritic",
        "default" => "Rating",
        _ => char.ToUpper(src[0]) + src.Substring(1),
    };

    private static string FormatVotes(int votes) => votes switch
    {
        >= 1_000_000 => $"{votes / 1_000_000.0:F1}M votes",
        >= 1_000     => $"{votes / 1_000.0:F1}K votes",
        _            => $"{votes} votes",
    };

    private void PopulateFileInfo(MovieDetail m)
    {
        bool any = false;
        // Runtime / Released / Rated / Country — folded in from the old
        // below-poster column so the row stays compact (v2.3.x).
        if (m.Runtime.HasValue && m.Runtime.Value > 0)
        {
            FiRuntime.Text = $"{m.Runtime.Value} min";
            FiRuntimeBlock.Visibility = Visibility.Visible;
            any = true;
        }
        if (!string.IsNullOrWhiteSpace(m.Premiered))
        {
            FiRelease.Text = DateTime.TryParse(m.Premiered, out var dt)
                ? dt.ToString("MMM d, yyyy")
                : m.Premiered!;
            FiReleaseBlock.Visibility = Visibility.Visible;
            any = true;
        }
        else if (m.Year.HasValue)
        {
            FiRelease.Text = m.Year.Value.ToString();
            FiReleaseBlock.Visibility = Visibility.Visible;
            any = true;
        }
        if (!string.IsNullOrWhiteSpace(m.Mpaa))
        {
            FiMpaa.Text = m.Mpaa!;
            FiMpaaBlock.Visibility = Visibility.Visible;
            any = true;
        }
        if (!string.IsNullOrWhiteSpace(m.Country))
        {
            FiCountry.Text = m.Country!;
            FiCountryBlock.Visibility = Visibility.Visible;
            any = true;
        }
        if (m.FileSizeBytes.HasValue && m.FileSizeBytes.Value > 0)
        {
            FiSize.Text = FormatBytes(m.FileSizeBytes.Value);
            FiSizeBlock.Visibility = Visibility.Visible;
            any = true;
        }
        if (m.DurationSeconds.HasValue && m.DurationSeconds.Value > 0)
        {
            FiDur.Text = FormatDuration(m.DurationSeconds.Value);
            FiDurBlock.Visibility = Visibility.Visible;
            any = true;
        }
        if (!string.IsNullOrWhiteSpace(m.ContainerExt))
        {
            FiContainer.Text = m.ContainerExt.ToUpperInvariant();
            FiContainerBlock.Visibility = Visibility.Visible;
            any = true;
        }
        if (!string.IsNullOrWhiteSpace(m.AudioLanguages))
        {
            FiLangs.Text = m.AudioLanguages.Replace(",", " · ");
            FiLangBlock.Visibility = Visibility.Visible;
            any = true;
        }
        if (!string.IsNullOrWhiteSpace(m.SubtitleLanguages))
        {
            var subs = m.SubtitleLanguages.Split(',', StringSplitOptions.RemoveEmptyEntries);
            FiSubs.Text = subs.Length switch
            {
                0 => "—",
                1 => subs[0],
                <= 3 => string.Join(" · ", subs),
                _ => $"{subs.Length} tracks",
            };
            FiSubsBlock.Visibility = Visibility.Visible;
            any = true;
        }
        FileInfoPanel.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
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

    // ── Sticky action bar + external link buttons (v2.3) ─────────────────

    private void OnContentScrolled(object sender, ScrollViewerViewChangedEventArgs e)
    {
        // Bar appears once the user is past the hero block (~340 px tall)
        StickyBar.Visibility = ContentScroller.VerticalOffset > 280
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnStickyClose(object sender, RoutedEventArgs e) => Close();

    private async void OnOpenImdb(object sender, RoutedEventArgs e)
    {
        if (_movie?.ImdbId == null) return;
        try { await Launcher.LaunchUriAsync(new Uri($"https://www.imdb.com/title/{_movie.ImdbId}/")); } catch { }
    }

    private async void OnOpenTmdb(object sender, RoutedEventArgs e)
    {
        if (_movie?.TmdbId == null) return;
        try { await Launcher.LaunchUriAsync(new Uri($"https://www.themoviedb.org/movie/{_movie.TmdbId}")); } catch { }
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
                }
                catch (Microsoft.Data.Sqlite.SqliteException) { /* dup name */ }
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
