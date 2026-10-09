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

    // v3.7.0: All TV shows sort / watched filter / poster size, remembered
    // in prefs like the All movies toolbar.
    private string _tvSort = "title:asc";
    private string _tvFilter = "all";
    private bool _tvUiReady;
    private readonly string _noShowsHint;

    // All episodes currently on the show page, for Play-next + event routing.
    private readonly List<TvEpisodeItem> _showEpisodes = new();

    public event EventHandler? SidebarRefreshRequested;

    public TvShowsPage()
    {
        InitializeComponent();
        ShowsRepeater.ItemsSource = _shows;
        ShowsRepeater.Tapped += OnShowsTapped;

        _noShowsHint = EmptyHint.Text;
        _tvSort = AppState.Instance.GetPref("tvSort", "title:asc");
        _tvFilter = AppState.Instance.GetPref("tvFilter", "all");
        if (_tvFilter is not ("all" or "unwatched" or "watched")) _tvFilter = "all";
        ApplyTvDensity(AppState.Instance.GetPref("tvDensity", "M"));
        SyncTvToolbar();
        _tvUiReady = true;

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

        // v4.4.0 (#19): dark like the show header, in both themes, with brighter
        // text. The grey dialog with the app's dim grey text was hard to read,
        // especially on a TV across the room.
        static Microsoft.UI.Xaml.Media.SolidColorBrush Rgb(byte r, byte g, byte b, byte a = 0xFF) =>
            new(Windows.UI.Color.FromArgb(a, r, g, b));
        var textBrush  = Rgb(0xF0, 0xF0, 0xF5);
        var metaBrush  = Rgb(0xD8, 0xD8, 0xD8);
        var labelBrush = Rgb(0x9C, 0xA3, 0xAF);

        var root = new StackPanel { Spacing = 14 };

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
            FontSize = 14,
            Foreground = metaBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var favBtn = new Button
        {
            Content = d.IsFavorite ? "★ Favorited" : "☆ Favorite",
            Background = Rgb(0xFF, 0xFF, 0xFF, 0x22),
            Foreground = textBrush,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 5, 12, 5),
            FontSize = 13,
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
                Foreground = textBrush,
                FontSize = 15,
                LineHeight = 24,
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
                    Background = Rgb(0xFF, 0xFF, 0xFF, 0x22),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 3, 8, 3),
                    Child = new TextBlock { Text = b, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = textBrush },
                });
            root.Children.Add(wrap);
        }

        // Detail rows (audio langs, subs, duration, file size)
        void AddRow(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var l = new TextBlock { Text = label, FontSize = 13, Foreground = labelBrush };
            var v = new TextBlock { Text = value, FontSize = 13, TextWrapping = TextWrapping.Wrap,
                Foreground = textBrush };
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
            Text = "YOUR NOTE",
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            CharacterSpacing = 120,
            Foreground = labelBrush,
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

        // Show name small on the first line, the episode's own name under it
        var title = new StackPanel { Spacing = 2 };
        title.Children.Add(new TextBlock
        {
            Text = d.ShowTitle, FontSize = 14, Foreground = labelBrush,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        title.Children.Add(new TextBlock
        {
            Text = d.Title, FontSize = 24, Foreground = textBrush,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold, TextWrapping = TextWrapping.Wrap,
        });

        var dlg = new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 500, Padding = new Thickness(0, 0, 16, 0) },
            PrimaryButtonText = "▶ Play",
            SecondaryButtonText = d.IsWatched ? "Mark unwatched" : "Mark watched",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ElementTheme.Dark,   // the dark card in both themes, as the show header
            Background = Rgb(0x0A, 0x0A, 0x12),    // the button bar
            BorderBrush = Rgb(0xFF, 0xFF, 0xFF, 0x33),
            BorderThickness = new Thickness(1),
        };
        dlg.Resources["ContentDialogTopOverlay"] = Rgb(0x0E, 0x0E, 0x16);   // the card itself
        dlg.Resources["ContentDialogMaxWidth"] = 720.0;
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
        TvToolbar.Visibility      = _level == Level.Shows ? Visibility.Visible : Visibility.Collapsed;
        TvFilterPills.Visibility  = _level == Level.Shows ? Visibility.Visible : Visibility.Collapsed;

        if (_level == Level.Shows) LoadShows();
        else
        {
            // A filtered-empty list must not leave its empty state over a show page.
            EmptyState.Visibility = Visibility.Collapsed;
            LoadShow();
        }
    }

    private void LoadShows()
    {
        TitleText.Text = "All TV shows";
        BackLabel.Text = "Back";
        var all = AppState.Instance.Db.GetTvShows(AppState.Instance.Connected);
        var shown = SortShows(FilterShows(all)).ToList();
        _shows.Clear();
        foreach (var s in shown) _shows.Add(s);
        SubText.Text = shown.Count == all.Count
            ? (all.Count == 1 ? "1 show" : $"{all.Count} shows")
            : $"{shown.Count} of {all.Count} shows";
        if (all.Count == 0)
        {
            EmptyTitle.Text = "No TV shows yet";
            EmptyHint.Text = _noShowsHint;
        }
        else
        {
            EmptyTitle.Text = _tvFilter == "watched" ? "No fully watched shows yet" : "Nothing left to watch";
            EmptyHint.Text = "Choose All to see every show.";
        }
        EmptyState.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── v3.7.0 All TV shows toolbar ──────────────────────────────────────────

    private IEnumerable<TvShowListItem> FilterShows(IEnumerable<TvShowListItem> shows) => _tvFilter switch
    {
        "watched"   => shows.Where(s => s.FullyWatched),
        "unwatched" => shows.Where(s => !s.FullyWatched),
        _           => shows,
    };

    // Title ↑ keeps the database order (sort title, then title). The other
    // sorts are stable on top of it, so ties stay alphabetical; unknown
    // values (no year, no rating, never watched) go last.
    private IEnumerable<TvShowListItem> SortShows(IEnumerable<TvShowListItem> shows)
    {
        var parts = _tvSort.Split(':');
        bool desc = parts.Length > 1 && parts[1] == "desc";
        return parts[0] switch
        {
            "year"        => desc ? shows.OrderByDescending(s => s.Year ?? 0)
                                  : shows.OrderBy(s => s.Year ?? int.MaxValue),
            "rating"      => desc ? shows.OrderByDescending(s => s.Rating ?? -1)
                                  : shows.OrderBy(s => s.Rating ?? double.MaxValue),
            "date_added"  => desc ? shows.OrderByDescending(s => s.DateAdded)
                                  : shows.OrderBy(s => s.DateAdded),
            "last_played" => desc ? shows.OrderByDescending(s => s.LastPlayed)
                                  : shows.OrderBy(s => s.LastPlayed == 0 ? long.MaxValue : s.LastPlayed),
            _             => desc ? shows.Reverse() : shows,
        };
    }

    private void OnTvSortChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_tvUiReady || TvSortCombo.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        _tvSort = tag;
        AppState.Instance.SetPref("tvSort", tag);
        LoadShows();
    }

    private void OnTvFilterClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        _tvFilter = tag;
        AppState.Instance.SetPref("tvFilter", tag);
        SyncTvToolbar();
        LoadShows();
    }

    private void OnTvDensityClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }) return;
        ApplyTvDensity(tag);
        AppState.Instance.SetPref("tvDensity", tag);
        LoadShows();   // fresh items, so every card re-applies the new size
    }

    private void ApplyTvDensity(string tag)
    {
        var (w, h) = TvShowCard.SetDensity(tag);
        ShowsGridLayout.MinItemWidth = w;
        ShowsGridLayout.MinItemHeight = h;
        TvDensityS.IsChecked  = tag == "S";
        TvDensityM.IsChecked  = tag is not ("S" or "L" or "XL");
        TvDensityL.IsChecked  = tag == "L";
        TvDensityXL.IsChecked = tag == "XL";
        TvDensityLabel.Text = $"Size: {(tag is "S" or "L" or "XL" ? tag : "M")}";
    }

    private void SyncTvToolbar()
    {
        // Only touch the combo when it's wrong: setting it fires OnTvSortChanged,
        // which would otherwise store a passing value as the user's choice.
        var index = 0;
        for (int i = 0; i < TvSortCombo.Items.Count; i++)
            if (TvSortCombo.Items[i] is ComboBoxItem { Tag: string t } && t == _tvSort) { index = i; break; }
        if (TvSortCombo.SelectedIndex != index) TvSortCombo.SelectedIndex = index;
        var pill = (Style)Application.Current.Resources["PillButtonStyle"];
        var active = (Style)Application.Current.Resources["PillButtonActiveStyle"];
        TvFilterAll.Style       = _tvFilter == "all"       ? active : pill;
        TvFilterUnwatched.Style = _tvFilter == "unwatched" ? active : pill;
        TvFilterWatched.Style   = _tvFilter == "watched"   ? active : pill;
    }

    private TvShowDetail? _detail;

    private void LoadShow()
    {
        if (_currentShow == null) { _level = Level.Shows; ShowLevel(); return; }
        TitleText.Text = _currentShow.Title;   // as written, not in capitals (v3.10.0, issue #12)
        BackLabel.Text = "All TV Shows";
        EmptyState.Visibility = Visibility.Collapsed;

        _selectedSeason = null;   // a newly opened show starts on its default season
        ShowTmdbStatus.Visibility = Visibility.Collapsed;
        PopulateShowHeader();
        BuildSeasonSections();
    }

    // v3.8.0: the show page shows one season at a time, picked from a row of
    // season tabs, so a long show doesn't need a long scroll to reach season 10.
    private int? _selectedSeason;

    /// <summary>Season tabs, then the selected season's header + episode row.</summary>
    private void BuildSeasonSections()
    {
        SeasonSectionsHost.Children.Clear();
        _showEpisodes.Clear();
        if (_currentShow == null) return;

        var connected = AppState.Instance.Connected;
        var seasons = AppState.Instance.Db.GetSeasons(_currentShow.Id);
        SubText.Text = $"{seasons.Count} season{(seasons.Count == 1 ? "" : "s")}";

        var episodes = new Dictionary<int, List<TvEpisodeItem>>();
        foreach (var season in seasons)
        {
            var eps = AppState.Instance.Db.GetEpisodes(_currentShow.Id, season.Season, connected);
            episodes[season.Season] = eps;
            _showEpisodes.AddRange(eps);
        }

        // Tabs list the regular seasons in order with Specials last.
        var tabs = seasons.OrderBy(s => s.Season == 0 ? int.MaxValue : s.Season).ToList();
        if (_selectedSeason is not int chosen || !episodes.ContainsKey(chosen))
        {
            // Default: the first regular season with something left to watch.
            var regular = tabs.Where(s => s.Season != 0).ToList();
            _selectedSeason = (regular.FirstOrDefault(s => episodes[s.Season].Any(e => !e.IsWatched))
                               ?? regular.FirstOrDefault() ?? tabs.FirstOrDefault())?.Season;
        }

        if (tabs.Count > 0)
            SeasonSectionsHost.Children.Add(BuildSeasonTabs(tabs));

        foreach (var season in tabs.Where(s => s.Season == _selectedSeason))
        {
            var eps = episodes[season.Season];

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

            // Section header — label + episode/watched counts + actions.
            SeasonSectionsHost.Children.Add(BuildSeasonHeader(season, eps, rowScroller));
            SeasonSectionsHost.Children.Add(rowScroller);
        }

        // Header progress + Play-next label depend on the full episode set.
        RefreshHeaderProgress();
    }

    /// <summary>One pill per season ("Season 1" … "Specials"); the selected one is filled.</summary>
    private FrameworkElement BuildSeasonTabs(List<TvSeason> seasons)
    {
        var tabs = new Controls.WrapPanel
        {
            HorizontalSpacing = 8,
            VerticalSpacing = 8,
            Margin = new Thickness(24, 0, 24, 0),
        };
        foreach (var season in seasons)
        {
            var number = season.Season;
            var tab = new Button
            {
                Content = season.SeasonLabel,
                Style = (Style)Application.Current.Resources[
                    number == _selectedSeason ? "PillButtonActiveStyle" : "PillButtonStyle"],
            };
            tab.Click += (_, _) =>
            {
                if (_selectedSeason == number) return;
                _selectedSeason = number;
                BuildSeasonSections();
            };
            tabs.Children.Add(tab);
        }
        return tabs;
    }

    /// <summary>
    /// Richer season bar: a "Season N" title, a "watched / total · runtime"
    /// sub-line, and Play-season + Mark-all-watched actions on the right,
    /// plus ‹ › buttons that page the episode row a screenful at a time.
    /// </summary>
    private FrameworkElement BuildSeasonHeader(TvSeason season, List<TvEpisodeItem> eps, ScrollViewer row)
    {
        var muted = CineLibraryCS.Services.ThemeBrushes.Get("MutedBrush");
        var text = CineLibraryCS.Services.ThemeBrushes.Get("TextBrush");

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
            Background = CineLibraryCS.Services.ThemeBrushes.Get("BrandPurpleBrush"),
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
            Background = CineLibraryCS.Services.ThemeBrushes.Get("CardBrush"),
            Foreground = text,
            BorderBrush = CineLibraryCS.Services.ThemeBrushes.Get("BorderBrush"),
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

        // Page the episode row: one step moves by the whole cards that fit.
        Button PageButton(string glyph, string name, int direction)
        {
            var b = new Button
            {
                Content = new FontIcon { Glyph = glyph, FontSize = 12 },
                Background = CineLibraryCS.Services.ThemeBrushes.Get("CardBrush"),
                Foreground = text,
                BorderBrush = CineLibraryCS.Services.ThemeBrushes.Get("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, name);
            ToolTipService.SetToolTip(b, name);
            b.Click += (_, _) =>
            {
                const double step = 270 + 14;   // TvEpisodeCard width + row spacing
                var cards = Math.Max(1, Math.Floor((row.ViewportWidth + 14) / step));
                var target = Math.Clamp(row.HorizontalOffset + direction * cards * step, 0, row.ScrollableWidth);
                row.ChangeView(target, null, null);
            };
            return b;
        }
        var prev = PageButton(((char)0xE76B).ToString(), "Previous episodes", -1);
        var next = PageButton(((char)0xE76C).ToString(), "Next episodes", 1);
        void SyncPager()
        {
            var visible = row.ScrollableWidth > 0.5 ? Visibility.Visible : Visibility.Collapsed;
            prev.Visibility = next.Visibility = visible;
            prev.IsEnabled = row.HorizontalOffset > 0.5;
            next.IsEnabled = row.HorizontalOffset < row.ScrollableWidth - 0.5;
        }
        row.ViewChanged += (_, _) => SyncPager();
        row.SizeChanged += (_, _) => SyncPager();
        if (row.Content is FrameworkElement episodesRow)
            episodesRow.SizeChanged += (_, _) => SyncPager();   // the row grows as cards realize
        actions.Children.Add(prev);
        actions.Children.Add(next);

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
        ShowMpaa.Text = _detail.Mpaa ?? "";
        ShowMpaa.Visibility = string.IsNullOrWhiteSpace(_detail.Mpaa) ? Visibility.Collapsed : Visibility.Visible;
        ShowStatus.Text = _detail.Status ?? "";
        ShowStatus.Visibility = string.IsNullOrEmpty(_detail.Status) ? Visibility.Collapsed : Visibility.Visible;
        ShowProgress.Text = $"{_detail.WatchedCount}/{_detail.EpisodeCount} watched";
        ShowStudio.Text = _detail.Studio ?? "";
        ShowStudio.Visibility = string.IsNullOrWhiteSpace(_detail.Studio) ? Visibility.Collapsed : Visibility.Visible;
        ShowFolderBtn.IsEnabled = ResolveShowFolderAbs(_detail) != null;
        // One flowing paragraph: a blank line between paragraphs would use up
        // one of the five visible lines and hide the "…" that says there's more.
        ShowPlot.Text = System.Text.RegularExpressions.Regex.Replace(_detail.Plot ?? "", @"\s*\n\s*", " ").Trim();
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

        // v4.0.1: drive location, as in the movie window
        var driveLetter = AppState.Instance.Connected.TryGetValue(_detail.VolumeSerial, out var dl) ? dl : null;
        ShowDriveDot.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(driveLetter != null
            ? Windows.UI.Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E)
            : Windows.UI.Color.FromArgb(0xFF, 0x6B, 0x72, 0x80));
        ShowDriveText.Text = _detail.DriveLabel + (driveLetter != null ? $" ({driveLetter}:)" : "");

        UpdateShowButtons();
        UpdateShowNote();
        RefreshShowTagChips();

        // Cast
        CastRepeater.ItemsSource = _detail.Actors;
        ShowCastSection.Visibility = _detail.Actors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _ = LoadCastThumbsAsync(_detail.Actors, ResolveShowFolderAbs(_detail));

        ShowImdbBtn.Visibility = string.IsNullOrWhiteSpace(_detail.ImdbId) ? Visibility.Collapsed : Visibility.Visible;
        ShowTmdbBtn.Visibility = string.IsNullOrWhiteSpace(_detail.TmdbId) ? Visibility.Collapsed : Visibility.Visible;

        // v3.10.0: without fanart there's no banner; the title sits beside the
        // poster at the top, kept clear of the IMDb / TMDb buttons in the corner.
        var hasFanart = _detail.LocalFanart != null
                        && AppState.Instance.Db.GetCachedImagePath(_detail.LocalFanart) != null;
        ShowBanner.Visibility = hasFanart ? Visibility.Visible : Visibility.Collapsed;
        ShowInfoGrid.Margin = new Thickness(0, hasFanart ? -110 : 24, 0, 0);
        ShowMeta.Margin = hasFanart ? new Thickness(0, 122, 0, 0) : new Thickness(0, 0, 110, 0);

        _ = LoadShowImage(ShowPoster, _detail.LocalPoster, 260);
        _ = LoadShowImage(ShowFanart, _detail.LocalFanart, 1600);
    }

    // ── v3.10.0: Fetch missing info from TMDB (mirrors the movie window) ──

    private void ShowTmdbBusyState(bool busy, string? status = null)
    {
        ShowTmdbBusy.IsActive = busy;
        FetchShowTmdbBtn.IsEnabled = !busy;
        if (status != null) { ShowTmdbStatus.Text = status; ShowTmdbStatus.Visibility = Visibility.Visible; }
    }

    private async void OnFetchShowMissing(object sender, RoutedEventArgs e)
    {
        if (_detail == null) return;
        var show = _detail;
        using var client = new Services.Tmdb.TmdbClient();

        ShowTmdbBusyState(true, "Looking up TMDb…");
        try
        {
            // Match: a stored tmdb_id is exact; otherwise let the user confirm.
            Services.Tmdb.TmdbTvShow? t;
            if (int.TryParse(show.TmdbId, out var tid) && tid > 0)
            {
                t = await client.GetTvDetailsAsync(tid);
            }
            else
            {
                var picker = new TmdbPickerDialog(client, show.Title, show.Year, tvShows: true) { XamlRoot = XamlRoot };
                if (await picker.ShowAsync() != ContentDialogResult.Primary || picker.Picked == null)
                {
                    ShowTmdbBusy.IsActive = false;
                    FetchShowTmdbBtn.IsEnabled = true;
                    ShowTmdbStatus.Visibility = Visibility.Collapsed;
                    return;
                }
                t = await client.GetTvDetailsAsync(picker.Picked.TmdbId);
            }
            if (t == null)
            {
                ShowTmdbBusyState(false, "Couldn't reach TMDb. Try again.");
                return;
            }

            // Poster / fanart → portable cache, only if currently missing.
            string? posterRel = null, fanartRel = null;
            if (string.IsNullOrWhiteSpace(show.LocalPoster) && !string.IsNullOrEmpty(t.PosterPath))
                posterRel = await MovieDetailDialog.DownloadArtAsync(client, t.PosterPath!, "manual_posters", t.TmdbId);
            if (string.IsNullOrWhiteSpace(show.LocalFanart) && !string.IsNullOrEmpty(t.BackdropPath))
                fanartRel = await MovieDetailDialog.DownloadArtAsync(client, t.BackdropPath!, "manual_fanart", t.TmdbId);

            var studio = t.Networks.Count > 0 ? t.Networks[0].Name
                       : t.ProductionCompanies.Count > 0 ? t.ProductionCompanies[0].Name : null;
            var db = AppState.Instance.Db;
            db.FillTvShowGaps(
                show.Id,
                year: t.Year > 0 ? t.Year : null,
                rating: t.Rating > 0 ? t.Rating : null,
                votes: t.VoteCount > 0 ? t.VoteCount : null,
                plot: string.IsNullOrWhiteSpace(t.Overview) ? null : t.Overview,
                mpaa: string.IsNullOrWhiteSpace(t.Certification) ? null : t.Certification,
                premiered: string.IsNullOrWhiteSpace(t.FirstAirDate) ? null : t.FirstAirDate,
                studio: string.IsNullOrWhiteSpace(studio) ? null : studio,
                status: string.IsNullOrWhiteSpace(t.Status) ? null : t.Status,
                imdbId: string.IsNullOrWhiteSpace(t.ImdbId) ? null : t.ImdbId,
                tmdbId: t.TmdbId.ToString(),
                posterRel: posterRel, fanartRel: fanartRel);
            db.FillTvShowGenres(show.Id, t.Genres.Select(g => g.Name).ToList());

            // Cast photos are fetched even when the show has cast: .nfo thumbs are
            // often TMDb links (blank offline) or missing; these are kept locally.
            if (t.Cast.Count > 0)
            {
                ShowTmdbBusyState(true, "Fetching cast photos…");
                var actors = new List<(string, string?, int, string?)>();
                foreach (var c in t.Cast)
                {
                    if (string.IsNullOrWhiteSpace(c.Name)) continue;
                    string? thumbRel = null;
                    if (!string.IsNullOrEmpty(c.ProfilePath))
                        thumbRel = await MovieDetailDialog.DownloadArtRawAsync(client, c.ProfilePath!, "manual_actors", "w185");
                    actors.Add((c.Name, string.IsNullOrWhiteSpace(c.Character) ? null : c.Character, c.Order, thumbRel));
                }
                db.AddManualShowActors(show.Id, actors);
            }

            ShowTmdbBusyState(false, "Updated.");
            if (ReferenceEquals(_detail, show)) PopulateShowHeader();   // re-render in place
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Fetch missing show info failed: {ex.Message}");
            ShowTmdbBusyState(false, "Something went wrong. Please try again.");
        }
    }

    private async void OnOpenShowImdb(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_detail?.ImdbId)) return;
        await OpenWebPageAsync($"https://www.imdb.com/title/{_detail.ImdbId.Trim()}/", "IMDb");
    }

    private async void OnOpenShowTmdb(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_detail?.TmdbId)) return;
        await OpenWebPageAsync($"https://www.themoviedb.org/tv/{_detail.TmdbId.Trim()}", "TMDb");
    }

    private static async Task OpenWebPageAsync(string url, string site)
    {
        try { await Windows.System.Launcher.LaunchUriAsync(new Uri(url)); }
        catch
        {
            if (App.MainWindow is MainWindow mw) mw.ShowToast($"Couldn't open {site}. Is a web browser installed?");
        }
    }

    private void UpdateShowButtons()
    {
        if (_detail == null) return;
        ShowFavBtn.Content = _detail.IsFavorite ? "★ Favorited" : "☆ Favorite";
        ShowWatchlistBtn.Content = _detail.IsWatchlist ? "📌 In Watchlist" : "📋 Watchlist";
    }

    // ── v4.3.0 (#17): the show's own note ──────────────────────────────────
    // Written in a small box, shown under the plot, and listed on the Notes page.

    private void UpdateShowNote()
    {
        var note = _detail?.Note;
        var has = !string.IsNullOrWhiteSpace(note);
        ShowNoteText.Text = has ? note : "";
        ShowNoteWrap.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        ShowNoteBtnText.Text = has ? "📝 Edit note" : "📝 Add note";
    }

    private async void OnEditShowNote(object sender, RoutedEventArgs e)
    {
        if (_detail == null) return;
        var show = _detail;
        var box = new TextBox
        {
            Text = show.Note ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 120,
            MaxHeight = 300,
            PlaceholderText = "Anything you want to remember about this show…",
        };
        ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        var dlg = new ContentDialog
        {
            Title = $"📝 Note for {show.Title}",
            Content = box,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = MainWindow.CurrentTheme,
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

        var fresh = box.Text?.Trim();
        if (string.IsNullOrEmpty(fresh)) fresh = null;
        if (fresh == show.Note) return;
        show.Note = fresh;
        AppState.Instance.Db.SetTvShowNote(show.Id, fresh);   // also the show folder's state file
        if (ReferenceEquals(show, _detail)) UpdateShowNote();
        SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);   // the Notes count
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

    private async void OnOpenShowFolder(object sender, RoutedEventArgs e)
    {
        if (_detail == null) return;
        var folder = ResolveShowFolderAbs(_detail);
        if (folder != null && Directory.Exists(folder))
            await Windows.System.Launcher.LaunchFolderPathAsync(folder);
    }

    private static string? ResolveShowFolderAbs(TvShowDetail d)
    {
        if (string.IsNullOrEmpty(d.FolderRelPath)) return null;
        if (!AppState.Instance.Connected.TryGetValue(d.VolumeSerial, out var letter)) return null;
        return Path.Combine($"{letter}:\\", d.FolderRelPath.Replace('/', '\\'));
    }

    // Cast thumbnails — mirror MovieDetailDialog: look in the show's
    // .actors folder first, then any inline thumb URL/path. v3.10.0: like the
    // movie window, the URL / cached thumb is used when the drive is offline too.
    private static readonly string[] ActorThumbExts = { ".jpg", ".jpeg", ".png", ".tbn", ".webp" };

    private async Task LoadCastThumbsAsync(IReadOnlyList<Models.Actor> actors, string? showFolderAbs)
    {
        await Task.Run(() =>
        {
            var actorsDir = showFolderAbs == null ? null : Path.Combine(showFolderAbs, ".actors");
            foreach (var a in actors)
            {
                Uri? uri = null;
                try
                {
                    if (actorsDir != null && Directory.Exists(actorsDir))
                    {
                        foreach (var stem in new[] { a.Name.Replace(' ', '_'), a.Name })
                        foreach (var ext in ActorThumbExts)
                        {
                            var p = Path.Combine(actorsDir, stem + ext);
                            if (File.Exists(p)) { uri = new Uri(p); break; }
                        }
                    }
                    if (uri == null && !string.IsNullOrWhiteSpace(a.Thumb))
                    {
                        var raw = a.Thumb!.Trim();
                        if (raw.StartsWith("http", StringComparison.OrdinalIgnoreCase)) uri = new Uri(raw);
                        else if (File.Exists(raw)) uri = new Uri(raw);
                        else if (AppState.Instance.Db.GetCachedImagePath(raw) is string cached) uri = new Uri(cached);
                    }
                }
                catch { }
                if (uri == null) continue;
                var capturedUri = uri;
                DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        // 100 × 150 portrait cards (v3.10.0), sharp up to 200 % scale.
                        var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage { DecodePixelWidth = 200 };
                        bmp.UriSource = capturedUri;
                        a.ThumbBitmap = bmp;
                    }
                    catch { }
                });
            }
        });
    }

    /// <summary>Loads a cached image into <paramref name="target"/>; true once it's set.</summary>
    private async Task<bool> LoadShowImage(Image target, string? relPath, int decodeWidth)
    {
        target.Source = null;
        if (relPath == null) return false;
        var full = AppState.Instance.Db.GetCachedImagePath(relPath);
        if (full == null) return false;
        try
        {
            var bytes = await Task.Run(() => ImageCache.GetOrLoad(relPath, full));
            if (bytes == null) return false;
            var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage { DecodePixelWidth = decodeWidth };
            using var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await ms.WriteAsync(bytes.AsBuffer());
            ms.Seek(0);
            await bmp.SetSourceAsync(ms);
            target.Source = bmp;
            return true;
        }
        catch { return false; }
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
        try { launched = await VideoPlayer.PlayAsync(path); }
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
