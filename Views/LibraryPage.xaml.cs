using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;
using CineLibraryCS.Models;
using CineLibraryCS.Services;
using CineLibraryCS.ViewModels;
using CineLibraryCS.Views;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CineLibraryCS.Views;

public sealed partial class LibraryPage : Page
{
    private readonly LibraryViewModel _vm;
    public LibraryViewModel ViewModel => _vm;
    public event EventHandler? SidebarRefreshRequested;

    // Set to true only after construction finishes. XAML-driven events
    // (ComboBox.SelectionChanged on IsSelected="True", etc.) can fire during
    // InitializeComponent() — we must not touch _vm from those before it's wired up.
    private bool _ready;

    public LibraryPage()
    {
        // _vm MUST be created before InitializeComponent() — the SortCombo's
        // initial SelectionChanged fires inside InitializeComponent and touches _vm.
        _vm = new LibraryViewModel();

        InitializeComponent();

        // Assign ItemsSource once — ObservableCollection handles all future updates
        GridRepeater.ItemsSource = _vm.Movies;
        ListRepeater.ItemsSource = _vm.Movies;

        // Reflect saved prefs in the UI (sort dropdown, grid/list toggle)
        SyncUiFromVm();

        // Load density pref (S/M/L/XL)
        ApplyDensity(AppState.Instance.GetPref("gridDensity", "M"));

        // Keyboard shortcuts
        AddAccelerator(VirtualKey.F, VirtualKeyModifiers.Control, (_, a) =>
        {
            SearchBox.Focus(FocusState.Programmatic);
            a.Handled = true;
        });
        AddAccelerator(VirtualKey.Escape, VirtualKeyModifiers.None, (_, a) =>
        {
            if (!string.IsNullOrEmpty(SearchBox.Text))
            {
                SearchBox.Text = "";
                _vm.SearchText = "";
                a.Handled = true;
            }
        });

        _vm.PropertyChanged += OnVmPropertyChanged;
        _vm.Movies.CollectionChanged += (_, _) => UpdateEmptyState();
        _ready = true;

        _ = _vm.LoadAsync();
    }

    private void AddAccelerator(VirtualKey key, VirtualKeyModifiers mods,
        Windows.Foundation.TypedEventHandler<KeyboardAccelerator, KeyboardAcceleratorInvokedEventArgs> handler)
    {
        var acc = new KeyboardAccelerator { Key = key, Modifiers = mods };
        acc.Invoked += handler;
        KeyboardAccelerators.Add(acc);
    }

    private void UpdateEmptyState()
    {
        var empty = _vm.Movies.Count == 0 && !_vm.IsLoading;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        GridBorder.Opacity = empty ? 0 : 1;
        ListBorder.Opacity = empty ? 0 : 1;
    }

    // ── Density (S/M/L/XL) ────────────────────────────────────────────────

    private void OnDensityClick(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (sender is not Microsoft.UI.Xaml.Controls.Primitives.ToggleButton btn || btn.Tag is not string tag) return;
        ApplyDensity(tag);
        AppState.Instance.SetPref("gridDensity", tag);
    }

    private void ApplyDensity(string tag)
    {
        var (w, h) = tag switch
        {
            "S"  => (120.0, 220.0),
            "L"  => (190.0, 340.0),
            "XL" => (240.0, 420.0),
            _    => (150.0, 280.0),   // M
        };

        MovieCardControl.SetGlobalSize(w, h);
        GridLayout.MinItemWidth = w;
        GridLayout.MinItemHeight = h;

        DensityS.IsChecked  = tag == "S";
        DensityM.IsChecked  = tag == "M";
        DensityL.IsChecked  = tag == "L";
        DensityXL.IsChecked = tag == "XL";
    }

    private void SyncUiFromVm()
    {
        // Sort combo — match the item whose Tag corresponds to current SortKey+SortDir
        var keyStr = _vm.SortKey switch
        {
            SortKey.Year      => "year",
            SortKey.Rating    => "rating",
            SortKey.Runtime   => "runtime",
            SortKey.DateAdded => "date_added",
            _                 => "title"
        };
        var dirStr = _vm.SortDir == SortDir.Asc ? "asc" : "desc";
        var wantTag = $"{keyStr}:{dirStr}";
        for (int i = 0; i < SortCombo.Items.Count; i++)
        {
            if (SortCombo.Items[i] is ComboBoxItem ci && (ci.Tag as string) == wantTag)
            {
                SortCombo.SelectedIndex = i;
                break;
            }
        }
        if (SortCombo.SelectedIndex < 0) SortCombo.SelectedIndex = 0;

        // View mode toggle
        if (_vm.ViewMode == ViewMode.List)
        {
            GridViewToggle.IsChecked = false;
            ListViewToggle.IsChecked = true;
            GridBorder.Visibility = Visibility.Collapsed;
            ListBorder.Visibility = Visibility.Visible;
        }
        else
        {
            GridViewToggle.IsChecked = true;
            ListViewToggle.IsChecked = false;
            GridBorder.Visibility = Visibility.Visible;
            ListBorder.Visibility = Visibility.Collapsed;
        }
    }

    public void ApplyNavParam(LibraryNavParam p)
    {
        SearchBox.Text = "";
        _vm.SearchText = "";

        if (p.FavoritesOnly)
            _vm.SetFavorites();
        else if (p.DriveSerial != null)
            _vm.SetDriveFilter(p.DriveSerial, p.Label);
        else if (p.Genre != null)
            _vm.SetGenreFilter(p.Genre);
        else if (p.CollectionId != null)
            _vm.SetCollectionFilter(p.CollectionId, p.Label);
        else
            _vm.ClearFilters();

        // Breadcrumb-style title: "ALL MOVIES › DRAMA"
        PageTitleText.Text = p.Label == null
            ? "ALL MOVIES"
            : $"ALL MOVIES › {p.Label.ToUpper()}";
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (e.PropertyName == nameof(LibraryViewModel.TotalCount) ||
                e.PropertyName == nameof(LibraryViewModel.HasMore))
            {
                MovieCountText.Text = _vm.HasMore
                    ? $"{_vm.TotalCount}+ movies"
                    : $"{_vm.TotalCount} movies";
            }
            if (e.PropertyName == nameof(LibraryViewModel.IsLoading))
            {
                LoadingRing.IsActive = _vm.IsLoading;
                LoadingRing.Visibility = _vm.IsLoading ? Visibility.Visible : Visibility.Collapsed;
                UpdateEmptyState();
            }
        });
    }

    // ── Search ────────────────────────────────────────────────────────────

    private void OnSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (!_ready) return;
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            _vm.SearchText = sender.Text;
    }

    // ── Sort ──────────────────────────────────────────────────────────────

    private void OnSortChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (SortCombo.SelectedItem is not ComboBoxItem item) return;
        var parts = (item.Tag as string ?? "title:asc").Split(':');
        _vm.SortKey = parts[0] switch
        {
            "year"       => SortKey.Year,
            "rating"     => SortKey.Rating,
            "runtime"    => SortKey.Runtime,
            "date_added" => SortKey.DateAdded,
            _            => SortKey.Title
        };
        _vm.SortDir = parts.Length > 1 && parts[1] == "desc" ? SortDir.Desc : SortDir.Asc;
    }

    // ── View mode ─────────────────────────────────────────────────────────

    private void OnViewGrid(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        GridViewToggle.IsChecked = true;
        ListViewToggle.IsChecked  = false;
        GridBorder.Visibility = Visibility.Visible;
        ListBorder.Visibility = Visibility.Collapsed;
        _vm.ViewMode = ViewMode.Grid;
    }

    private void OnViewList(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        ListViewToggle.IsChecked  = true;
        GridViewToggle.IsChecked = false;
        GridBorder.Visibility = Visibility.Collapsed;
        ListBorder.Visibility = Visibility.Visible;
        _vm.ViewMode = ViewMode.List;
    }

    // ── Watched filter ────────────────────────────────────────────────────

    private void OnWatchedFilter(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (sender is not Button btn) return;
        var pill = (Style)Application.Current.Resources["PillButtonStyle"];
        var pillActive = (Style)Application.Current.Resources["PillButtonActiveStyle"];
        FilterAll.Style       = pill;
        FilterWatched.Style   = pill;
        FilterUnwatched.Style = pill;
        btn.Style = pillActive;

        _vm.WatchedFilter = (btn.Tag as string) switch
        {
            "watched"   => WatchedFilter.Watched,
            "unwatched" => WatchedFilter.Unwatched,
            _           => WatchedFilter.All
        };
    }

    // ── Infinite scroll ───────────────────────────────────────────────────

    private void OnScrollChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        if (sv.VerticalOffset >= sv.ScrollableHeight - 300 && _vm.HasMore && !_vm.IsLoading)
            _ = _vm.LoadMoreAsync();
    }

    // ── Export ────────────────────────────────────────────────────────────

    private async void OnExportCsvDefault(SplitButton sender, SplitButtonClickEventArgs e)
        => await ExportCsvCore();

    private async void OnExportCsv(object sender, RoutedEventArgs e)
        => await ExportCsvCore();

    private async Task ExportCsvCore()
    {
        var path = await PickSaveFile("CSV file", ".csv", "movies_export");
        if (path == null) return;
        var mainVm = new MainViewModel();
        await mainVm.ExportCsvAsync(_vm.Movies, path);
        App.MainWindow?.ShowToast("Exported to CSV");
    }

    private async void OnExportHtml(object sender, RoutedEventArgs e)
    {
        var path = await PickSaveFile("HTML file", ".html", "movies_export");
        if (path == null) return;
        var mainVm = new MainViewModel();
        await mainVm.ExportHtmlAsync(_vm.Movies, path);
        App.MainWindow?.ShowToast("Exported to HTML");
    }

    private async Task<string?> PickSaveFile(string type, string ext, string suggestedName)
    {
        var picker = new FileSavePicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add(type, new List<string> { ext });
        picker.SuggestedFileName = suggestedName;
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    // ── Back/Toggle sidebar button ────────────────────────────────────────

    public void ShowBackButton(bool show)
    {
        // Button is already defined in XAML with Visibility="Collapsed"
        // This will be called by MainWindow to show/hide it
    }

    private void OnBackToggleClick(object sender, RoutedEventArgs e)
    {
        // Get the main window and toggle sidebar
        if (App.MainWindow is MainWindow mainWindow)
        {
            var toggleSidebarMethod = typeof(MainWindow).GetMethod("OnToggleSidebar", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            toggleSidebarMethod?.Invoke(mainWindow, new object[] { sender, e });
        }
    }
}

