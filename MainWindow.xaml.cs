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

        // Global Ctrl+B to toggle sidebar
        var acc = new KeyboardAccelerator { Key = VirtualKey.B, Modifiers = VirtualKeyModifiers.Control };
        acc.Invoked += (_, a) => { ApplySidebarCollapsed(!_sidebarCollapsed); a.Handled = true; };
        RootGrid.KeyboardAccelerators.Add(acc);

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

            DrivesRepeater.ItemsSource = _vm.Drives;
            LibrariesHeader.Visibility = _vm.Drives.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            CollectionsRepeater.ItemsSource = _vm.Collections;
            CollectionsHeader.Visibility = _vm.Collections.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            GenresRepeater.ItemsSource = _vm.TopGenres;
            GenresHeader.Visibility = _vm.TopGenres.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        });
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
            Title = "CineLibrary v1.0",
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
            ToastBorder.Visibility = Visibility.Visible;
        });
        Task.Delay(6000).ContinueWith(_ =>
            DispatcherQueue.TryEnqueue(() => ToastBorder.Visibility = Visibility.Collapsed));
    }

    private void OnToastDismiss(object sender, RoutedEventArgs e)
        => ToastBorder.Visibility = Visibility.Collapsed;

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
