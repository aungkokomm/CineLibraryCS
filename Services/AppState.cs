using CineLibraryCS.Models;

namespace CineLibraryCS.Services;

/// <summary>
/// Singleton app-wide state and service locator.
/// </summary>
public class AppState
{
    private static AppState? _instance;
    public static AppState Instance => _instance ??= new AppState();

    public DatabaseService Db { get; private set; } = null!;
    public ScannerService Scanner { get; private set; } = null!;
    public ListCopyService ListCopy { get; private set; } = null!;
    public string DataDir { get; private set; } = "";

    public void Initialize()
    {
        DataDir = GetDataDir();
        Db = new DatabaseService(DataDir);
        Scanner = new ScannerService(Db);
        ListCopy = new ListCopyService(Db);

        // v2.7 — mirror every personal-state change into the per-movie
        // sidecar (cinelibrary-state.json) so the state travels with the
        // drive. Best-effort; the sidecar helper swallows I/O errors.
        Db.PersonalStateChanged += OnPersonalStateChanged;
        // v2.8 — same for TV shows (show favorite/watchlist + per-episode watched).
        Db.TvShowStateChanged += OnTvShowStateChanged;
    }

    private void OnTvShowStateChanged(int showId)
    {
        var composed = TvStateSidecar.Compose(Db, showId, _connected);
        if (composed.HasValue)
            TvStateSidecar.TryWrite(composed.Value.FolderAbs, composed.Value.State);
    }

    /// <summary>
    /// Fires after Toggle/Set Watched | Favorite | Watchlist, MarkPlayed,
    /// SetNote, and list add/remove. Off the SQL thread is preferable but
    /// the operation is cheap, so we just run it inline.
    /// </summary>
    private void OnPersonalStateChanged(int movieId)
    {
        var composed = MovieStateSidecar.Compose(Db, movieId, _connected);
        if (composed.HasValue)
            MovieStateSidecar.TryWrite(composed.Value.FolderAbs, composed.Value.State);
    }

    /// <summary>
    /// Walk every movie with non-default personal state and mirror it to
    /// the per-folder sidecar. Used by:
    ///   • App startup (catches state edited before this feature, or while
    ///     drives were offline).
    ///   • The Drives page "Sync personal state" button.
    ///   • The pre-remove confirmation when a user removes a drive.
    /// Returns (written, skipped) counts. Skipped = drive offline or path
    /// unreachable. Runs on the caller's thread — call from a Task.Run.
    /// </summary>
    public (int Written, int Skipped) SweepStateSidecars(string? onlyVolumeSerial = null)
    {
        var ids = Db.GetMoviesWithPersonalState(onlyVolumeSerial);
        int written = 0, skipped = 0;
        foreach (var id in ids)
        {
            var composed = MovieStateSidecar.Compose(Db, id, _connected);
            if (composed == null) { skipped++; continue; }
            MovieStateSidecar.TryWrite(composed.Value.FolderAbs, composed.Value.State);
            written++;
        }
        // v2.8 — also sweep TV shows.
        foreach (var id in Db.GetTvShowsWithPersonalState(onlyVolumeSerial))
        {
            var composed = TvStateSidecar.Compose(Db, id, _connected);
            if (composed == null) { skipped++; continue; }
            TvStateSidecar.TryWrite(composed.Value.FolderAbs, composed.Value.State);
            written++;
        }
        return (written, skipped);
    }

    private static string GetDataDir()
    {
        // Walk up from bin/x64/Debug/net8.0-windows... to project root during dev
        var exeDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(exeDir);
        // In VS, output is typically: ProjectDir\bin\x64\Debug\net8.0-windows...\
        // Walk up until we find a .csproj or we've gone up 5 levels
        for (int i = 0; i < 5; i++)
        {
            if (dir == null) break;
            if (dir.GetFiles("*.csproj").Length > 0)
            {
                // In dev: put data next to the .csproj
                var devData = Path.Combine(dir.FullName, "CineLibrary-Data");
                Directory.CreateDirectory(devData);
                return devData;
            }
            dir = dir.Parent;
        }
        // In production: put data next to the exe
        var prodData = Path.Combine(exeDir, "CineLibrary-Data");
        Directory.CreateDirectory(prodData);
        return prodData;
    }

    // ── Cached connected drives (refreshed on a timer) ───────────────────────

    private Dictionary<string, string> _connected = new();

    public Dictionary<string, string> Connected => _connected;

    public void RefreshConnected()
    {
        _connected = Db.GetConnectedDrives();
        foreach (var kv in _connected)
            Db.UpdateDriveLastSeen(kv.Key, kv.Value);
    }

    // ── Preferences ──────────────────────────────────────────────────────────

    public string GetPref(string key, string defaultValue = "")
        => Db.GetPref(key) ?? defaultValue;

    public void SetPref(string key, string value)
        => Db.SetPref(key, value);
}
