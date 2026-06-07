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

        // Header line: code · aired · runtime · rating + ★ favorite toggle
        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var meta = new List<string>();
        if (!string.IsNullOrEmpty(d.AiredText)) meta.Add(d.AiredText);
        if (!string.IsNullOrEmpty(d.RuntimeText)) meta.Add(d.RuntimeText);
        if (!string.IsNullOrEmpty(d.RatingText)) meta.Add(d.RatingText);
        headerRow.Children.Add(new TextBlock
        {
            Text = $"{d.Code}   ·   {string.Join("   ·   ", meta)}",
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["MutedBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        var favBtn = new Button
        {
            Content = d.IsFavorite ? "★ Favorited" : "☆ Favorite",
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 12,
        };
        favBtn.Click += (_, _) =>
        {
            d.IsFavorite = !d.IsFavorite;
            // SetEpisodeFavorite raises TvShowStateChanged → AppState
            // auto-syncs the show's sidecar, no explicit write needed.
            AppState.Instance.Db.SetEpisodeFavorite(d.Id, d.IsFavorite);
            ep.IsFavorite = d.IsFavorite;  // pushes the badge update to the card
            favBtn.Content = d.IsFavorite ? "★ Favorited" : "☆ Favorite";
            SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
        };
        Grid.SetColumn(favBtn, 1);
        headerRow.Children.Add(favBtn);
        root.Children.Add(headerRow);

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

        // v2.9 — Personal note for this episode. Saves on blur; empty
        // string clears the note row. Sidecar is best-effort.
        root.Children.Add(new TextBlock
        {
            Text = "Your note",
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            CharacterSpacing = 100,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["MutedBrush"],
            Margin = new Thickness(0, 6, 0, 0),
        });
        var noteBox = new TextBox
        {
            Text = d.Note ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 70,
            PlaceholderText = "Anything you want to remember about this episode…",
        };
        noteBox.LostFocus += (_, _) =>
        {
            var fresh = noteBox.Text?.Trim();
            if ((fresh ?? "") == (d.Note ?? "")) return;
            d.Note = fresh;
            AppState.Instance.Db.SetEpisodeNote(d.Id, fresh);  // auto-syncs sidecar
            ep.Note = fresh;
        };
        root.Children.Add(noteBox);

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
        BackBtn.Visibility       = _level == Level.Shows ? Visibility.Collapsed : Visibility.Visible;
        ShowsLevelHost.Visibility = _level == Level.Shows ? Visibility.Visible : Visibility.Collapsed;
        SeasonsPanel.Visibility   = _level == Level.Show  ? Visibility.Visible : Visibility.Collapsed;

        if (_level == Level.Shows) LoadShows();
        else LoadShow();
    }

    private void LoadShows()
    {
        TitleText.Text = "All TV shows";
        BackLabel.Text = "Back";
        var shows = AppState.Instance.Db.GetTvShows(AppState.Instance.Connected);
        _shows.Clear();
        foreach (var s in shows) _shows.Add(s);
        SubText.Text = shows.Count == 1 ? "1 show" : $"{shows.Count} shows";
        EmptyState.Visibility = shows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LoadContinueWatching();
    }

    // ── v2.9 Continue Watching row ───────────────────────────────────────────

    /// <summary>
    /// Builds the "▶ Continue Watching" strip at the top of the shows grid.
    /// Hidden when there are no in-progress shows. Each card shows the show
    /// poster + "Next: SxxExx · Title" label; tapping it opens the show
    /// page so the user can hit ▶ Play next (or pick a different episode).
    /// </summary>
    private void LoadContinueWatching()
    {
        var items = AppState.Instance.Db.GetTvContinueWatching(AppState.Instance.Connected, limit: 12);
        if (items.Count == 0)
        {
            ContinueWatchingHost.Visibility = Visibility.Collapsed;
            ContinueWatchingRepeater.ItemsSource = null;
            return;
        }
        var cards = new List<FrameworkElement>(items.Count);
        foreach (var it in items) cards.Add(BuildContinueWatchingCard(it));
        ContinueWatchingRepeater.ItemsSource = cards;
        ContinueWatchingHost.Visibility = Visibility.Visible;
    }

    private FrameworkElement BuildContinueWatchingCard(TvContinueWatchingItem it)
    {
        var card = new Grid
        {
            Width = 200,
            Height = 290,
        };
        // No ToolTip: this row's cards live in a horizontal scroller and the
        // show title + next-episode label are already shown on the card, so
        // a tooltip would only add the WinUI orphaned-tooltip-on-scroll bug.

        var border = new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBrush"],
            CornerRadius = new CornerRadius(12),
        };
        border.Shadow = new Microsoft.UI.Xaml.Media.ThemeShadow();
        border.Translation = new System.Numerics.Vector3(0, 0, 16);

        var inner = new Grid();

        // Poster (or placeholder)
        var poster = new Image { Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };
        inner.Children.Add(poster);

        var placeholder = new Grid
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PlaceholderBgBrush"],
        };
        placeholder.Children.Add(new TextBlock
        {
            Text = "📺",
            FontSize = 44,
            Opacity = 0.85,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        inner.Children.Add(placeholder);

        // Hide placeholder once the poster image loads.
        if (!string.IsNullOrEmpty(it.LocalPoster))
        {
            placeholder.Visibility = Visibility.Visible;
            poster.ImageOpened += (_, _) => placeholder.Visibility = Visibility.Collapsed;
            LoadShowImage(poster, it.LocalPoster, 260);
        }

        // ▶ overlay (large, centered) — signals "play next"
        var playOverlay = new Border
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0x66, 0, 0, 0)),
            CornerRadius = new CornerRadius(28),
            Width = 56, Height = 56,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 10, 0),
            Child = new TextBlock
            {
                Text = "▶",
                FontSize = 22,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        inner.Children.Add(playOverlay);

        // Offline badge top-left
        if (!it.IsOnline)
        {
            var badge = new Border
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(0xCC, 0, 0, 0)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(6),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = "OFFLINE",
                    FontSize = 9,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                },
            };
            inner.Children.Add(badge);
        }

        // Bottom gradient + meta strip
        var stripBorder = new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            CornerRadius = new CornerRadius(0, 0, 12, 12),
            Padding = new Thickness(10, 8, 10, 10),
        };
        var grad = new Microsoft.UI.Xaml.Media.LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(0, 1),
        };
        grad.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Offset = 0,    Color = Windows.UI.Color.FromArgb(0x00, 0, 0, 0) });
        grad.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Offset = 0.35, Color = Windows.UI.Color.FromArgb(0xAA, 0, 0, 0) });
        grad.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Offset = 1,    Color = Windows.UI.Color.FromArgb(0xF2, 0, 0, 0) });
        stripBorder.Background = grad;

        var strip = new StackPanel { Spacing = 4 };
        strip.Children.Add(new TextBlock
        {
            Text = it.ShowTitle,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        });
        strip.Children.Add(new TextBlock
        {
            Text = it.NextLabel,
            FontSize = 11,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0xFF, 0xA7, 0x8B, 0xFA)), // brand purple-ish
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2,
            TextWrapping = TextWrapping.WrapWholeWords,
        });

        // Progress bar
        var progBack = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 2, 0, 0),
        };
        var progFill = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BrandPurpleBrush"],
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        // Bind width to fraction × parent width via SizeChanged once measured.
        var fraction = it.ProgressFraction;
        progBack.SizeChanged += (s, _) =>
        {
            if (s is Border b && b.ActualWidth > 0)
                progFill.Width = Math.Max(2, b.ActualWidth * fraction);
        };
        progBack.Child = progFill;
        strip.Children.Add(progBack);

        stripBorder.Child = strip;
        inner.Children.Add(stripBorder);

        border.Child = inner;
        card.Children.Add(border);

        // Tap → open the show. The user can then hit ▶ Play next, which
        // launches this same next-unwatched episode.
        card.Tapped += (_, _) => OpenShow(it.ShowId);

        // Visual lift on hover (subtle)
        card.PointerEntered += (_, _) => card.Translation = new System.Numerics.Vector3(0, -4, 24);
        card.PointerExited += (_, _) => card.Translation = System.Numerics.Vector3.Zero;
        return card;
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
        RefreshShowTagChips();

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

    // ── v2.9 Show tags ───────────────────────────────────────────────────────

    /// <summary>Rebuild the show's tag chip row from DB state.</summary>
    private void RefreshShowTagChips()
    {
        ShowTagChipsRepeater.Items.Clear();
        if (_detail == null) return;
        _detail.Tags = AppState.Instance.Db.GetTagNamesForShow(_detail.Id);
        foreach (var name in _detail.Tags)
            ShowTagChipsRepeater.Items.Add(BuildShowTagChip(name));
    }

    private FrameworkElement BuildShowTagChip(string tagName)
    {
        var border = new Border
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
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
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
        };
        var captured = tagName;
        label.Click += (_, _) =>
        {
            if (App.MainWindow is MainWindow mw) mw.NavigateLibraryByTag(captured);
        };
        sp.Children.Add(label);
        var x = new Button
        {
            Content = "✕",
            FontSize = 10,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            Padding = new Thickness(4, 0, 4, 0),
            MinWidth = 18,
            MinHeight = 18,
        };
        ToolTipService.SetToolTip(x, $"Remove tag “{tagName}”");
        x.Click += (_, _) =>
        {
            if (_detail == null) return;
            var tagId = AppState.Instance.Db.EnsureTag(tagName);
            // RemoveShowTag raises TvShowStateChanged → AppState auto-syncs sidecar
            AppState.Instance.Db.RemoveShowTag(_detail.Id, tagId);
            RefreshShowTagChips();
            SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
        };
        sp.Children.Add(x);
        border.Child = sp;
        return border;
    }

    private void OnAddShowTagClick(object sender, RoutedEventArgs e)
    {
        AddShowTagBtn.Visibility = Visibility.Collapsed;
        AddShowTagBox.Text = "";
        AddShowTagBox.Visibility = Visibility.Visible;
        AddShowTagBox.Focus(FocusState.Programmatic);
    }

    private void OnAddShowTagBlur(object sender, RoutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (AddShowTagBox.FocusState != FocusState.Programmatic)
            {
                AddShowTagBox.Visibility = Visibility.Collapsed;
                AddShowTagBtn.Visibility = Visibility.Visible;
            }
        });
    }

    private void OnAddShowTagTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
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

    private void OnAddShowTagSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_detail == null) return;
        var raw = args.ChosenSuggestion as string ?? sender.Text;
        var name = (raw ?? "").Trim();
        if (name.Length == 0) return;
        try
        {
            var tagId = AppState.Instance.Db.EnsureTag(name);
            AppState.Instance.Db.AddShowTag(_detail.Id, tagId);
            RefreshShowTagChips();
            SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Add show tag failed: {ex.Message}");
        }
        sender.Text = "";
        AddShowTagBox.Visibility = Visibility.Collapsed;
        AddShowTagBtn.Visibility = Visibility.Visible;
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
