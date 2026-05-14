using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using CineLibraryCS.Models;
using CineLibraryCS.Services;
using CineLibraryCS.ViewModels;
using CineLibraryCS.Views;
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

        InitializeComponent();

        // Extend content into titlebar for Mica effect + use our custom drag region
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Apply Mica material
        SystemBackdrop = new MicaBackdrop();

        // Window size
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(1400, 900));
        appWindow.Title = "CineLibrary";

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
            _libraryPage?.FocusSearchBox();
            a.Handled = true;
        };
        RootGrid.KeyboardAccelerators.Add(searchAcc);

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
        ThemeBtn.Content = theme switch
        {
            ElementTheme.Dark    => "🌙",
            ElementTheme.Light   => "☀️",
            _                    => "🖥️",
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

        await _vm.InitializeAsync();
        RefreshSidebar();
        NavigateTo("library");
        // Initial active highlight on All Movies (v1.9.3)
        SetActiveNav(BtnAllMovies);

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

    private async void OnToastActionClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_pendingUpdateUrl)) return;
        try { await Windows.System.Launcher.LaunchUriAsync(new Uri(_pendingUpdateUrl)); }
        catch { }
        ToastBorder.Visibility = Visibility.Collapsed;
    }

    // ── Sidebar refresh ───────────────────────────────────────────────────

    public void RefreshSidebar()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var stats = _vm.Stats;
            TotalBadge.Text = stats?.TotalMovies.ToString() ?? "0";
            DrivesBadge.Text = _vm.Drives.Count.ToString();
            StatRuntime.Text = stats?.TotalRuntimeText ?? "—";
            StatRating.Text = stats?.AvgRatingText ?? "—";

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

            DrivesRepeater.ItemsSource = _vm.Drives;
            LibrariesHeader.Visibility = _vm.Drives.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            // v2.1: COLLECTIONS + TOP GENRES sub-sections removed. Both live
            // as BROWSE pages now (Collections grid, By Genre banners).

            RefreshUserLists();
        });
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

    private Button BuildUserListButton(DatabaseService.UserList ul)
    {
        var btn = new Button
        {
            Style = (Style)Application.Current.Resources["NavItemStyle"],
            Tag = ul.Id,
        };
        btn.Click += (_, _) => _libraryPage?.UpdatePageTitle($"📑 {ul.Name.ToUpper()}");
        btn.Click += (_, _) =>
        {
            if (_libraryPage == null) NavigateTo("library");
            _libraryPage?.ViewModel.ShowUserList(ul.Id, ul.Name);
            if (ContentFrame.Content != _libraryPage) NavigateTo("library");
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

        // Right-click context menu: copy / rename / delete
        var menu = new MenuFlyout();
        var copyItem = new MenuFlyoutItem { Text = "📂 Copy movies to folder…" };
        copyItem.Click += async (_, _) => await CopyListToFolder(ul);
        var renameItem = new MenuFlyoutItem { Text = "Rename" };
        renameItem.Click += async (_, _) => await PromptRenameUserList(ul);
        var deleteItem = new MenuFlyoutItem { Text = "Delete list" };
        deleteItem.Click += async (_, _) => await ConfirmDeleteUserList(ul);
        menu.Items.Add(copyItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(renameItem);
        menu.Items.Add(deleteItem);
        btn.ContextFlyout = menu;
        return btn;
    }

    /// <summary>
    /// Bucket-style export — pick a destination folder, then copy each
    /// online movie's source folder into it.
    /// </summary>
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
            _             => (null!,                                  null!),
        };
        if (repeater == null) return;
        var collapsed = repeater.Visibility == Visibility.Collapsed;
        repeater.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        chevron.Text = collapsed ? "⌃" : "⌄"; // ⌃ open / ⌄ closed
    }

    // ── Navigation ────────────────────────────────────────────────────────

    private void NavigateTo(string page, object? param = null)
    {
        if (page == "library")
        {
            if (_libraryPage == null)
            {
                _libraryPage = new LibraryPage();
                _libraryPage.SidebarRefreshRequested += (_, _) => { _ = RefreshSidebarAsync(); };
            }

            if (param is LibraryNavParam lp)
            {
                _libraryPage.ApplyNavParam(lp);
            }
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
    }

    private async Task RefreshSidebarAsync()
    {
        await _vm.RefreshSidebarAsync();
        RefreshSidebar();
    }

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
        _libraryPage?.UpdatePageTitle($"ALL MOVIES › {actor.ToUpper()}");
        if (ContentFrame.Content != _libraryPage) NavigateTo("library");
    }

    public void NavigateLibraryByDirector(string director)
    {
        if (_libraryPage == null) NavigateTo("library");
        _libraryPage?.ViewModel.FilterByDirector(director);
        _libraryPage?.UpdatePageTitle($"ALL MOVIES › {director.ToUpper()}");
        if (ContentFrame.Content != _libraryPage) NavigateTo("library");
    }

    public void NavigateLibraryByGenre(string genre)
    {
        NavigateTo("library", new LibraryNavParam(Genre: genre, Label: genre));
    }

    public void NavigateLibraryByStudio(string studio)
    {
        if (_libraryPage == null) NavigateTo("library");
        _libraryPage?.ViewModel.FilterByStudio(studio);
        _libraryPage?.UpdatePageTitle($"ALL MOVIES › {studio.ToUpper()}");
        if (ContentFrame.Content != _libraryPage) NavigateTo("library");
    }

    public void NavigateLibraryByDecade(int decadeStart, string label)
    {
        if (_libraryPage == null) NavigateTo("library");
        _libraryPage?.ViewModel.FilterByDecade(decadeStart, label);
        _libraryPage?.UpdatePageTitle($"ALL MOVIES › {label.ToUpper()}");
        if (ContentFrame.Content != _libraryPage) NavigateTo("library");
    }

    public void NavigateLibraryByRatingBand(string key, string label)
    {
        if (_libraryPage == null) NavigateTo("library");
        _libraryPage?.ViewModel.FilterByRatingBand(key, label);
        _libraryPage?.UpdatePageTitle($"ALL MOVIES › {label.ToUpper()}");
        if (ContentFrame.Content != _libraryPage) NavigateTo("library");
    }

    public void NavigateLibraryByCollection(int id, string name)
    {
        NavigateTo("library", new LibraryNavParam(CollectionId: id, Label: name));
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
        if (ContentFrame.Content != _libraryPage) NavigateTo("library");
        SetActiveNav(sender as Button ?? BtnContinueWatching);
    }

    private void OnNavRecentlyAdded(object sender, RoutedEventArgs e)
    {
        if (_libraryPage == null) NavigateTo("library");
        _libraryPage?.ViewModel.ShowRecentlyAdded();
        if (ContentFrame.Content != _libraryPage) NavigateTo("library");
        SetActiveNav(sender as Button ?? BtnRecentlyAdded);
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
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var kb = new Border
            {
                Background = (SolidColorBrush)Application.Current.Resources["CardBrush"],
                BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
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

        AddRow("Ctrl + F",      "Focus search box");
        AddRow("Esc",           "Clear search");
        AddRow("Ctrl + B",      "Toggle sidebar");
        AddRow("Ctrl + Shift + /", "Show this shortcuts dialog");
        AddRow("Ctrl + Q",      "Quit CineLibrary");

        var dialog = new ContentDialog
        {
            Title = "Keyboard shortcuts",
            Content = panel,
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
            Title = "CineLibrary v2.1.0",
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
            ToastText.Text = message;
            ToastActionBtn.Visibility = Visibility.Collapsed;
            ToastBorder.Visibility = Visibility.Visible;
        });
        Task.Delay(6000).ContinueWith(_ =>
            DispatcherQueue.TryEnqueue(() => ToastBorder.Visibility = Visibility.Collapsed));
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
            SidebarGrid.Visibility = Visibility.Collapsed;
            SidebarReopenBtn.Visibility = Visibility.Visible;
        }
        else
        {
            SidebarCol.Width = new GridLength(260);
            SidebarGrid.Visibility = Visibility.Visible;
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
