using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CineLibraryCS.Models;
using CineLibraryCS.Services;
using System.Collections.ObjectModel;

namespace CineLibraryCS.ViewModels;

public enum SortKey { Title, Year, Rating, Runtime, DateAdded, LastPlayed }
public enum SortDir { Asc, Desc }
public enum WatchedFilter { All, Unwatched, Watched }
public enum ViewMode { Grid, List }
public enum LibraryViewType { AllMovies, Watched, Unwatched, Favorites, Watchlist }

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
    [ObservableProperty] private string? _filterActor = null;
    [ObservableProperty] private string? _filterDirector = null;
    [ObservableProperty] private string? _filterStudio = null;
    [ObservableProperty] private bool _isWatchlistOnly = false;
    [ObservableProperty] private bool _isContinueWatching = false;
    [ObservableProperty] private int? _collectionId = null;
    [ObservableProperty] private bool _isLoading = false;
    [ObservableProperty] private bool _hasMore = false;
    [ObservableProperty] private int _totalCount = 0;
    [ObservableProperty] private string _pageTitle = "All Movies";
    [ObservableProperty] private int _watchlistCount = 0;

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
            "lastplayed" => "last_played",
            var s => s
        },
        SortDir: SortDir == SortDir.Asc ? "asc" : "desc",
        DriveSerial: DriveSerial,
        Genre: Genre,
        Actor: FilterActor,
        Director: FilterDirector,
        Studio: FilterStudio,
        CollectionId: CollectionId,
        WatchedFilter: WatchedFilter switch
        {
            WatchedFilter.Watched => "watched",
            WatchedFilter.Unwatched => "unwatched",
            _ => "all"
        },
        FavoritesOnly: FavoritesOnly,
        IsWatchlistOnly: IsWatchlistOnly,
        ContinueWatching: IsContinueWatching,
        Limit: PageSize,
        Offset: offset
    );

    // ── Search ───────────────────────────────────────────────────────────────
    // Debounced reload — cancels in-flight delay on every keystroke.
    // Replaces the previous Timer-per-keystroke pattern (each new keystroke
    // disposed the old Timer, but a callback could still fire on a disposed
    // VM during shutdown, and creating a fresh Timer per keystroke was
    // wasteful on busy typing).
    private CancellationTokenSource? _searchCts;

    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(300, token); }
            catch (OperationCanceledException) { return; }
            if (token.IsCancellationRequested) return;
            _dispatcherQueue?.TryEnqueue(async () =>
            {
                if (token.IsCancellationRequested) return;
                await LoadAsync();
            });
        }, token);
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

    /// <summary>
    /// "Recently Added" view — clears filters and forces sort by date_added DESC.
    /// Doesn't persist this sort to prefs (it's a transient nav choice).
    /// </summary>
    public void ShowRecentlyAdded()
    {
        DriveSerial = null;
        Genre = null;
        CollectionId = null;
        FavoritesOnly = false;
        FilterActor = null;
        FilterDirector = null;
        IsWatchlistOnly = false;
        IsContinueWatching = false;
        SearchText = "";
        WatchedFilter = WatchedFilter.All;
        SortKey = SortKey.DateAdded;
        SortDir = SortDir.Desc;
        PageTitle = "🆕 Recently Added";
        _ = LoadAsync();
    }

    /// <summary>
    /// "Continue Watching" — movies the user has hit Play on at least once
    /// but hasn't marked watched. Sorted by last_played_at DESC so the most
    /// recently started one is on top. is_watched=0 is enforced in the SQL.
    /// </summary>
    public void ShowContinueWatching()
    {
        DriveSerial = null;
        Genre = null;
        CollectionId = null;
        FavoritesOnly = false;
        FilterActor = null;
        FilterDirector = null;
        IsWatchlistOnly = false;
        IsContinueWatching = true;
        SearchText = "";
        WatchedFilter = WatchedFilter.All;
        SortKey = SortKey.LastPlayed;
        SortDir = SortDir.Desc;
        PageTitle = "▶ Continue Watching";
        _ = LoadAsync();
    }

    public void ClearFilters()
    {
        DriveSerial = null;
        Genre = null;
        CollectionId = null;
        FavoritesOnly = false;
        FilterActor = null;
        FilterDirector = null;
        FilterStudio = null;
        IsWatchlistOnly = false;
        IsContinueWatching = false;
        PageTitle = "All Movies";
        _ = LoadAsync();
    }

    // ── New filters (v1.3) ───────────────────────────────────────────────────

    public void FilterByActor(string actorName)
    {
        FilterActor = actorName;
        FilterDirector = null;
        FilterStudio = null;
        Genre = null;
        DriveSerial = null;
        CollectionId = null;
        FavoritesOnly = false;
        IsWatchlistOnly = false;
        SearchText = "";
        PageTitle = $"Movies with {actorName}";
        _ = LoadAsync();
    }

    public void FilterByDirector(string directorName)
    {
        FilterDirector = directorName;
        FilterActor = null;
        FilterStudio = null;
        Genre = null;
        DriveSerial = null;
        CollectionId = null;
        FavoritesOnly = false;
        IsWatchlistOnly = false;
        SearchText = "";
        PageTitle = $"Directed by {directorName}";
        _ = LoadAsync();
    }

    public void FilterByStudio(string studio)
    {
        FilterStudio = studio;
        FilterActor = null;
        FilterDirector = null;
        Genre = null;
        DriveSerial = null;
        CollectionId = null;
        FavoritesOnly = false;
        IsWatchlistOnly = false;
        SearchText = "";
        PageTitle = $"Studio: {studio}";
        _ = LoadAsync();
    }

    public void ShowWatchlist()
    {
        IsWatchlistOnly = true;
        FilterActor = null;
        FilterDirector = null;
        Genre = null;
        DriveSerial = null;
        CollectionId = null;
        FavoritesOnly = false;
        SearchText = "";
        PageTitle = "📋 To Watch";
        RefreshWatchlistCount();
        _ = LoadAsync();
    }

    public void RefreshWatchlistCount()
    {
        WatchlistCount = _state.Db.GetWatchlistCount();
    }

    public void ToggleWatchlist(int movieId, bool isWatchlist)
    {
        _state.Db.SetWatchlist(movieId, isWatchlist);
        RefreshWatchlistCount();
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

    public void ToggleWatchlistOnCard(MovieListItem movie)
    {
        var newVal = !movie.IsWatchlist;
        _state.Db.SetWatchlist(movie.Id, newVal);
        movie.IsWatchlist = newVal;
        // If currently filtered to watchlist-only, removing should drop the card
        if (IsWatchlistOnly && !newVal)
            Movies.Remove(movie);
        RefreshWatchlistCount();
    }
}
