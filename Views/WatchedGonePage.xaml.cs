using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;
using CineLibraryCS.Models;
using CineLibraryCS.Services;
using CineLibraryCS.ViewModels;

namespace CineLibraryCS.Views;

/// <summary>
/// v3.3 — Watched &amp; Gone: the records page. Movies the user watched and
/// then deleted from disk live here, isolated from the main library, with
/// poster + metadata + notes + watch history intact. Cards run in
/// ArchiveMode, so their context menu offers Restore / Delete record.
/// </summary>
public sealed partial class WatchedGonePage : Page
{
    public event EventHandler? SidebarRefreshRequested;

    private string _search = "";
    private string _sortKey = "archived";
    private string _sortDir = "desc";
    private List<MovieListItem> _records = new();
    private bool _ready;

    public WatchedGonePage()
    {
        InitializeComponent();
        // Restore / Delete record actions raise this; reload so the card
        // disappears (or the count updates) immediately. Re-subscribed on
        // every Loaded because this page instance is cached and re-shown —
        // a ctor-only subscription would die at the first Unloaded.
        MovieCardControl.AnyMovieArchived += OnArchiveChanged;
        Loaded += (_, _) =>
        {
            MovieCardControl.AnyMovieArchived -= OnArchiveChanged;
            MovieCardControl.AnyMovieArchived += OnArchiveChanged;
        };
        Unloaded += (_, _) => MovieCardControl.AnyMovieArchived -= OnArchiveChanged;
        _ready = true;
        Refresh();
    }

    private void OnArchiveChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            Refresh();
            SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    public void Refresh()
    {
        // Records are a bounded set (tens to a few hundred) — load in one go
        // through the same pipeline as the library, flipped to ArchivedOnly so
        // search / sort behave identically.
        _records = AppState.Instance.Db.GetMovies(new DatabaseService.ListOptions(
            ArchivedOnly: true,
            Search: string.IsNullOrWhiteSpace(_search) ? null : _search,
            SortKey: _sortKey, SortDir: _sortDir,
            Limit: 10000, Offset: 0), AppState.Instance.Connected);

        RecordsRepeater.ItemsSource = _records;
        CountText.Text = _records.Count == 1 ? "1 record" : $"{_records.Count:N0} records";
        EmptyState.Visibility = _records.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (!_ready || args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        _search = sender.Text ?? "";
        Refresh();
    }

    private void OnSortChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (SortCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            var parts = tag.Split(':');
            _sortKey = parts[0];
            _sortDir = parts.Length > 1 ? parts[1] : "asc";
            Refresh();
        }
    }

    private async void OnExportCsv(object sender, RoutedEventArgs e)
    {
        if (_records.Count == 0) return;
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeChoices.Add("CSV file", new List<string> { ".csv" });
        picker.SuggestedFileName = "watched_and_gone";
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSaveFileAsync();
        if (file == null) return;
        await new MainViewModel().ExportCsvAsync(_records, file.Path);
        if (App.MainWindow is MainWindow mw) mw.ShowToast("Exported records to CSV");
    }
}
