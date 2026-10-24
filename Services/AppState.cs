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
    public (int Written, int Skipped) SweepStateSidecars(
        string? onlyVolumeSerial = null, bool includeFetchedArt = false)
    {
        int written = 0, skipped = 0;

        var stateIds = Db.GetMoviesWithPersonalState(onlyVolumeSerial);
        // v3.4 — only the manual "Sync to drive" pass also writes fetched
        // metadata + art; the automatic startup sweep stays personal-state only.
        var fetchedSet = includeFetchedArt
            ? new HashSet<int>(Db.GetMoviesWithFetchedData(onlyVolumeSerial))
            : new HashSet<int>();
        var allIds = new HashSet<int>(stateIds);
        allIds.UnionWith(fetchedSet);

        foreach (var id in allIds)
        {
            var composed = MovieStateSidecar.Compose(Db, id, _connected);
            if (composed == null) { skipped++; continue; }
            var folderAbs = composed.Value.FolderAbs;
            var state = composed.Value.State;
            if (fetchedSet.Contains(id))
            {
                state.Meta = MovieStateSidecar.ComposeMeta(Db, id);
                CopyFetchedArtToFolder(id, folderAbs);
            }
            MovieStateSidecar.TryWrite(folderAbs, state);
            written++;
        }

        // v2.8 — also sweep TV shows (personal state only).
        foreach (var id in Db.GetTvShowsWithPersonalState(onlyVolumeSerial))
        {
            var composed = TvStateSidecar.Compose(Db, id, _connected);
            if (composed == null) { skipped++; continue; }
            TvStateSidecar.TryWrite(composed.Value.FolderAbs, composed.Value.State);
            written++;
        }
        return (written, skipped);
    }

    /// <summary>
    /// v3.4 — copy a movie's fetched poster / fanart / cast photos out of the
    /// data cache into its drive folder, only where the file is absent. A later
    /// rescan then discovers them like any MediaElch artwork. Best-effort.
    /// v4.6.0: false when a copy failed (e.g. a read-only or full drive), so the
    /// automatic sync tries again later.
    /// </summary>
    private bool CopyFetchedArtToFolder(int movieId, string folderAbs)
    {
        try
        {
            if (!Directory.Exists(folderAbs)) return false;
            var (posterRel, fanartRel, actors) = Db.GetMovieArtForSync(movieId);
            return CopyFetchedArt(posterRel, fanartRel, actors, folderAbs);
        }
        catch { return false; /* best-effort: drive offline / read-only / locked */ }
    }

    /// <summary>The fetched ones (in the data folder) among a title's pictures,
    /// into its folder where that file is absent. False when a copy failed.</summary>
    private bool CopyFetchedArt(string? posterRel, string? fanartRel,
        IEnumerable<(string Name, string? Thumb)> actors, string folderAbs)
    {
        bool ok = true;
        if (posterRel != null && posterRel.StartsWith("manual_posters/", StringComparison.Ordinal))
            ok &= CopyCacheFileIfAbsent(posterRel, Path.Combine(folderAbs, "poster.jpg"));
        if (fanartRel != null && fanartRel.StartsWith("manual_fanart/", StringComparison.Ordinal))
            ok &= CopyCacheFileIfAbsent(fanartRel, Path.Combine(folderAbs, "fanart.jpg"));

        var actorsDir = Path.Combine(folderAbs, ".actors");
        foreach (var (name, thumb) in actors)
        {
            if (thumb == null || !thumb.StartsWith("manual_actors/", StringComparison.Ordinal)) continue;
            var file = name.Replace(' ', '_') + ".jpg";
            if (file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;   // can't be named on disk
            ok &= CopyCacheFileIfAbsent(thumb, Path.Combine(actorsDir, file));
        }
        return ok;
    }

    /// <summary>
    /// v4.8.0: what was fetched, into the movie's .nfo where it's empty (NfoFill),
    /// saved like an IMDb rating and under the same lock. False when it couldn't
    /// be written (tried again later); an .nfo that isn't XML is left as it is.
    /// </summary>
    private bool FillMovieNfo(string folderAbs, MovieStateSidecar.Meta meta)
    {
        var nfo = ScannerService.FindNfo(folderAbs);
        if (nfo == null) return true;
        var actorsDir = Path.Combine(folderAbs, ".actors");
        string? Thumb(string name)   // the photo CopyFetchedArt names, when it's there
        {
            var file = name.Replace(' ', '_') + ".jpg";
            return file.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && File.Exists(Path.Combine(actorsDir, file))
                ? ".actors/" + file : null;
        }
        lock (ImdbUpdate.Gate)
        {
            System.Xml.Linq.XDocument doc;
            try { doc = System.Xml.Linq.XDocument.Load(nfo); }
            catch (System.Xml.XmlException) { return true; }
            catch { return false; }
            if (doc.Root == null || !NfoFill.Apply(doc.Root, meta, Thumb)) return true;
            try { NfoRatings.Save(doc, nfo); }
            catch { return false; }
        }
        return true;
    }

    public enum DriveSave { Saved, Waiting, Off }

    /// <summary>
    /// v4.6.0: copies what Fetch missing info got into the folders of the titles
    /// fetched since it last reached them: the poster, fanart and cast photos
    /// (only files that aren't there yet), and for a movie its details into the
    /// state file and (v4.8.0) into its .nfo where that's empty. A title whose drive isn't connected
    /// waits for the next pass (when the drive is plugged in, or at start-up).
    /// Off with Settings → Save fetched info to your drives, unless
    /// <paramref name="evenIfOff"/> (Sync to drive). Call off the UI thread.
    /// </summary>
    public (int Movies, int Shows) SyncFetched(string? onlyVolumeSerial = null, bool evenIfOff = false)
    {
        if (!evenIfOff && !UiSettings.SaveFetchedToDrives) return (0, 0);
        lock (_syncGate) return SyncFetchedLocked(onlyVolumeSerial);
    }

    // One pass at a time: start-up, a drive plugged in and a fetch can overlap.
    private readonly object _syncGate = new();

    private (int Movies, int Shows) SyncFetchedLocked(string? onlyVolumeSerial)
    {
        var (movies, shows) = Db.GetPendingDriveSync(onlyVolumeSerial);
        var connected = _connected;
        int m = 0, s = 0;
        foreach (var id in movies)
        {
            try
            {
                var composed = MovieStateSidecar.Compose(Db, id, connected);
                if (composed == null) continue;                    // drive not connected
                var (folderAbs, state) = composed.Value;
                if (!CopyFetchedArtToFolder(id, folderAbs)) continue;
                state.Meta = MovieStateSidecar.ComposeMeta(Db, id);
                if (!FillMovieNfo(folderAbs, state.Meta)) continue;   // v4.8.0
                MovieStateSidecar.TryWrite(folderAbs, state);
                // v4.8.0: the folder is read again, so the library shows just what the
                // .nfo says, its own cast too rather than the fetched one; the state
                // file, written first, keeps what the .nfo can't hold
                if (Db.GetMovieFolder(id) is { } where)
                    ReloadFolderAsync(where.Serial, where.FolderRel).GetAwaiter().GetResult();
                Db.MarkSyncedToDrive(false, id);
                m++;
            }
            catch { /* tried again next time */ }
        }
        foreach (var id in shows)
        {
            try
            {
                if (Db.GetTvShowArtForSync(id) is not { } art || art.FolderRel == null
                    || !connected.TryGetValue(art.Serial, out var letter)) continue;
                var folderAbs = Path.Combine($"{letter}:\\", art.FolderRel.Replace('/', '\\'));
                if (!Directory.Exists(folderAbs) || !CopyFetchedArt(art.Poster, art.Fanart, art.Actors, folderAbs)) continue;
                Db.MarkSyncedToDrive(true, id);
                s++;
            }
            catch { /* tried again next time */ }
        }
        return (m, s);
    }

    /// <summary>v4.6.0: right after a fetch: saves it to the drive now when it can.</summary>
    public DriveSave SyncFetchedTitle(bool show, int id, string volumeSerial)
    {
        if (!UiSettings.SaveFetchedToDrives || volumeSerial == "__archive__") return DriveSave.Off;   // Watched & Gone: no folder
        if (!_connected.ContainsKey(volumeSerial)) return DriveSave.Waiting;
        SyncFetched(volumeSerial);
        return Db.IsSyncedToDrive(show, id) ? DriveSave.Saved : DriveSave.Waiting;
    }

    /// <summary>
    /// v4.6.0: reads one movie or show folder again (its .nfo and pictures, and a
    /// show's episodes), e.g. after editing it in CineLibrary Essentials, and
    /// forgets its old pictures. False when the drive isn't connected or the
    /// folder is gone.
    /// </summary>
    public async Task<bool> ReloadFolderAsync(string volumeSerial, string? folderRelPath)
    {
        if (folderRelPath == null || !_connected.TryGetValue(volumeSerial, out var letter)) return false;
        var driveRoot = $"{letter}:\\";
        var folderAbs = Path.Combine(driveRoot, folderRelPath.Replace('/', '\\'));
        if (!Directory.Exists(folderAbs)) return false;
        await Scanner.ScanAsync(volumeSerial, driveRoot, scanFolder: folderAbs);
        ImageCache.Forget($"cache/{ScannerService.ComputeMovieKey(volumeSerial, folderRelPath)}/");
        return true;
    }

    /// <returns>v4.6.0: false only when the copy failed (nothing to copy is fine).</returns>
    private bool CopyCacheFileIfAbsent(string cacheRel, string destAbs)
    {
        try
        {
            if (File.Exists(destAbs)) return true;
            var src = Db.GetCachedImagePath(cacheRel);
            if (src == null) return true;
            Directory.CreateDirectory(Path.GetDirectoryName(destAbs)!);
            File.Copy(src, destAbs, overwrite: false);
            return true;
        }
        catch { return false; /* best-effort */ }
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
