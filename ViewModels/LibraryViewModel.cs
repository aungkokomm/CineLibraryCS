using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CineLibraryCS.Models;
using CineLibraryCS.Services;
using System.Collections.ObjectModel;

namespace CineLibraryCS.ViewModels;

public enum SortKey { Title, Year, Rating, Runtime, DateAdded }
public enum SortDir { Asc, Desc }
public enum WatchedFilter { All, Unwatched, Watched }
public enum ViewMode { Grid, List }

public partial class LibraryViewModel : ObservableObject
{
    private const int PageSize = 60;
    private readonly AppState _state;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private SortKey _sortKey = SortKey.Title;
    [ObservableProperty] private SortDir _sortDir = SortDir.Asc;
    [ObservableProperty] private WatchedFilter _watchedFilter = WatchedFilter.All;
    [ObservableProperty] private ViewMode _viewMode = ViewMode.Grid;
    [ObservableProperty] private bool _favoritesOnly = false;
    [ObservableProperty] private string? _driveSerial = null;
    [ObservableProperty] private string? _genre = null;
    [ObservableProperty] private int? _collectionId = null;
    [ObservableProperty] private bool _isLoading = false;
    [ObservableProperty] private bool _hasMore = false;
    [ObservableProperty] private int _totalCount = 0;
    [ObservableProperty] private string _pageTitle = "All Movies";

    public ObservableCollection<MovieListItem> Movies { get; } = new();

    // Filter/nav state
    private string? _filterDriveSerial;
    private string? _filterGenre;
    private int? _filterCollectionId;

    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;

    public LibraryViewModel()
    {
        _state = AppState.Instance;
        _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        LoadPrefs();
    }

    private void LoadPrefs()
    {
        var vm = _state.GetPref("viewMode", "Grid");
        _viewMode = Enum.TryParse<ViewMode>(vm, out var vmp) ? vmp : ViewMode.Grid;

        var sk = _state.GetPref("sortKey", "Title");
        _sortKey = Enum.TryParse<SortKey>(sk, out var skp) ? skp : SortKey.Title;

        var sd = _state.GetPref("sortDir", "Asc");
        _sortDir = Enum.TryParse<SortDir>(sd, out var sdp) ? sdp : SortDir.Asc;
    }

    // ── Load movies ──────────────────────────────────────────────────────────

    public async Task LoadAsync()
    {
        IsLoading = true;
        var opts = BuildOpts(0);
        var movies = await Task.Run(() => _state.Db.GetMovies(opts, _state.Connected));

        Movies.Clear();
        foreach (var m in movies) Movies.Add(m);
        HasMore = movies.Count == PageSize;
        TotalCount = Movies.Count;
        IsLoading = false;
    }

    public async Task LoadMoreAsync()
    {
        if (!HasMore || IsLoading) return;
        IsLoading = true;
        var opts = BuildOpts(Movies.Count);
        var movies = await Task.Run(() => _state.Db.GetMovies(opts, _state.Connected));
        foreach (var m in movies) Movies.Add(m);
        HasMore = movies.Count == PageSize;
        TotalCount = Movies.Count;
        IsLoading = false;
    }

    private DatabaseService.ListOptions BuildOpts(int offset) => new(
        Search: string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
        SortKey: SortKey.ToString().ToLower() switch
        {
            "dateadded" => "date_added",
            var s => s
        },
        SortDir: SortDir == SortDir.Asc ? "asc" : "desc",
        DriveSerial: DriveSerial,
        Genre: Genre,
        CollectionId: CollectionId,
        WatchedFilter: WatchedFilter switch
        {
            WatchedFilter.Watched => "watched",
            WatchedFilter.Unwatched => "unwatched",
            _ => "all"
        },
        FavoritesOnly: FavoritesOnly,
        Limit: PageSize,
        Offset: offset
    );

    // ── Search ───────────────────────────────────────────────────────────────

    private System.Threading.Timer? _searchTimer;

    partial void OnSearchTextChanged(string value)
    {
        _searchTimer?.Dispose();
        _searchTimer = new System.Threading.Timer(_ =>
        {
            _dispatcherQueue?.TryEnqueue(async () => await LoadAsync());
        }, null, 300, Timeout.Infinite);
    }

    // ── Sort / View ──────────────────────────────────────────────────────────

    partial void OnSortKeyChanged(SortKey value)
    {
        _state.SetPref("sortKey", value.ToString());
        _ = LoadAsync();
    }

    partial void OnSortDirChanged(SortDir value)
    {
        _state.SetPref("sortDir", value.ToString());
        _ = LoadAsync();
    }

    partial void OnViewModeChanged(ViewMode value)
    {
        _state.SetPref("viewMode", value.ToString());
    }

    partial void OnWatchedFilterChanged(WatchedFilter value) => _ = LoadAsync();
    partial void OnFavoritesOnlyChanged(bool value) => _ = LoadAsync();

    // ── Navigation filters ───────────────────────────────────────────────────

    public void SetDriveFilter(string? serial, string? driveLabel = null)
    {
        DriveSerial = serial;
        Genre = null;
        CollectionId = null;
        FavoritesOnly = false;
        PageTitle = driveLabel ?? (serial == null ? "All Movies" : "Drive");
        _ = LoadAsync();
    }

    public void SetGenreFilter(string? genre)
    {
        Genre = genre;
        DriveSerial = null;
        CollectionId = null;
        FavoritesOnly = false;
        PageTitle = genre ?? "All Movies";
        _ = LoadAsync();
    }

    public void SetCollectionFilter(int? id, string? name)
    {
        CollectionId = id;
        DriveSerial = null;
        Genre = null;
        FavoritesOnly = false;
        PageTitle = name ?? "Collection";
        _ = LoadAsync();
    }

    public void SetFavorites()
    {
        FavoritesOnly = true;
        DriveSerial = null;
        Genre = null;
        CollectionId = null;
        PageTitle = "Favorites";
        _ = LoadAsync();
    }

    public void ClearFilters()
    {
        DriveSerial = null;
        Genre = null;
        CollectionId = null;
        FavoritesOnly = false;
        PageTitle = "All Movies";
        _ = LoadAsync();
    }

    // ── Mutations ────────────────────────────────────────────────────────────

    public void ToggleFavorite(MovieListItem movie)
    {
        _state.Db.ToggleFavorite(movie.Id);
        movie.IsFavorite = !movie.IsFavorite;
        if (FavoritesOnly && !movie.IsFavorite)
            Movies.Remove(movie);
    }

    public void ToggleWatched(MovieListItem movie)
    {
        _state.Db.ToggleWatched(movie.Id);
        movie.IsWatched = !movie.IsWatched;
        if (WatchedFilter == WatchedFilter.Unwatched && movie.IsWatched)
            Movies.Remove(movie);
        else if (WatchedFilter == WatchedFilter.Watched && !movie.IsWatched)
            Movies.Remove(movie);
    }
}
