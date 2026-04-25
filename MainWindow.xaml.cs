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
    private DeviceChangeWatcher? _deviceWatcher;

    public MainWindow()
    {
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
        AppState.Instance.Initialize();

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

            DrivesRepeater.ItemsSource = _vm.Drives;
            LibrariesHeader.Visibility = _vm.Drives.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            CollectionsRepeater.ItemsSource = _vm.Collections;
            CollectionsHeader.Visibility = _vm.Collections.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            GenresRepeater.ItemsSource = _vm.TopGenres;
            GenresHeader.Visibility = _vm.TopGenres.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    // ── Sidebar section collapse/expand ───────────────────────────────────

    private void OnSidebarSectionToggle(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        var (repeater, chevron) = tag switch
        {
            "Libraries"   => ((FrameworkElement)DrivesRepeater,      LibrariesChevron),
            "Genres"      => (GenresRepeater,                        GenresChevron),
            "Collections" => (CollectionsRepeater,                   CollectionsChevron),
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
    }

    private async Task RefreshSidebarAsync()
    {
        await _vm.RefreshSidebarAsync();
        RefreshSidebar();
    }

    // ── Nav handlers ──────────────────────────────────────────────────────

    private void OnNavAllMovies(object sender, RoutedEventArgs e)
        => NavigateTo("library", new LibraryNavParam());

    private void OnNavFavorites(object sender, RoutedEventArgs e)
        => NavigateTo("library", new LibraryNavParam(FavoritesOnly: true, Label: "Favorites"));

    private void OnNavDrives(object sender, RoutedEventArgs e)
        => NavigateTo("drives");

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
    }

    // ── v1.4.1 Statistics dashboard ────────────────────────────────────────

    private void OnNavStatistics(object sender, RoutedEventArgs e)
        => NavigateTo("statistics");

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
            Title = "CineLibrary v1.7.0",
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
    string? Label = null
);
