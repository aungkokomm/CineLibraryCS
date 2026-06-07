using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using CineLibraryCS.Models;
using CineLibraryCS.Services;
using CineLibraryCS.ViewModels;
using CineLibraryCS.Views;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.System;

namespace CineLibraryCS;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private LibraryPage? _libraryPage;
    private DrivesPage? _drivesPage;
    private StatisticsPage? _statisticsPage;
    private BrowsePage? _browsePage;
    private CollectionsBrowsePage? _collectionsPage;
    private TvShowsPage? _tvShowsPage;
    private OnThisDayPage? _onThisDayPage;
    private DeviceChangeWatcher? _deviceWatcher;

    public MainWindow()
    {
        // Initialize the SQLite-backed AppState SYNCHRONOUSLY first.
        // Click handlers on the sidebar (e.g. OnNavAllMovies) construct
        // LibraryViewModel which calls AppState.GetPref → Db.GetPref. If
        // the user can click before the async InitAsync completes, that
        // path NREs because Db is still null. Init is a few ms of SQLite
        // work — fine to run on the UI thread before XAML loads.
        try { AppState.Instance.Initialize(); }
        catch (Exception ex)
        {
            // If schema/migration explodes, surface it instead of leaving
            // a half-constructed app behind.
            App.LogStartupCrashStatic(ex, "AppState.Initialize");
            throw;
        }

        // v3.0.0 — load UI preferences (card shadows, reduce motion) before any
        // card is built so they paint with the right look on first render.
        UiSettings.Load();

        InitializeComponent();

        // Extend content into titlebar for Mica effect + use our custom drag region
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Apply Mica material (user-toggleable in Settings) and keep it in sync.
        ApplyMica();
        UiSettings.Changed += () => DispatcherQueue.TryEnqueue(ApplyMica);

        // Window size
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(1400, 900));   // restore-size used when the user un-maximizes
        appWindow.Title = "CineLibrary";

        // v2.9 — start maximized. The 1400×900 above becomes the size the
        // window restores to when the user clicks the restore button.
        if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            presenter.Maximize();

        // Set custom titlebar icon
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
            if (File.Exists(iconPath))
                appWindow.SetIcon(iconPath);
        }
        catch { }

        _vm = new MainViewModel();

        // Suppress the auto-displayed accelerator-key tooltip (e.g. "Ctrl+F"
        // floating over whichever element the focus visual lands on).
        RootGrid.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;

        // Global Ctrl+B to toggle sidebar
        var acc = new KeyboardAccelerator { Key = VirtualKey.B, Modifiers = VirtualKeyModifiers.Control };
        acc.Invoked += (_, a) => { ApplySidebarCollapsed(!_sidebarCollapsed); a.Handled = true; };
        RootGrid.KeyboardAccelerators.Add(acc);

        // Global Ctrl+Q to quit
        var quitAcc = new KeyboardAccelerator { Key = VirtualKey.Q, Modifiers = VirtualKeyModifiers.Control };
        quitAcc.Invoked += (_, a) => { Close(); a.Handled = true; };
        RootGrid.KeyboardAccelerators.Add(quitAcc);

        // Global Ctrl+F to focus search
        var searchAcc = new KeyboardAccelerator { Key = VirtualKey.F, Modifiers = VirtualKeyModifiers.Control };
        searchAcc.Invoked += (_, a) => {
            TitleSearchBox.Focus(FocusState.Programmatic);
            a.Handled = true;
        };
        RootGrid.KeyboardAccelerators.Add(searchAcc);

        // Esc inside the title-bar search clears the text and exits the box.
        TitleSearchBox.AddHandler(UIElement.KeyDownEvent,
            new Microsoft.UI.Xaml.Input.KeyEventHandler(OnTitleSearchKeyDown), handledEventsToo: true);

        // v2.5 — drag-and-drop targets in the sidebar. Drag selected cards
        // onto Favorites / Watchlist to flip those flags in bulk; drop on
        // a user-list row to add. AllowDrop must be set per-target.
        WireSidebarDropTarget(BtnFavorites, ids =>
        {
            foreach (var mid in ids) AppState.Instance.Db.ToggleFavorite(mid);
            return $"Favorited {ids.Count}";
        });
        WireSidebarDropTarget(BtnWatchlist, ids =>
        {
            foreach (var mid in ids) AppState.Instance.Db.SetWatchlist(mid, true);
            return $"Added {ids.Count} to watchlist";
        });

        // v1.4.1 — Ctrl+? (Ctrl+Shift+/) shows keyboard shortcuts dialog
        var helpAcc = new KeyboardAccelerator
        {
            Key = (VirtualKey)191, // '/' on US layout
            Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift,
        };
        helpAcc.Invoked += async (_, a) => { a.Handled = true; await ShowShortcutsDialogAsync(); };
        RootGrid.KeyboardAccelerators.Add(helpAcc);

        // Event-driven drive detection — replaces the 10-second poll timer.
        // The watcher subclasses our HWND and marshals WM_DEVICECHANGE to the
        // UI thread (the subclass proc runs on the UI thread by definition).
        _deviceWatcher = new DeviceChangeWatcher(hwnd, () =>
        {
            // Don't await — we're inside WndProc and must return promptly.
            _ = _vm.OnDeviceChangeAsync();
        });

        // Stop background timers BEFORE the XAML/Sqlite teardown begins.
        // The poll timer is gone in v1.5 (see above) but the toast timer
        // still exists, and the SqliteConnection still needs clean disposal.
        this.Closed += (_, _) =>
        {
            try { _deviceWatcher?.Dispose(); } catch { }
            _deviceWatcher = null;
            try { _vm.Shutdown(); } catch { }
            try { AppState.Instance.Db.Dispose(); } catch { }
        };

        _ = InitAsync();
    }

    // ── Theme ─────────────────────────────────────────────────────────────

    public static ElementTheme CurrentTheme { get; private set; } = ElementTheme.Default;

    private void ApplyTheme(ElementTheme theme)
    {
        CurrentTheme = theme;
        if (Content is FrameworkElement root)
            root.RequestedTheme = theme;
        // v3.0.0 — monochrome FontIcon glyph instead of a colour emoji so it
        // sits cleanly next to the other footer icons.
        //   Light  → Brightness (sun)   E706
        //   Dark   → QuietHours (moon)  E708
        //   System → TVMonitor          E7F4
        ThemeIcon.Glyph = theme switch
        {
            ElementTheme.Light => "",
            ElementTheme.Dark  => "",
            _                  => "",
        };
        AppState.Instance.SetPref("theme", theme.ToString());
    }

    private void OnToggleTheme(object sender, RoutedEventArgs e)
    {
        var next = CurrentTheme switch
        {
            ElementTheme.Default => ElementTheme.Dark,
            ElementTheme.Dark    => ElementTheme.Light,
            _                    => ElementTheme.Default,
        };
        ApplyTheme(next);
    }

    // ── v3.0.0 Settings dialog ────────────────────────────────────────────

    /// <summary>
    /// Settings dialog: theme (mirrors the quick toggle), card shadows, and
    /// reduce motion. Both visual extras default off, persisted via prefs,
    /// and applied live (cards listen to UiSettings.Changed).
    /// </summary>
    private async void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var muted = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["MutedBrush"];

        TextBlock Header(string t) => new()
        {
            Text = t, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            CharacterSpacing = 120, Opacity = 0.7, Foreground = muted,
            Margin = new Thickness(0, 12, 0, 4),
        };

        var panel = new StackPanel { Spacing = 6, MinWidth = 360 };

        // ── Appearance ──
        panel.Children.Add(Header("APPEARANCE"));

        // Theme — three-way segmented choice that mirrors the quick toggle.
        var themeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        RadioButton ThemeChip(string label, ElementTheme value)
        {
            var rb = new RadioButton
            {
                Content = label,
                GroupName = "theme",
                IsChecked = CurrentTheme == value,
                MinWidth = 0,
                Margin = new Thickness(0, 0, 14, 0),
            };
            rb.Checked += (_, _) => ApplyTheme(value);
            return rb;
        }
        themeRow.Children.Add(ThemeChip("Light",  ElementTheme.Light));
        themeRow.Children.Add(ThemeChip("Dark",   ElementTheme.Dark));
        themeRow.Children.Add(ThemeChip("System", ElementTheme.Default));
        panel.Children.Add(new TextBlock { Text = "Theme", FontSize = 13 });
        panel.Children.Add(themeRow);

        // Background material (Mica)
        var micaToggle = new ToggleSwitch
        {
            Header = "Background material (Mica)",
            IsOn = UiSettings.MicaEnabled,
            OffContent = "Off", OnContent = "On",
            Margin = new Thickness(0, 12, 0, 0),
        };
        micaToggle.Toggled += (_, _) => UiSettings.SetMicaEnabled(micaToggle.IsOn);
        panel.Children.Add(micaToggle);
        panel.Children.Add(new TextBlock
        {
            Text = "Shows the Windows Mica material behind the sidebar and the floating content panel, tinted by your wallpaper. Turn off for a flat, solid look or on a low-end GPU.",
            FontSize = 12, Opacity = 0.7, Foreground = muted, TextWrapping = TextWrapping.Wrap,
        });

        // Card borders
        var borderToggle = new ToggleSwitch
        {
            Header = "Card borders",
            IsOn = UiSettings.CardBorders,
            OffContent = "Off", OnContent = "On",
            Margin = new Thickness(0, 10, 0, 0),
        };
        borderToggle.Toggled += (_, _) => UiSettings.SetCardBorders(borderToggle.IsOn);
        panel.Children.Add(borderToggle);
        panel.Children.Add(new TextBlock
        {
            Text = "A thin outline around each movie card. Helps the cards stand out, especially in light theme.",
            FontSize = 12, Opacity = 0.7, Foreground = muted, TextWrapping = TextWrapping.Wrap,
        });

        // Card shadows
        var shadowToggle = new ToggleSwitch
        {
            Header = "Card drop shadows",
            IsOn = UiSettings.CardShadows,
            OffContent = "Off", OnContent = "On",
            Margin = new Thickness(0, 10, 0, 0),
        };
        shadowToggle.Toggled += (_, _) => UiSettings.SetCardShadows(shadowToggle.IsOn);
        panel.Children.Add(shadowToggle);
        panel.Children.Add(new TextBlock
        {
            Text = "A subtle shadow that lifts each movie card. Off by default for the smoothest scrolling.",
            FontSize = 12, Opacity = 0.7, Foreground = muted, TextWrapping = TextWrapping.Wrap,
        });

        // Reduce motion
        var motionToggle = new ToggleSwitch
        {
            Header = "Reduce motion",
            IsOn = UiSettings.ReduceMotion,
            OffContent = "Off", OnContent = "On",
            Margin = new Thickness(0, 10, 0, 0),
        };
        motionToggle.Toggled += (_, _) => UiSettings.SetReduceMotion(motionToggle.IsOn);
        panel.Children.Add(motionToggle);
        panel.Children.Add(new TextBlock
        {
            Text = "Turns off the zoom/lift animation when you hover a card. Easier on the eyes and on low-end GPUs.",
            FontSize = 12, Opacity = 0.7, Foreground = muted, TextWrapping = TextWrapping.Wrap,
        });

        var dialog = new ContentDialog
        {
            Title = "Settings",
            Content = new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 520,
            },
            CloseButtonText = "Done",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = CurrentTheme,
        };
        try { await dialog.ShowAsync(); } catch { }
    }

    private async Task InitAsync()
    {
        // AppState.Initialize() already ran synchronously in the ctor —
        // DB is ready by the time any UI handler can fire. InitAsync now
        // only does the slower bits: theme apply, sidebar populate, update check.

        // Restore saved theme (default = System, not forced dark)
        var saved = AppState.Instance.GetPref("theme", "Default");
        var theme = Enum.TryParse<ElementTheme>(saved, out var t) ? t : ElementTheme.Default;
        ApplyTheme(theme);

        // Restore sidebar collapsed state
        if (AppState.Instance.GetPref("sidebarCollapsed", "false") == "true")
            ApplySidebarCollapsed(true);

        // v2.9 — faster perceived startup. Previously we awaited the full
        // sidebar data fetch (drives + collections + stats) BEFORE showing
        // the library, so the main content waited on queries the user isn't
        // even looking at yet. Now:
        //   1. Prime the connected-drive set (fast — just enumerates drive
        //      letters) so the grid's first paint shows correct ONLINE/OFFLINE.
        //   2. Show the library immediately; its grid load runs in the
        //      background, in parallel with the sidebar fetch below.
        //   3. Fill the sidebar, rendering it immediately (skip the 150 ms
        //      debounce that exists to coalesce mid-session bursts).
        await Task.Run(() => AppState.Instance.RefreshConnected());

        NavigateTo("library");
        SetActiveNav(BtnAllMovies);   // initial active highlight on All Movies

        await _vm.InitializeAsync();
        RefreshSidebarImmediate();

        // Fire-and-forget update check. Silent on no-network. Skipped versions
        // are remembered via the prefs table so the user isn't nagged.
        _ = CheckForUpdatesAsync();
    }

    // ── Update check ──────────────────────────────────────────────────────

    private string? _pendingUpdateUrl;
    private string? _pendingUpdateVersion;

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var skipped = AppState.Instance.GetPref("skippedUpdate", "");
            var info = await UpdateChecker.CheckAsync(string.IsNullOrEmpty(skipped) ? null : skipped);
            if (info == null) return;

            _pendingUpdateVersion = info.LatestVersion;
            _pendingUpdateUrl = info.ReleaseUrl;
            DispatcherQueue.TryEnqueue(() => ShowUpdateToast(info.LatestVersion));
        }
        catch { /* never let an update check break the app */ }
    }

    private void ShowUpdateToast(string version)
    {
        ToastText.Text = $"CineLibrary v{version} is available";
        ToastActionBtn.Content = "Download";
        ToastActionBtn.Visibility = Visibility.Visible;
        ToastBorder.Visibility = Visibility.Visible;
        // Update toast stays until dismissed — no auto-hide.
    }

    // Pending action for the toast's primary button. The update-notifier
    // path sets _pendingUpdateUrl; selection bulk-ops set _pendingUndo.
    // Whichever is set when the button is clicked wins.
    //
    // v2.5.1 — every show-toast call bumps _toastGeneration. Auto-hide
    // timers capture the generation at show-time and bail out if a newer
    // toast has been shown since. Prevents a stale 6 s timer from
    // collapsing a freshly-shown toast (or worse, clearing the wrong
    // _pendingUndo).
    private Action? _pendingUndo;
    private int _toastGeneration;

    private async void OnToastActionClick(object sender, RoutedEventArgs e)
    {
        // Undo first (selection bulk-ops). Update download is a fallback.
        if (_pendingUndo != null)
        {
            try { _pendingUndo(); } catch { }
            _pendingUndo = null;
            ToastBorder.Visibility = Visibility.Collapsed;
            return;
        }
        if (string.IsNullOrEmpty(_pendingUpdateUrl)) return;
        try { await Windows.System.Launcher.LaunchUriAsync(new Uri(_pendingUpdateUrl)); }
        catch { }
        ToastBorder.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Show a toast with an "Undo" button that calls back into the caller
    /// to revert the action. Auto-hides after 6s. Used by the multi-select
    /// bulk operations (add to list / mark watched / favorite / etc.) so a
    /// fat-fingered batch is one click away from being put back.
    /// </summary>
    public void ShowToastWithUndo(string message, Action undo)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var myGen = ++_toastGeneration;
            ToastText.Text = message;
            // Set the visual label BEFORE installing the action so a rapid
            // click can never trigger the wrong handler.
            ToastActionBtn.Content = "Undo";
            ToastActionBtn.Visibility = Visibility.Visible;
            _pendingUndo = undo;
            ToastBorder.Visibility = Visibility.Visible;
            AnimateToastIn();
            ScheduleToastHide(myGen);
        });
    }

    /// <summary>
    /// Hide the toast 6 s from now, but only if it's still showing the
    /// generation we captured. Newer ShowToast* calls bump the counter
    /// and effectively cancel us.
    /// </summary>
    private void ScheduleToastHide(int generation)
    {
        Task.Delay(6000).ContinueWith(_ => DispatcherQueue.TryEnqueue(() =>
        {
            if (_toastGeneration != generation) return;
            _pendingUndo = null;
            ToastBorder.Visibility = Visibility.Collapsed;
        }));
    }

    // ── Sidebar refresh ───────────────────────────────────────────────────

    // v2.9 — Debounce a flurry of sidebar refresh requests into one render.
    // Multiple DB writes (e.g. a bulk multi-select toggle, or the scanner
    // wrapping up) can fire RefreshSidebar 5+ times in rapid succession;
    // coalescing them within ~150 ms keeps the tree from re-rendering N×.
    private CancellationTokenSource? _sidebarDebounce;

    public void RefreshSidebar()
    {
        _sidebarDebounce?.Cancel();
        _sidebarDebounce = new CancellationTokenSource();
        var token = _sidebarDebounce.Token;
        _ = Task.Delay(150, token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            DispatcherQueue.TryEnqueue(RefreshSidebarImmediate);
        }, TaskScheduler.Default);
    }

    private void RefreshSidebarImmediate()
    {
        // Body runs on UI thread (caller dispatches). The original inline
        // DispatcherQueue.TryEnqueue is gone — we already arrived here via
        // the dispatch in the debouncer above.
        {
            var stats = _vm.Stats;
            TotalBadge.Text = stats?.TotalMovies.ToString() ?? "0";
            DrivesBadge.Text = _vm.Drives.Count.ToString();
            try { TvShowsBadge.Text = AppState.Instance.Db.GetTvShowCount().ToString(); } catch { }
            try { NotesBadge.Text = AppState.Instance.Db.GetNotesCount().ToString(); } catch { }
            // v2.5.1 — StatRuntime / StatRating tiles removed from sidebar
            // (they live on the Statistics page now). Stats object still
            // computed because other code paths use it.

            // Update watchlist badge (v1.3)
            if (_libraryPage?.ViewModel is LibraryViewModel vm)
            {
                vm.RefreshWatchlistCount();
                WatchlistBadge.Text = vm.WatchlistCount.ToString();
            }

            // Continue Watching badge (v1.8) — only show shortcut if there's anything to continue
            var cwCount = AppState.Instance.Db.GetContinueWatchingCount();
            ContinueWatchingBadge.Text = cwCount.ToString();
            BtnContinueWatching.Visibility = cwCount > 0 ? Visibility.Visible : Visibility.Collapsed;

            // Recently Watched badge (v2.9) — hidden until user has watched anything.
            try
            {
                var rwCount = AppState.Instance.Db.GetRecentlyWatchedCount();
                RecentlyWatchedBadge.Text = rwCount.ToString();
                BtnRecentlyWatched.Visibility = rwCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            } catch { }

            // On This Day (v2.9) — only show the sidebar entry when there's
            // something today. Probe is a cheap LIMIT 1 query so we can
            // call it on every sidebar refresh without worrying.
            try
            {
                BtnOnThisDay.Visibility = AppState.Instance.Db.HasOnThisDayMatches()
                    ? Visibility.Visible : Visibility.Collapsed;
            } catch { BtnOnThisDay.Visibility = Visibility.Collapsed; }

            // v2.9 — Tags section (hidden when no tags exist)
            RefreshTags();

            DrivesRepeater.ItemsSource = _vm.Drives;
            LibrariesHeader.Visibility = _vm.Drives.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            // v2.1: COLLECTIONS + TOP GENRES sub-sections removed. Both live
            // as BROWSE pages now (Collections grid, By Genre banners).

            RefreshUserLists();
        }
    }

    /// <summary>
    /// Rebuild the MY LISTS section. Each entry is a Button with the list
    /// name, a count badge, and a context menu (Rename / Delete).
    /// </summary>
    public void RefreshUserLists()
    {
        UserListsItemsPanel.Children.Clear();
        var lists = AppState.Instance.Db.GetUserLists();
        foreach (var ul in lists)
        {
            var btn = BuildUserListButton(ul);
            UserListsItemsPanel.Children.Add(btn);
        }
    }

    /// <summary>
    /// v2.9 — Rebuild the 🏷 TAGS sidebar section. One Button per tag with
    /// a count badge (movies + shows). Hidden entirely when no tags exist
    /// so empty users never see a stub.
    /// </summary>
    public void RefreshTags()
    {
        try
        {
            TagsItemsPanel.Children.Clear();
            var tags = AppState.Instance.Db.GetAllTags();
            if (tags.Count == 0)
            {
                TagsHeader.Visibility = Visibility.Collapsed;
                return;
            }
            TagsHeader.Visibility = Visibility.Visible;
            foreach (var t in tags)
                TagsItemsPanel.Children.Add(BuildTagButton(t));
        }
        catch { /* sidebar refresh must never throw — table may not exist mid-migration */ }
    }

    private Button BuildTagButton(DatabaseService.TagSummary t)
    {
        var btn = new Button
        {
            Style = (Style)Application.Current.Resources["NavItemStyle"],
            Tag = t.Id,
        };
        btn.Click += (_, _) =>
        {
            if (_libraryPage == null) NavigateTo("library");
            _libraryPage?.ViewModel.FilterByTag(t.Id, t.Name);
            if (!ReferenceEquals(ContentFrame.Content, _libraryPage)) NavigateTo("library");
            ClearLibraryBack();
            SetActiveNav(btn);
        };

        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        sp.Children.Add(new TextBlock { Text = "🏷", FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(new TextBlock
        {
            Text = t.Name,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis,
        });
        grid.Children.Add(sp);

        var badge = new Border { Style = (Style)Application.Current.Resources["BadgeStyle"] };
        Grid.SetColumn(badge, 1);
        var total = t.MovieCount + t.ShowCount;
        badge.Child = new TextBlock
        {
            Text = total.ToString(),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        grid.Children.Add(badge);

        btn.Content = grid;
        return btn;
    }

    private Button BuildUserListButton(DatabaseService.UserList ul)
    {
        var btn = new Button
        {
            Style = (Style)Application.Current.Resources["NavItemStyle"],
            Tag = ul.Id,
        };
        btn.Click += (_, _) => _libraryPage?.UpdatePageTitle(ul.Name);
        btn.Click += (_, _) =>
        {
            if (_libraryPage == null) NavigateTo("library");
            _libraryPage?.ViewModel.ShowUserList(ul.Id, ul.Name);
            if (!ReferenceEquals(ContentFrame.Content, _libraryPage)) NavigateTo("library");
            ClearLibraryBack();
            SetActiveNav(btn);
        };

        var grid = new Grid();
        grid.HorizontalAlignment = HorizontalAlignment.Stretch;
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        sp.Children.Add(new FontIcon
        {
            Glyph = "\uE8FD", // List
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons,Segoe MDL2 Assets"),
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
        });
        sp.Children.Add(new TextBlock
        {
            Text = ul.Name,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis,
        });
        grid.Children.Add(sp);

        var badge = new Border { Style = (Style)Application.Current.Resources["BadgeStyle"] };
        Grid.SetColumn(badge, 1);
        badge.Child = new TextBlock
        {
            Text = ul.MovieCount.ToString(),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        grid.Children.Add(badge);

        btn.Content = grid;

        // Right-click context menu: copy / export image / rename / delete
        var menu = new MenuFlyout();
        var copyItem = new MenuFlyoutItem { Text = "📂 Copy movies to folder…" };
        copyItem.Click += async (_, _) => await CopyListToFolder(ul);
        // v2.9 — export the list as a shareable PNG poster grid.
        var exportImgItem = new MenuFlyoutItem { Text = "📤 Export as image…" };
        exportImgItem.Click += async (_, _) => await ExportListAsImage(ul);
        var renameItem = new MenuFlyoutItem { Text = "Rename" };
        renameItem.Click += async (_, _) => await PromptRenameUserList(ul);
        var deleteItem = new MenuFlyoutItem { Text = "Delete list" };
        deleteItem.Click += async (_, _) => await ConfirmDeleteUserList(ul);
        menu.Items.Add(copyItem);
        menu.Items.Add(exportImgItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(renameItem);
        menu.Items.Add(deleteItem);
        btn.ContextFlyout = menu;

        // Drop target for drag-from-card multi-select.
        var listIdCap = ul.Id;
        var listNameCap = ul.Name;
        WireSidebarDropTarget(btn, ids =>
        {
            foreach (var mid in ids)
                AppState.Instance.Db.AddMovieToUserList(listIdCap, mid);
            return $"Added {ids.Count} to “{listNameCap}”";
        });
        return btn;
    }

    /// <summary>
    /// Wire a sidebar button as a drop target for "cinelibrary/movie-ids".
    /// The applyOp callback runs synchronously against the DB and returns
    /// the toast text. Visual: a purple outline appears while dragging
    /// over the target.
    /// </summary>
    private void WireSidebarDropTarget(Button target, Func<List<int>, string> applyOp)
    {
        target.AllowDrop = true;
        var purple = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BrandPurpleBrush"];
        var origBg = target.Background;
        var origBorderBrush = target.BorderBrush;
        var origBorderThickness = target.BorderThickness;

        target.DragEnter += (_, e) =>
        {
            if (!ContainsMovieIds(e.DataView)) return;
            e.AcceptedOperation = DataPackageOperation.Link;
            target.BorderBrush = purple;
            target.BorderThickness = new Thickness(2);
        };
        target.DragOver += (_, e) =>
        {
            if (ContainsMovieIds(e.DataView))
                e.AcceptedOperation = DataPackageOperation.Link;
        };
        target.DragLeave += (_, _) =>
        {
            target.BorderBrush = origBorderBrush;
            target.BorderThickness = origBorderThickness;
        };
        target.Drop += async (_, e) =>
        {
            // Read the DataView first — once we hand control back to the
            // event pump (e.g. by setting visuals and awaiting), the view
            // can be invalidated and GetTextAsync starts returning empty.
            // Take a deferral so the system waits for our read.
            var deferral = e.GetDeferral();
            List<int> ids;
            try { ids = await ReadMovieIdsAsync(e.DataView); }
            finally { deferral.Complete(); }
            target.BorderBrush = origBorderBrush;
            target.BorderThickness = origBorderThickness;
            if (ids.Count == 0) return;
            string toastMsg;
            try { toastMsg = applyOp(ids); }
            catch (Exception ex)
            {
                ShowToast($"Couldn't apply drop: {ex.Message}");
                return;
            }
            _ = RefreshSidebarAsync();
            // For Favorites/Watchlist the user can undo by flipping
            // those movies back. We don't have a generic inverse op
            // because applyOp could be anything, so plain toast for now.
            ShowToast(toastMsg);
        };
    }

    private static bool ContainsMovieIds(DataPackageView view)
        => view.Properties.ContainsKey("cinelibrary/movie-ids") || view.Contains(StandardDataFormats.Text);

    private static async Task<List<int>> ReadMovieIdsAsync(DataPackageView view)
    {
        string? csv = null;
        if (view.Properties.TryGetValue("cinelibrary/movie-ids", out var raw))
            csv = raw as string;
        if (csv == null && view.Contains(StandardDataFormats.Text))
        {
            try { csv = await view.GetTextAsync(); } catch { }
        }
        if (string.IsNullOrWhiteSpace(csv)) return new List<int>();
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                  .Select(s => int.TryParse(s.Trim(), out var n) ? n : 0)
                  .Where(n => n > 0)
                  .ToList();
    }

    /// <summary>
    /// Bucket-style export — pick a destination folder, then copy each
    /// online movie's source folder into it.
    /// </summary>
    /// <summary>
    /// v2.9 — Renders the list's posters as a PNG and saves it via file
    /// picker. Single-call: pick destination, render, save, toast. The
    /// list itself doesn't need to be the currently-viewed page; we fetch
    /// the movies directly from the DB by list id.
    /// </summary>
    private async Task ExportListAsImage(DatabaseService.UserList ul)
    {
        // Fetch movies in the list (all of them — exporter caps display).
        var opts = new DatabaseService.ListOptions(
            UserListId: ul.Id,
            SortKey: "title", SortDir: "asc",
            Limit: 200, Offset: 0);
        var movies = AppState.Instance.Db.GetMovies(opts, AppState.Instance.Connected);
        if (movies.Count == 0)
        {
            ShowToast($"“{ul.Name}” has no movies to export");
            return;
        }

        // Pick destination
        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
            SuggestedFileName = $"{SanitizeFileName(ul.Name)}-{DateTime.Now:yyyyMMdd}",
        };
        picker.FileTypeChoices.Add("PNG image", new List<string> { ".png" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker,
            WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file == null) return;

        ShowToast("Rendering image…");
        var ok = await ListImageExporter.ExportAsync(Content.XamlRoot, ul.Name, movies, file.Path);
        if (ok) ShowToast($"Saved to {file.Path}");
        else    ShowToast("Image export failed — see debug log");
    }

    private static string SanitizeFileName(string raw)
    {
        var s = raw;
        foreach (var ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
        return s.Trim();
    }

    private async Task CopyListToFolder(DatabaseService.UserList ul)
    {
        // 1. Folder picker
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync();
        if (folder == null) return;
        var destRoot = folder.Path;

        // 2. Build plan on background thread (folder walks can take a while)
        ListCopyService.CopyPlan plan;
        try
        {
            plan = await Task.Run(() =>
                AppState.Instance.ListCopy.BuildPlan(ul.Id, AppState.Instance.Connected));
        }
        catch (Exception ex)
        {
            ShowToast($"Couldn't read source folders: {ex.Message}");
            return;
        }

        if (plan.Items.Count == 0)
        {
            var detail = plan.OfflineDriveLabels.Count > 0
                ? $"All movies are on offline drives. Plug in:\n• {string.Join("\n• ", plan.OfflineDriveLabels)}\n\nThen try again."
                : "Nothing to copy.";
            var emptyDlg = new ContentDialog
            {
                Title = "Can't copy yet",
                Content = detail,
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot,
                RequestedTheme = CurrentTheme,
            };
            await emptyDlg.ShowAsync();
            return;
        }

        // 2b. Pre-flight: warn if any drives are offline so the user can
        // plug them in first. Continuing is allowed but only the online
        // movies will be copied.
        if (plan.OfflineDriveLabels.Count > 0)
        {
            var msg = $"These drives are offline — their movies in this list won't be copied:\n• " +
                      $"{string.Join("\n• ", plan.OfflineDriveLabels)}\n\n" +
                      $"Plug them in to include all {plan.Items.Count + plan.OfflineDriveLabels.Count}+ movies, " +
                      $"or continue with the {plan.Items.Count} online ones.";
            var dlg = new ContentDialog
            {
                Title = $"{plan.OfflineDriveLabels.Count} drive(s) offline",
                Content = msg,
                PrimaryButtonText = "Cancel",
                SecondaryButtonText = $"Continue with {plan.Items.Count} online",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot,
                RequestedTheme = CurrentTheme,
            };
            var result = await dlg.ShowAsync();
            // Primary = Cancel (default — safer when drives are missing)
            if (result != ContentDialogResult.Secondary) return;
        }

        // 3. Free-space check
        var free = AppState.Instance.ListCopy.GetFreeBytes(destRoot);
        if (free >= 0 && free < plan.TotalBytes)
        {
            var dlg = new ContentDialog
            {
                Title = "Not enough free space",
                Content = $"Need {FormatBytes(plan.TotalBytes)}, only {FormatBytes(free)} free at the destination.",
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot,
                RequestedTheme = CurrentTheme,
            };
            await dlg.ShowAsync();
            return;
        }

        // 4. Conflict prompt if any target folders already exist
        var conflicts = AppState.Instance.ListCopy.FindExistingTargets(plan, destRoot);
        var policy = ListCopyService.ConflictPolicy.Skip;
        if (conflicts.Count > 0)
        {
            var preview = conflicts.Count <= 5
                ? string.Join("\n", conflicts.Take(5).Select(c => $"• {c}"))
                : string.Join("\n", conflicts.Take(5).Select(c => $"• {c}")) + $"\n…and {conflicts.Count - 5} more";
            var dlg = new ContentDialog
            {
                Title = $"{conflicts.Count} folder(s) already exist at destination",
                Content = $"What should happen to existing copies?\n\n{preview}",
                PrimaryButtonText = "Skip existing",
                SecondaryButtonText = "Overwrite",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot,
                RequestedTheme = CurrentTheme,
            };
            var result = await dlg.ShowAsync();
            if (result == ContentDialogResult.None) return; // cancel
            policy = result == ContentDialogResult.Secondary
                ? ListCopyService.ConflictPolicy.Overwrite
                : ListCopyService.ConflictPolicy.Skip;
        }

        // 5. Progress dialog
        await ShowCopyProgressDialog(plan, destRoot, policy, ul.Name);
    }

    private async Task ShowCopyProgressDialog(
        ListCopyService.CopyPlan plan, string destRoot,
        ListCopyService.ConflictPolicy policy, string listName)
    {
        var cts = new CancellationTokenSource();
        var bar = new ProgressBar { Minimum = 0, Maximum = plan.TotalBytes, Value = 0, Height = 6 };
        var movieText = new TextBlock { FontSize = 13, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextBrush"] };
        var fileText = new TextBlock
        {
            FontSize = 11,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["MutedBrush"],
            TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis,
        };
        var bytesText = new TextBlock { FontSize = 11, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["MutedBrush"] };
        var content = new StackPanel { Spacing = 10, Width = 480 };
        content.Children.Add(movieText);
        content.Children.Add(bar);
        content.Children.Add(bytesText);
        content.Children.Add(fileText);
        if (plan.OfflineDriveLabels.Count > 0)
        {
            content.Children.Add(new TextBlock
            {
                FontSize = 11,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["MutedBrush"],
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                Text = $"Skipping {plan.OfflineDriveLabels.Count} offline drive(s): " +
                       string.Join(", ", plan.OfflineDriveLabels),
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            });
        }

        var dlg = new ContentDialog
        {
            Title = $"Copying \"{listName}\" → {destRoot}",
            Content = content,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.None,
            XamlRoot = Content.XamlRoot,
            RequestedTheme = CurrentTheme,
        };
        dlg.Closing += (_, args) => cts.Cancel(); // any close path → cancel

        var progress = new Progress<ListCopyService.CopyProgress>(p =>
        {
            // Already on UI thread (Progress<T> captures sync context)
            bar.Value = p.BytesDone;
            movieText.Text = $"Movie {p.MoviesDone} of {p.MoviesTotal}";
            bytesText.Text = $"{FormatBytes(p.BytesDone)} of {FormatBytes(p.BytesTotal)}";
            fileText.Text = p.CurrentFile;
        });

        // Kick off copy as fire-and-forget; we close the dialog when done.
        ListCopyService.CopyResult? result = null;
        var copyTask = AppState.Instance.ListCopy.ExecuteAsync(plan, destRoot, policy, progress, cts.Token);
        _ = copyTask.ContinueWith(t =>
        {
            try { result = t.Result; } catch { }
            DispatcherQueue.TryEnqueue(() => { try { dlg.Hide(); } catch { } });
        }, TaskScheduler.Default);

        await dlg.ShowAsync();

        // Summary toast
        if (result == null)
        {
            ShowToast("Copy cancelled");
        }
        else
        {
            var bits = new List<string>();
            if (result.Copied > 0) bits.Add($"{result.Copied} copied");
            if (result.Skipped > 0) bits.Add($"{result.Skipped} skipped");
            if (result.OfflineSkipped > 0) bits.Add($"{result.OfflineSkipped} offline");
            ShowToast(result.Cancelled ? $"Cancelled — {string.Join(", ", bits)}" : string.Join(", ", bits));
        }
    }

    private static string FormatBytes(long b)
    {
        const long KB = 1024L, MB = KB * 1024, GB = MB * 1024;
        if (b >= GB) return $"{b / (double)GB:F1} GB";
        if (b >= MB) return $"{b / (double)MB:F1} MB";
        if (b >= KB) return $"{b / (double)KB:F1} KB";
        return $"{b} B";
    }

    private async void OnNewUserList(object sender, RoutedEventArgs e)
    {
        var name = await PromptForListName("New list", "Untitled list");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            AppState.Instance.Db.CreateUserList(name.Trim());
            RefreshUserLists();
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            ShowToast("A list with that name already exists");
        }
    }

    private async Task PromptRenameUserList(DatabaseService.UserList ul)
    {
        var name = await PromptForListName("Rename list", ul.Name);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == ul.Name) return;
        AppState.Instance.Db.RenameUserList(ul.Id, name.Trim());
        RefreshUserLists();
    }

    private async Task ConfirmDeleteUserList(DatabaseService.UserList ul)
    {
        var dlg = new ContentDialog
        {
            Title = $"Delete \"{ul.Name}\"?",
            Content = $"This list contains {ul.MovieCount} movie(s). Movies themselves are not deleted — only the list and its membership.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
            RequestedTheme = CurrentTheme,
        };
        var result = await dlg.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            AppState.Instance.Db.DeleteUserList(ul.Id);
            RefreshUserLists();
        }
    }

    private async Task<string?> PromptForListName(string title, string initial)
    {
        var box = new TextBox { Text = initial, PlaceholderText = "List name" };
        var dlg = new ContentDialog
        {
            Title = title,
            Content = box,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
            RequestedTheme = CurrentTheme,
        };
        // Auto-select the field
        box.Loaded += (_, _) => { box.Focus(FocusState.Programmatic); box.SelectAll(); };
        var result = await dlg.ShowAsync();
        return result == ContentDialogResult.Primary ? box.Text : null;
    }

    // ── Sidebar section collapse/expand ───────────────────────────────────

    private void OnSidebarSectionToggle(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        var (repeater, chevron) = tag switch
        {
            "Libraries"   => ((FrameworkElement)DrivesRepeater,      LibrariesChevron),
            // "Genres" and "Collections" sub-sections removed in v2.1 — their
            // BROWSE pages replace them. Map kept tolerant of missing keys.
            "UserLists"   => (UserListsItemsPanel,                   UserListsChevron),
            "Tags"        => (TagsItemsPanel,                        TagsChevron),
            _             => (null!,                                  null!),
        };
        if (repeater == null) return;
        var collapsed = repeater.Visibility == Visibility.Collapsed;
        repeater.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        chevron.Text = collapsed ? "⌃" : "⌄"; // ⌃ open / ⌄ closed
    }

    // ── Navigation ────────────────────────────────────────────────────────

    // ── Mica backdrop (toggleable in Settings) ────────────────────────────

    private void ApplyMica()
    {
        if (UiSettings.MicaEnabled)
        {
            SystemBackdrop ??= new MicaBackdrop();
            MicaOffBackdrop.Visibility = Visibility.Collapsed;   // reveal Mica
        }
        else
        {
            SystemBackdrop = null;
            MicaOffBackdrop.Visibility = Visibility.Visible;     // solid window
        }
    }

    // ── Global title-bar search ───────────────────────────────────────────

    private void OnTitleSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        GlobalSearch(sender.Text ?? "");
    }

    private void OnTitleSearchKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        if (!string.IsNullOrEmpty(TitleSearchBox.Text))
        {
            TitleSearchBox.Text = "";
            GlobalSearch("");   // clear the search results too
        }
        // Leave the search box.
        Microsoft.UI.Xaml.Input.FocusManager.TryMoveFocus(
            Microsoft.UI.Xaml.Input.FocusNavigationDirection.Next);
        e.Handled = true;
    }

    private void OnTitleScopeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TitleScopeCombo.SelectedItem is ComboBoxItem item && item.Tag is string scope
            && _libraryPage != null)
            _libraryPage.ViewModel.SearchScope = scope;
    }

    private void GlobalSearch(string text)
    {
        // Make sure the Library page is showing, then drive its search engine
        // (_vm.SearchText). Searching from any other page jumps here.
        if (_libraryPage == null || !ReferenceEquals(ContentFrame.Content, _libraryPage))
            NavigateTo("library");
        if (_libraryPage == null) return;
        if (TitleScopeCombo.SelectedItem is ComboBoxItem scopeItem && scopeItem.Tag is string scope)
            _libraryPage.ViewModel.SearchScope = scope;
        _libraryPage.ViewModel.SearchText = text;
    }

    private void NavigateTo(string page, object? param = null)
    {
        if (page == "library")
        {
            if (_libraryPage == null)
            {
                _libraryPage = new LibraryPage();
                _libraryPage.SidebarRefreshRequested += (_, _) => { _ = RefreshSidebarAsync(); };
                // Keep the title-bar search box in sync when the Library
                // clears/changes search internally (Esc, nav reset, etc.).
                _libraryPage.SearchTextChanged += (_, txt) =>
                {
                    if (TitleSearchBox.Text != txt) TitleSearchBox.Text = txt;
                };
            }

            if (param is LibraryNavParam lp)
            {
                _libraryPage.ApplyNavParam(lp);
            }
            // Any fresh library navigation hides the Browse-back button by
            // default. Drill-in callers (browse banners / collections)
            // re-arm it immediately after this returns.
            ClearLibraryBack();
            ContentFrame.Content = _libraryPage;
        }
        else if (page == "drives")
        {
            if (_drivesPage == null)
            {
                _drivesPage = new DrivesPage();
                _drivesPage.NavigateToLibrary += (_, serial) =>
                {
                    var drive = _vm.Drives.FirstOrDefault(d => d.VolumeSerial == serial);
                    NavigateTo("library", new LibraryNavParam(DriveSerial: serial, Label: drive?.Label));
                };
                _drivesPage.RefreshRequested += async (_, _) =>
                {
                    await _vm.RefreshSidebarAsync();
                    RefreshSidebar();
                };
            }
            _drivesPage.Refresh();
            ContentFrame.Content = _drivesPage;
        }
        else if (page == "statistics")
        {
            _statisticsPage ??= new StatisticsPage();
            _statisticsPage.Refresh();
            ContentFrame.Content = _statisticsPage;
        }
        else if (page == "browse" && param is DatabaseService.BrowseFacet facet)
        {
            _browsePage ??= new BrowsePage();
            _browsePage.Load(facet);
            ContentFrame.Content = _browsePage;
        }
        else if (page == "collections")
        {
            _collectionsPage ??= new CollectionsBrowsePage();
            _collectionsPage.Load();
            ContentFrame.Content = _collectionsPage;
        }
        else if (page == "tvshows")
        {
            if (_tvShowsPage == null)
            {
                _tvShowsPage = new TvShowsPage();
                _tvShowsPage.SidebarRefreshRequested += (_, _) => { _ = RefreshSidebarAsync(); };
            }
            _tvShowsPage.Load();
            ContentFrame.Content = _tvShowsPage;
        }
        else if (page == "onthisday")
        {
            if (_onThisDayPage == null)
            {
                _onThisDayPage = new OnThisDayPage();
                _onThisDayPage.BackRequested += (_, _) => { NavigateTo("library"); SetActiveNav(BtnAllMovies); };
            }
            _onThisDayPage.Load();
            ContentFrame.Content = _onThisDayPage;
        }
    }

    private async Task RefreshSidebarAsync()
    {
        await _vm.RefreshSidebarAsync();
        RefreshSidebar();
    }

    // ── Browse back navigation (v2.7) ─────────────────────────────────────
    // When the library view is drilled into from a Browse banner or the
    // Collections page, remember how to get back so LibraryPage can show
    // a "‹ By Rating" style button. Cleared on any other navigation.
    private Action? _libraryBackAction;

    public void SetLibraryBackToBrowse(DatabaseService.BrowseFacet facet)
    {
        _libraryBackAction = () => NavigateTo("browse", facet);
        _libraryPage?.ShowBrowseBack(facet switch
        {
            DatabaseService.BrowseFacet.Genre  => "By Genre",
            DatabaseService.BrowseFacet.Decade => "By Decade",
            DatabaseService.BrowseFacet.Rating => "By Rating",
            DatabaseService.BrowseFacet.Studio => "By Studio",
            _ => "Back",
        });
    }

    public void SetLibraryBackToCollections()
    {
        _libraryBackAction = () => { NavigateTo("collections"); SetActiveNav(BtnCollections); };
        _libraryPage?.ShowBrowseBack("Collections");
    }

    private void ClearLibraryBack()
    {
        _libraryBackAction = null;
        _libraryPage?.HideBrowseBack();
    }

    public void OnLibraryBackRequested() => _libraryBackAction?.Invoke();

    // ── Nav handlers ──────────────────────────────────────────────────────

    // ── Active sidebar nav state (v1.9.3) ─────────────────────────────────
    // Track the currently-highlighted button so we can swap styles. Each
    // handler calls SetActiveNav((Button)sender) to flip the visual state.
    private Button? _activeNavBtn;
    private Style? _navItemStyleCached;
    private Style? _navItemActiveStyleCached;

    private void SetActiveNav(Button? btn)
    {
        _navItemStyleCached       ??= (Style)Application.Current.Resources["NavItemStyle"];
        _navItemActiveStyleCached ??= (Style)Application.Current.Resources["NavItemActiveStyle"];
        if (_activeNavBtn != null && !ReferenceEquals(_activeNavBtn, btn))
            _activeNavBtn.Style = _navItemStyleCached;
        if (btn != null) btn.Style = _navItemActiveStyleCached;
        _activeNavBtn = btn;
    }

    private void OnNavAllMovies(object sender, RoutedEventArgs e)
    {
        NavigateTo("library", new LibraryNavParam());
        SetActiveNav(sender as Button ?? BtnAllMovies);
    }

    private void OnNavFavorites(object sender, RoutedEventArgs e)
    {
        NavigateTo("library", new LibraryNavParam(FavoritesOnly: true, Label: "Favorites"));
        SetActiveNav(sender as Button ?? BtnFavorites);
    }

    private void OnNavTvShows(object sender, RoutedEventArgs e)
    {
        NavigateTo("tvshows");
        SetActiveNav(sender as Button ?? BtnTvShows);
    }

    /// <summary>Open the TV page directly on a specific show (e.g. from a
    /// list's "TV shows in this list" row).</summary>
    public void OpenTvShow(int showId)
    {
        NavigateTo("tvshows");
        SetActiveNav(BtnTvShows);
        _tvShowsPage?.OpenShow(showId);
    }

    private void OnNavDrives(object sender, RoutedEventArgs e)
    {
        NavigateTo("drives");
        SetActiveNav(sender as Button ?? BtnDrives);
    }

    /// <summary>
    /// Public navigation hook used by the empty-state CTA on Library.
    /// Switches to the Drives page; the user proceeds with Add folder there.
    /// </summary>
    public void NavigateToDrivesAndAdd() => NavigateTo("drives");

    /// <summary>
    /// Public hooks used by the movie detail dialog to filter the library
    /// when the user clicks an actor / director / genre / studio chip.
    /// Each switches the main window to the Library page (creating it on first
    /// use), applies the filter, and updates the page header breadcrumb.
    /// </summary>
    public void NavigateLibraryByActor(string actor)
    {
        if (_libraryPage == null) NavigateTo("library");
        _libraryPage?.ViewModel.FilterByActor(actor);
        _libraryPage?.UpdatePageTitle($"All movies › {actor}");
        if (!ReferenceEquals(ContentFrame.Content, _libraryPage)) NavigateTo("library");
        ClearLibraryBack();  // chip-driven (e.g. from detail dialog) — no Browse origin
    }

    public void NavigateLibraryByDirector(string director)
    {
        if (_libraryPage == null) NavigateTo("library");
        _libraryPage?.ViewModel.FilterByDirector(director);
        _libraryPage?.UpdatePageTitle($"All movies › {director}");
        if (!ReferenceEquals(ContentFrame.Content, _libraryPage)) NavigateTo("library");
        ClearLibraryBack();
    }

    public void NavigateLibraryByGenre(string genre)
    {
        NavigateTo("library", new LibraryNavParam(Genre: genre, Label: genre));
    }

    /// <summary>v2.9 — deeplink: open the library filtered to a tag.</summary>
    public void NavigateLibraryByTag(string tagName)
    {
        if (_libraryPage == null) NavigateTo("library");
        try
        {
            var tagId = AppState.Instance.Db.EnsureTag(tagName);
            _libraryPage?.ViewModel.FilterByTag(tagId, tagName);
            if (!ReferenceEquals(ContentFrame.Content, _libraryPage)) NavigateTo("library");
            ClearLibraryBack();
        }
        catch { }
    }

    public void NavigateLibraryByStudio(string studio)
    {
        if (_libraryPage == null) NavigateTo("library");
        _libraryPage?.ViewModel.FilterByStudio(studio);
        _libraryPage?.UpdatePageTitle($"All movies › {studio}");
        if (!ReferenceEquals(ContentFrame.Content, _libraryPage)) NavigateTo("library");
    }

    public void NavigateLibraryByDecade(int decadeStart, string label)
    {
        if (_libraryPage == null) NavigateTo("library");
        _libraryPage?.ViewModel.FilterByDecade(decadeStart, label);
        _libraryPage?.UpdatePageTitle($"All movies › {label}");
        if (!ReferenceEquals(ContentFrame.Content, _libraryPage)) NavigateTo("library");
    }

    public void NavigateLibraryByRatingBand(string key, string label)
    {
        if (_libraryPage == null) NavigateTo("library");
        _libraryPage?.ViewModel.FilterByRatingBand(key, label);
        _libraryPage?.UpdatePageTitle($"All movies › {label}");
        if (!ReferenceEquals(ContentFrame.Content, _libraryPage)) NavigateTo("library");
    }

    public void NavigateLibraryByCollection(int id, string name)
    {
        NavigateTo("library", new LibraryNavParam(CollectionId: id, Label: name));
        SetLibraryBackToCollections();
    }

    private void OnNavBrowseGenre(object sender, RoutedEventArgs e)
    {
        NavigateTo("browse", DatabaseService.BrowseFacet.Genre);
        SetActiveNav(sender as Button ?? BtnBrowseGenre);
    }
    private void OnNavBrowseDecade(object sender, RoutedEventArgs e)
    {
        NavigateTo("browse", DatabaseService.BrowseFacet.Decade);
        SetActiveNav(sender as Button ?? BtnBrowseDecade);
    }
    private void OnNavBrowseRating(object sender, RoutedEventArgs e)
    {
        NavigateTo("browse", DatabaseService.BrowseFacet.Rating);
        SetActiveNav(sender as Button ?? BtnBrowseRating);
    }
    private void OnNavCollections(object sender, RoutedEventArgs e)
    {
        NavigateTo("collections");
        SetActiveNav(sender as Button ?? BtnCollections);
    }

    private void OnNavContinueWatching(object sender, RoutedEventArgs e)
    {
        if (_libraryPage == null) NavigateTo("library");
        _libraryPage?.ViewModel.ShowContinueWatching();
        if (!ReferenceEquals(ContentFrame.Content, _libraryPage)) NavigateTo("library");
        ClearLibraryBack();
        SetActiveNav(sender as Button ?? BtnContinueWatching);
    }

    private void OnNavRecentlyAdded(object sender, RoutedEventArgs e)
    {
        if (_libraryPage == null) NavigateTo("library");
        _libraryPage?.ViewModel.ShowRecentlyAdded();
        if (!ReferenceEquals(ContentFrame.Content, _libraryPage)) NavigateTo("library");
        ClearLibraryBack();
        SetActiveNav(sender as Button ?? BtnRecentlyAdded);
    }

    private void OnNavRecentlyWatched(object sender, RoutedEventArgs e)
    {
        if (_libraryPage == null) NavigateTo("library");
        _libraryPage?.ViewModel.ShowRecentlyWatched();
        if (!ReferenceEquals(ContentFrame.Content, _libraryPage)) NavigateTo("library");
        ClearLibraryBack();
        SetActiveNav(sender as Button ?? BtnRecentlyWatched);
    }

    /// <summary>
    /// v2.9 — Opens the Backup dialog: Export or Import personal state.
    /// </summary>
    private async void OnNavBackup(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "Save or restore everything personal — favorites, watchlist, notes, " +
                   "lists, tags, watched flags, and watch history. The exported JSON file " +
                   "is portable: import it on another PC to merge your state in.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextBrush"],
        });
        var status = new TextBlock
        {
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["MutedBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };

        var exportBtn = new Button
        {
            Content = "📤  Export backup…",
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BrandPurpleBrush"],
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 8, 14, 8),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        };
        var importBtn = new Button
        {
            Content = "📥  Import backup…",
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBrush"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 8, 14, 8),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(exportBtn);
        row.Children.Add(importBtn);
        panel.Children.Add(row);
        panel.Children.Add(status);

        exportBtn.Click += async (_, _) =>
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileSavePicker
                {
                    SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
                    SuggestedFileName = $"cinelibrary-backup-{DateTime.Now:yyyyMMdd-HHmmss}",
                };
                picker.FileTypeChoices.Add("CineLibrary backup", new List<string> { ".json" });
                WinRT.Interop.InitializeWithWindow.Initialize(picker,
                    WinRT.Interop.WindowNative.GetWindowHandle(this));
                var file = await picker.PickSaveFileAsync();
                if (file == null) return;
                status.Visibility = Visibility.Visible;
                status.Text = "Building backup…";
                exportBtn.IsEnabled = false; importBtn.IsEnabled = false;
                await Task.Run(() =>
                {
                    var snapshot = BackupService.BuildSnapshot(AppState.Instance.Db, AppVersionString());
                    BackupService.WriteToFile(snapshot, file.Path);
                });
                status.Text = $"Saved to {file.Path}";
            }
            catch (Exception ex)
            {
                status.Visibility = Visibility.Visible;
                status.Text = $"Export failed: {ex.Message}";
            }
            finally { exportBtn.IsEnabled = true; importBtn.IsEnabled = true; }
        };

        importBtn.Click += async (_, _) =>
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker
                {
                    SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
                };
                picker.FileTypeFilter.Add(".json");
                WinRT.Interop.InitializeWithWindow.Initialize(picker,
                    WinRT.Interop.WindowNative.GetWindowHandle(this));
                var file = await picker.PickSingleFileAsync();
                if (file == null) return;
                status.Visibility = Visibility.Visible;
                status.Text = "Reading backup…";
                exportBtn.IsEnabled = false; importBtn.IsEnabled = false;
                BackupService.ImportResult? result = null;
                await Task.Run(() =>
                {
                    var b = BackupService.ReadFromFile(file.Path);
                    if (b == null) throw new InvalidOperationException("File isn't a CineLibrary backup");
                    result = BackupService.Import(AppState.Instance.Db, b);
                });
                if (result != null)
                {
                    status.Text =
                        $"Imported: {result.MoviesMerged} movies, {result.ShowsMerged} shows, " +
                        $"{result.EpisodesMerged} episodes, {result.ListsCreated} new lists, " +
                        $"{result.TagsCreated} new tags, {result.EventsAppended} history events. " +
                        $"Skipped: {result.MoviesSkipped + result.ShowsSkipped} (drive not mounted).";
                    _ = RefreshSidebarAsync();
                }
            }
            catch (Exception ex)
            {
                status.Visibility = Visibility.Visible;
                status.Text = $"Import failed: {ex.Message}";
            }
            finally { exportBtn.IsEnabled = true; importBtn.IsEnabled = true; }
        };

        var dlg = new ContentDialog
        {
            Title = "🔒  Backup",
            Content = panel,
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = CurrentTheme,
        };
        try { await dlg.ShowAsync(); } catch { }
    }

    /// <summary>Best-effort assembly version string for backup metadata.</summary>
    private static string AppVersionString()
    {
        try
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return v?.ToString(3) ?? "";
        }
        catch { return ""; }
    }

    private async void OnNavRandomPick(object sender, RoutedEventArgs e)
    {
        var id = AppState.Instance.Db.GetRandomUnwatchedId(AppState.Instance.Connected);
        if (id == null)
        {
            ShowToast("Nothing unwatched left — fully caught up 🎉");
            return;
        }
        var dialog = new MovieDetailDialog(id.Value);
        dialog.WatchlistChanged += (_, _) => { _ = RefreshSidebarAsync(); };
        dialog.Activate();
    }

    /// <summary>
    /// v2.9 — Navigates to the dedicated On This Day page. The sidebar
    /// entry is hidden when there are no matches today, so this handler
    /// can assume there's content to show.
    /// </summary>
    private void OnNavOnThisDay(object sender, RoutedEventArgs e)
    {
        NavigateTo("onthisday");
        ClearLibraryBack();
        SetActiveNav(sender as Button ?? BtnOnThisDay);
    }

    private void OnNavDriveItem(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string serial)
        {
            var drive = _vm.Drives.FirstOrDefault(d => d.VolumeSerial == serial);
            NavigateTo("library", new LibraryNavParam(DriveSerial: serial, Label: drive?.Label));
        }
    }

    private void OnNavCollection(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        // WinRT can box an int as Int64 when it goes through {Binding} — accept both.
        int id;
        if (btn.Tag is int i)       id = i;
        else if (btn.Tag is long l) id = (int)l;
        else return;

        var col = _vm.Collections.FirstOrDefault(c => c.Id == id);
        NavigateTo("library", new LibraryNavParam(CollectionId: id, Label: col?.Name));
    }

    private void OnNavGenre(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string genre)
            NavigateTo("library", new LibraryNavParam(Genre: genre, Label: genre));
    }

    // ── v1.3 New Navigation ────────────────────────────────────────────────

    private void OnNavWatchlist(object sender, RoutedEventArgs e)
    {
        if (_libraryPage?.ViewModel is LibraryViewModel vm)
        {
            vm.ShowWatchlist();
            NavigateTo("library");
        }
        SetActiveNav(sender as Button ?? BtnWatchlist);
    }

    private void OnNavNotes(object sender, RoutedEventArgs e)
    {
        if (_libraryPage == null) NavigateTo("library");
        _libraryPage?.ViewModel.ShowNotes();
        if (!ReferenceEquals(ContentFrame.Content, _libraryPage)) NavigateTo("library");
        ClearLibraryBack();
        SetActiveNav(sender as Button ?? BtnNotes);
    }

    // ── v1.4.1 Statistics dashboard ────────────────────────────────────────

    private void OnNavStatistics(object sender, RoutedEventArgs e)
    {
        NavigateTo("statistics");
        SetActiveNav(sender as Button ?? BtnStatistics);
    }

    // ── v1.4.1 Keyboard shortcuts dialog ──────────────────────────────────

    private async void OnHelpClick(object sender, RoutedEventArgs e)
        => await ShowShortcutsDialogAsync();

    private async Task ShowShortcutsDialogAsync()
    {
        var panel = new StackPanel { Spacing = 10, MinWidth = 360 };

        void AddRow(string keys, string what)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var kb = new Border
            {
                Background = (SolidColorBrush)Application.Current.Resources["CardBrush"],
                BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            kb.Child = new TextBlock { Text = keys, FontSize = 12, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") };
            kb.SetValue(Grid.ColumnProperty, 0);
            row.Children.Add(kb);

            var desc = new TextBlock
            {
                Text = what,
                FontSize = 13,
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            };
            desc.SetValue(Grid.ColumnProperty, 1);
            row.Children.Add(desc);

            panel.Children.Add(row);
        }

        void AddHeader(string text)
        {
            panel.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                CharacterSpacing = 120,
                Opacity = 0.7,
                Margin = new Thickness(0, 10, 0, 2),
                Foreground = (SolidColorBrush)Application.Current.Resources["MutedBrush"],
            });
        }

        AddHeader("SEARCH & GENERAL");
        AddRow("/",                "Focus the search box");
        AddRow("Ctrl + F",         "Focus the search box");
        AddRow("Esc",              "Clear search, then clear selection");
        AddRow("Ctrl + B",         "Toggle the sidebar");
        AddRow("Ctrl + Shift + /", "Show this shortcuts dialog");
        AddRow("Ctrl + Q",         "Quit CineLibrary");

        AddHeader("SELECTION & ACTIONS");
        AddRow("Ctrl + A",     "Select every card on screen");
        AddRow("Ctrl + click", "Add / remove a single card");
        AddRow("Shift + click","Range-select from the last card");
        AddRow("F",            "Toggle favorite on the selection");
        AddRow("W",            "Toggle watchlist on the selection");
        AddRow("Delete",       "Remove selection from the current list");

        AddHeader("NAVIGATION");
        AddRow("PgDn / PgUp",  "Scroll one viewport");
        AddRow("Home / End",   "Jump to top / bottom");
        AddRow("↑ / ↓", "Scroll by one row of cards");

        var note = new TextBlock
        {
            Text = "Card shortcuts (F, W, Delete) act on the current selection — "
                 + "click one or more cards first. They pause while you're typing in the search box.",
            FontSize = 12,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 14, 0, 0),
            Foreground = (SolidColorBrush)Application.Current.Resources["MutedBrush"],
        };

        var dialog = new ContentDialog
        {
            Title = "Keyboard shortcuts",
            Content = new ScrollViewer
            {
                Content = new StackPanel { Children = { panel, note } },
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 520,
            },
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = CurrentTheme,
        };
        await dialog.ShowAsync();
    }

    // ── About ─────────────────────────────────────────────────────────────

    private async void OnAboutClick(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = "A fast, native movie catalog for MediaElch-scraped collections.",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Browse, search and play your movies across multiple external drives.",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Built with C# + WinUI 3.",
            TextWrapping = TextWrapping.Wrap,
        });
        var link = new HyperlinkButton
        {
            Content = "github.com/aungkokomm/CineLibraryCS",
            NavigateUri = new Uri("https://github.com/aungkokomm/CineLibraryCS"),
            Padding = new Thickness(0),
        };
        panel.Children.Add(link);

        var dialog = new ContentDialog
        {
            Title = "CineLibrary v3.2.0",
            Content = panel,
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = CurrentTheme,
        };
        await dialog.ShowAsync();
    }

    // ── Toast ─────────────────────────────────────────────────────────────

    public void ShowToast(string message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var myGen = ++_toastGeneration;
            ToastText.Text = message;
            ToastActionBtn.Visibility = Visibility.Collapsed;
            // Plain toast — no undo, so clear any pending action so the
            // user clicking the ✕ doesn't accidentally fire a stale undo.
            _pendingUndo = null;
            ToastBorder.Visibility = Visibility.Visible;
            AnimateToastIn();
            ScheduleToastHide(myGen);
        });
    }

    /// <summary>
    /// v2.6 — slide the toast up from 20 px below to 0 over ~180 ms. Run
    /// every time the toast becomes visible so each new toast animates,
    /// even if the previous one was still on screen.
    /// </summary>
    private void AnimateToastIn()
    {
        ToastSlide.Y = 20;
        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        var anim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = 20, To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase
                { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut },
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(anim, ToastSlide);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(anim, "Y");
        sb.Children.Add(anim);
        sb.Begin();
    }

    private void OnToastDismiss(object sender, RoutedEventArgs e)
    {
        // Dismissing an update toast = "skip this version, don't nag again".
        if (!string.IsNullOrEmpty(_pendingUpdateVersion))
        {
            try { AppState.Instance.SetPref("skippedUpdate", _pendingUpdateVersion); } catch { }
            _pendingUpdateVersion = null;
            _pendingUpdateUrl = null;
        }
        ToastBorder.Visibility = Visibility.Collapsed;
    }

    // ── Sidebar collapse ──────────────────────────────────────────────────

    private bool _sidebarCollapsed;

    private void OnToggleSidebar(object sender, RoutedEventArgs e)
        => ApplySidebarCollapsed(!_sidebarCollapsed);

    private void ApplySidebarCollapsed(bool collapsed)
    {
        _sidebarCollapsed = collapsed;
        if (collapsed)
        {
            SidebarCol.Width = new GridLength(0);
            // Hide the whole floating card (not just the inner grid) so no
            // rounded sliver / margin is left behind when collapsed.
            SidebarCard.Visibility = Visibility.Collapsed;
            SidebarReopenBtn.Visibility = Visibility.Visible;
        }
        else
        {
            SidebarCol.Width = new GridLength(252);   // keep in sync with SidebarCol default (240 inner + 12 margin)
            SidebarCard.Visibility = Visibility.Visible;
            SidebarReopenBtn.Visibility = Visibility.Collapsed;
        }
        AppState.Instance.SetPref("sidebarCollapsed", collapsed ? "true" : "false");
    }
}

public record LibraryNavParam(
    string? DriveSerial = null,
    string? Genre = null,
    int? CollectionId = null,
    bool FavoritesOnly = false,
    bool RecentlyAdded = false,
    string? Label = null
);
