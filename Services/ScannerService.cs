using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;

namespace CineLibraryCS.Services;

public record ScanProgress(int Found, int Inserted, int Updated, int Skipped, string CurrentFolder, bool Done);

public class ScannerService
{
    private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
        { ".mkv", ".mp4", ".avi", ".m4v", ".mov", ".wmv", ".flv", ".webm", ".mpg", ".mpeg", ".ts", ".m2ts", ".iso" };

    private static readonly string[] PosterNames = { "poster.jpg", "poster.jpeg", "poster.png", "folder.jpg", "cover.jpg" };
    private static readonly string[] FanartNames = { "fanart.jpg", "fanart.jpeg", "fanart.png", "backdrop.jpg", "backdrop.jpeg" };
    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };

    private readonly DatabaseService _db;

    public ScannerService(DatabaseService db) => _db = db;

    /// <param name="scanFolder">Optional: scan only this subfolder. Paths are still stored relative to driveRoot.</param>
    /// <param name="incremental">When true, skip movies whose .nfo mtime is older than the row's date_modified —
    /// MediaElch hasn't touched them since we last scanned, so re-parsing would be wasted work.</param>
    public async Task ScanAsync(string volumeSerial, string driveRoot, IProgress<ScanProgress>? progress = null, CancellationToken ct = default, string? scanFolder = null, bool incremental = false)
    {
        await Task.Run(() => ScanSync(volumeSerial, driveRoot, progress, ct, scanFolder, incremental), ct);
    }

    private void ScanSync(string volumeSerial, string driveRoot, IProgress<ScanProgress>? progress, CancellationToken ct, string? scanFolder, bool incremental)
    {
        // Use a dedicated connection so our long-lived scan transaction
        // doesn't taint the main DB connection used by sidebar refresh and
        // other UI reads. Microsoft.Data.Sqlite raises InvalidOperationException
        // ("Execute requires the command to have a transaction object…") if any
        // command runs against a connection with a pending tx and that command
        // doesn't carry the tx reference — easy to hit on rescans.
        using var conn = _db.OpenNewConnection();
        var dataDir = _db.DataDir;
        var walkFrom = scanFolder ?? driveRoot;

        int found = 0, inserted = 0, updated = 0, skipped = 0;

        // Mark movies in the scanned scope as potentially missing
        using (var cmd = conn.CreateCommand())
        {
            if (scanFolder != null)
            {
                // Only mark movies under the chosen subfolder
                var subRel = Path.GetRelativePath(driveRoot, scanFolder).Replace('\\', '/');
                cmd.CommandText = "UPDATE movies SET is_missing=1 WHERE volume_serial=@s AND (folder_rel_path=@r OR folder_rel_path LIKE @p)";
                cmd.Parameters.AddWithValue("@s", volumeSerial);
                cmd.Parameters.AddWithValue("@r", subRel);
                cmd.Parameters.AddWithValue("@p", subRel + "/%");
            }
            else
            {
                cmd.CommandText = "UPDATE movies SET is_missing=1 WHERE volume_serial=@s";
                cmd.Parameters.AddWithValue("@s", volumeSerial);
            }
            cmd.ExecuteNonQuery();
        }

        var movieFolders = FindMovieFolders(walkFrom);

        // Prepare statements — disposed in finally block below
        using var stmtGetId = conn.CreateCommand();
        stmtGetId.CommandText = "SELECT id, date_modified FROM movies WHERE volume_serial=@s AND folder_rel_path=@f";
        stmtGetId.Parameters.AddWithValue("@s", volumeSerial);
        stmtGetId.Parameters.Add("@f", SqliteType.Text);

        // For incremental rescan: a quick "just clear is_missing" statement
        // we use when the nfo hasn't been touched since last scan.
        using var stmtTouch = conn.CreateCommand();
        stmtTouch.CommandText = "UPDATE movies SET is_missing=0 WHERE id=@id";
        stmtTouch.Parameters.Add("@id", SqliteType.Integer);

        using var stmtUpdate = conn.CreateCommand();
        stmtUpdate.CommandText = @"UPDATE movies SET
            video_file_rel_path=@vfr, title=@t, original_title=@ot, sort_title=@st,
            year=@y, rating=@ra, votes=@vo, runtime=@ru, plot=@pl, outline=@ou,
            tagline=@tg, mpaa=@mp, imdb_id=@im, tmdb_id=@tm, premiered=@pr,
            studio=@su, country=@co, trailer=@tr, local_poster=@lp, local_fanart=@lf,
            local_nfo=@ln, is_missing=0, date_modified=strftime('%s','now')
            WHERE id=@id";

        using var stmtInsert = conn.CreateCommand();
        stmtInsert.CommandText = @"INSERT INTO movies (
            volume_serial, folder_rel_path, video_file_rel_path, title, original_title, sort_title,
            year, rating, votes, runtime, plot, outline, tagline, mpaa, imdb_id, tmdb_id,
            premiered, studio, country, trailer, local_poster, local_fanart, local_nfo)
            VALUES (@vs,@fr,@vfr,@t,@ot,@st,@y,@ra,@vo,@ru,@pl,@ou,@tg,@mp,@im,@tm,@pr,@su,@co,@tr,@lp,@lf,@ln)";

        using var tx = conn.BeginTransaction();
        // All commands on this connection must carry the active transaction
        stmtGetId.Transaction = tx;
        stmtUpdate.Transaction = tx;
        stmtInsert.Transaction = tx;
        stmtTouch.Transaction  = tx;

        // Pre-load existing name→id maps once so per-movie related-row work
        // doesn't hammer the DB with redundant SELECTs.
        var lookup = LookupCache.Load(conn, tx);

        try
        {
            foreach (var folder in movieFolders)
            {
                ct.ThrowIfCancellationRequested();

                var folderRelPath = Path.GetRelativePath(driveRoot, folder).Replace('\\', '/');
                var movieKey = ComputeMovieKey(volumeSerial, folderRelPath);

                // Find NFO
                var nfoPath = FindNfo(folder);
                if (nfoPath == null) { skipped++; continue; }

                // Incremental short-circuit: if the row already exists and the
                // .nfo mtime is older than (or equal to) date_modified, skip
                // parsing entirely. We just clear is_missing so the row is
                // marked online again. Cuts a no-op rescan from minutes to
                // seconds on a large library.
                if (incremental)
                {
                    stmtGetId.Parameters["@f"].Value = folderRelPath;
                    using (var probe = stmtGetId.ExecuteReader())
                    {
                        if (probe.Read())
                        {
                            var existingIdProbe = probe.GetInt32(0);
                            var rowModified = probe.IsDBNull(1) ? 0L : probe.GetInt64(1);
                            probe.Close();
                            try
                            {
                                var nfoMtime = new DateTimeOffset(File.GetLastWriteTimeUtc(nfoPath)).ToUnixTimeSeconds();
                                if (nfoMtime <= rowModified)
                                {
                                    stmtTouch.Parameters["@id"].Value = existingIdProbe;
                                    stmtTouch.ExecuteNonQuery();
                                    found++;
                                    skipped++; // counted as skipped-because-unchanged
                                    progress?.Report(new ScanProgress(found, inserted, updated, skipped, folder, false));
                                    continue;
                                }
                            }
                            catch { /* fall through to full parse if mtime read fails */ }
                        }
                    }
                }

                // Parse NFO
                var parsed = NfoParser.Parse(nfoPath);
                if (parsed == null) { skipped++; continue; }

                found++;
                progress?.Report(new ScanProgress(found, inserted, updated, skipped, folder, false));

                // Find video file
                string? videoRelPath = null;
                foreach (var f in Directory.EnumerateFiles(folder))
                {
                    if (VideoExts.Contains(Path.GetExtension(f)))
                    {
                        videoRelPath = Path.GetRelativePath(driveRoot, f).Replace('\\', '/');
                        break;
                    }
                }

                // Find and cache artwork
                var posterSrc = FindArtwork(folder, PosterNames, "poster");
                var fanartSrc = FindArtwork(folder, FanartNames, "fanart");

                var localPoster = posterSrc != null ? CopyToCache(posterSrc, dataDir, movieKey, "poster") : null;
                var localFanart = fanartSrc != null ? CopyToCache(fanartSrc, dataDir, movieKey, "fanart") : null;
                var localNfo    = CopyToCache(nfoPath, dataDir, movieKey, "nfo");

                // Check existing
                stmtGetId.Parameters["@f"].Value = folderRelPath;
                var existingId = stmtGetId.ExecuteScalar();

                int rowId;
                if (existingId != null && existingId != DBNull.Value)
                {
                    // Clear params from any previous iteration before re-binding
                    stmtUpdate.Parameters.Clear();
                    rowId = Convert.ToInt32(existingId);
                    BindMovieParams(stmtUpdate, parsed, videoRelPath, localPoster, localFanart, localNfo);
                    stmtUpdate.Parameters.AddWithValue("@id", rowId);
                    stmtUpdate.ExecuteNonQuery();
                    UpsertRelated(conn, tx, rowId, parsed, lookup);
                    updated++;
                }
                else
                {
                    stmtInsert.Parameters.Clear();
                    stmtInsert.Parameters.AddWithValue("@vs", volumeSerial);
                    stmtInsert.Parameters.AddWithValue("@fr", folderRelPath);
                    BindMovieParams(stmtInsert, parsed, videoRelPath, localPoster, localFanart, localNfo);
                    stmtInsert.ExecuteNonQuery();
                    // Retrieve the inserted row ID
                    using var lastId = conn.CreateCommand();
                    lastId.CommandText = "SELECT last_insert_rowid()";
                    lastId.Transaction = tx;
                    rowId = Convert.ToInt32(lastId.ExecuteScalar());
                    UpsertRelated(conn, tx, rowId, parsed, lookup);
                    inserted++;
                }

                // Sidecar note import (cinelibrary-note.txt next to .nfo).
                // Only writes DB if DB note is currently empty — DB is the source
                // of truth once the user has saved a note inside the app. This way
                // a reinstall + rescan can recover notes from sidecars; subsequent
                // edits in CineLibrary won't be reverted by the next scan.
                ImportSidecarNoteIfNeeded(conn, tx, rowId, folder);
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        progress?.Report(new ScanProgress(found, inserted, updated, skipped, "", true));
    }

    private static void BindMovieParams(SqliteCommand cmd, ParsedMovie p, string? vfr, string? lp, string? lf, string? ln)
    {
        cmd.Parameters.AddWithValue("@vfr", (object?)vfr ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@t", p.Title);
        cmd.Parameters.AddWithValue("@ot", (object?)p.OriginalTitle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@st", (object?)(p.SortTitle ?? p.Title) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@y", (object?)p.Year ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ra", (object?)p.Rating ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@vo", (object?)p.Votes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ru", (object?)p.Runtime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pl", (object?)p.Plot ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ou", (object?)p.Outline ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tg", (object?)p.Tagline ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@mp", (object?)p.Mpaa ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@im", (object?)p.ImdbId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tm", (object?)p.TmdbId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pr", (object?)p.Premiered ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@su", (object?)p.Studio ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@co", (object?)p.Country ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tr", (object?)p.Trailer ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lp", (object?)lp ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lf", (object?)lf ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ln", (object?)ln ?? DBNull.Value);
    }

    /// <summary>
    /// In-memory caches of name→id for genres/directors/actors/sets.
    /// Built once per scan and shared across all movies.
    ///
    /// Why: the previous version did 4 SQL round-trips per related row
    /// (DELETE child, INSERT IGNORE name, SELECT id, INSERT link). For a
    /// movie with 20 actors that was ~80 queries. Across 1000 movies that
    /// was 80k queries dominated by the chatty SELECT id lookups for
    /// names that repeat across movies (e.g. Tom Hanks shows up 30 times).
    ///
    /// New approach: pre-load all existing name→id maps at scan start.
    /// During scan, look up locally (free) and only INSERT when missing,
    /// using last_insert_rowid() to extend the cache. Net effect on a
    /// big rescan: typically 5–10× fewer queries on the related-tables.
    /// </summary>
    /// <summary>
    /// Some scrapers / older nfos write multiple genres in a single
    /// &lt;genre&gt; element, separated by /, comma, semicolon or pipe.
    /// Split them out so each becomes its own row, then normalize each.
    /// Also folds well-known aliases ("Science Fiction" → "Sci-Fi").
    /// </summary>
    private static IEnumerable<string> SplitAndAliasGenres(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) yield break;
        foreach (var part in raw.Split(new[] { '/', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var n = NormalizeName(part);
            if (n == null) continue;
            yield return GenreAlias(n);
        }
    }


    private static string GenreAlias(string name) => GenreAliases.Fold(name);

    /// <summary>
    /// Trim, collapse whitespace runs to a single space. Returns null when
    /// the input becomes empty — callers skip null names.
    /// </summary>
    private static string? NormalizeName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var sb = new System.Text.StringBuilder(raw.Length);
        bool wasWhite = false;
        foreach (var ch in raw)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!wasWhite && sb.Length > 0) sb.Append(' ');
                wasWhite = true;
            }
            else
            {
                sb.Append(ch);
                wasWhite = false;
            }
        }
        // strip trailing space
        while (sb.Length > 0 && sb[^1] == ' ') sb.Length--;
        return sb.Length == 0 ? null : sb.ToString();
    }

    private sealed class LookupCache
    {
        // Keyed by lowercased normalized name (so "tom hanks" and "Tom Hanks "
        // collide on the same cache entry → same DB row).
        public Dictionary<string, int> Genres { get; } = new();
        public Dictionary<string, int> Directors { get; } = new();
        public Dictionary<string, int> Actors { get; } = new();
        public Dictionary<string, int> Sets { get; } = new();

        public static LookupCache Load(SqliteConnection conn, SqliteTransaction tx)
        {
            var c = new LookupCache();
            void Fill(string sql, Dictionary<string, int> dest)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var key = NormalizeName(r.GetString(1))?.ToLowerInvariant();
                    if (key != null) dest[key] = r.GetInt32(0);
                }
            }
            Fill("SELECT id, name FROM genres",    c.Genres);
            Fill("SELECT id, name FROM directors", c.Directors);
            Fill("SELECT id, name FROM actors",    c.Actors);
            Fill("SELECT id, name FROM sets",      c.Sets);
            return c;
        }
    }

    /// <summary>
    /// Returns id for (table, name). Cache is keyed lowercase to dedupe
    /// "Tom Hanks" / "tom hanks" / "Tom Hanks ". The DB row stores the
    /// trimmed/collapsed display form (preserves the user's casing).
    /// Returns -1 when name is empty/whitespace.
    /// </summary>
    private static int GetOrInsert(SqliteConnection conn, SqliteTransaction tx,
        string table, string rawName, Dictionary<string, int> cache, string? extraCol = null, object? extraVal = null)
    {
        var norm = NormalizeName(rawName);
        if (norm == null) return -1;
        var key = norm.ToLowerInvariant();
        if (cache.TryGetValue(key, out var id)) return id;

        using var ins = conn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = extraCol == null
            ? $"INSERT INTO {table} (name) VALUES (@n); SELECT last_insert_rowid();"
            : $"INSERT INTO {table} (name, {extraCol}) VALUES (@n, @e); SELECT last_insert_rowid();";
        ins.Parameters.AddWithValue("@n", norm);
        if (extraCol != null) ins.Parameters.AddWithValue("@e", extraVal ?? DBNull.Value);
        id = Convert.ToInt32(ins.ExecuteScalar());
        cache[key] = id;
        return id;
    }

    private static void UpsertRelated(SqliteConnection conn, SqliteTransaction tx, int movieId, ParsedMovie p, LookupCache cache)
    {
        SqliteCommand Cmd(string sql)
        {
            var c = conn.CreateCommand();
            c.CommandText = sql;
            c.Transaction = tx;
            return c;
        }

        using (var d = Cmd("DELETE FROM movie_genres    WHERE movie_id=@id")) { d.Parameters.AddWithValue("@id", movieId); d.ExecuteNonQuery(); }
        using (var d = Cmd("DELETE FROM movie_directors WHERE movie_id=@id")) { d.Parameters.AddWithValue("@id", movieId); d.ExecuteNonQuery(); }
        using (var d = Cmd("DELETE FROM movie_actors    WHERE movie_id=@id")) { d.Parameters.AddWithValue("@id", movieId); d.ExecuteNonQuery(); }
        using (var d = Cmd("DELETE FROM movie_sets      WHERE movie_id=@id")) { d.Parameters.AddWithValue("@id", movieId); d.ExecuteNonQuery(); }

        // Split multi-genre strings ("Action / Adventure / Sci-Fi") into
        // separate rows, and fold common aliases. Dedupe via HashSet so a
        // duplicate after splitting (e.g. "Sci-Fi" already from a separate
        // <genre>) inserts the link only once.
        var seenGenres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in p.Genres)
        {
            foreach (var g in SplitAndAliasGenres(raw))
            {
                if (!seenGenres.Add(g)) continue;
                var gid = GetOrInsert(conn, tx, "genres", g, cache.Genres);
                if (gid < 0) continue;
                using var c = Cmd("INSERT OR IGNORE INTO movie_genres VALUES (@m,@g)");
                c.Parameters.AddWithValue("@m", movieId); c.Parameters.AddWithValue("@g", gid); c.ExecuteNonQuery();
            }
        }
        foreach (var d in p.Directors)
        {
            var did = GetOrInsert(conn, tx, "directors", d, cache.Directors);
            if (did < 0) continue;
            using var c = Cmd("INSERT OR IGNORE INTO movie_directors VALUES (@m,@d)");
            c.Parameters.AddWithValue("@m", movieId); c.Parameters.AddWithValue("@d", did); c.ExecuteNonQuery();
        }
        foreach (var a in p.Actors)
        {
            var aid = GetOrInsert(conn, tx, "actors", a.Name, cache.Actors, "thumb", a.Thumb);
            if (aid < 0) continue;
            using var c = Cmd("INSERT OR REPLACE INTO movie_actors VALUES (@m,@a,@r,@o)");
            c.Parameters.AddWithValue("@m", movieId); c.Parameters.AddWithValue("@a", aid);
            c.Parameters.AddWithValue("@r", (object?)a.Role ?? DBNull.Value); c.Parameters.AddWithValue("@o", a.Order);
            c.ExecuteNonQuery();
        }
        foreach (var s in p.Sets)
        {
            var sid = GetOrInsert(conn, tx, "sets", s, cache.Sets);
            if (sid < 0) continue;
            using var c = Cmd("INSERT OR IGNORE INTO movie_sets VALUES (@m,@s)");
            c.Parameters.AddWithValue("@m", movieId); c.Parameters.AddWithValue("@s", sid); c.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Drive-root subfolders Windows owns and we shouldn't try to read.
    /// Avoids "Access to the path … is denied" when scanning a drive root.
    /// Match is case-insensitive on the folder NAME (last segment).
    /// </summary>
    private static readonly HashSet<string> ExcludedSystemDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "System Volume Information",
        "$RECYCLE.BIN",
        "RECYCLER",
        "Config.Msi",
        "$WinREAgent",
        "Recovery",
    };

    /// <summary>
    /// Manual recursive walk that:
    ///   1. Skips well-known Windows system folders by name.
    ///   2. Swallows UnauthorizedAccessException / IOException per-folder so
    ///      one denied subdirectory doesn't kill the whole scan.
    /// </summary>
    private static IEnumerable<string> FindMovieFolders(string root)
    {
        if (!Directory.Exists(root)) yield break;

        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();

            // Yield this dir if it's a movie folder
            if (FindNfo(dir) != null) yield return dir;

            // Recurse into children, defensively
            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException) { continue; }

            foreach (var sub in subs)
            {
                var name = Path.GetFileName(sub);
                if (ExcludedSystemDirs.Contains(name)) continue;
                // Skip hidden + system attributed folders too (cheap belt-and-braces)
                try
                {
                    var attr = File.GetAttributes(sub);
                    if ((attr & FileAttributes.System) == FileAttributes.System &&
                        (attr & FileAttributes.Hidden) == FileAttributes.Hidden)
                        continue;
                }
                catch { /* if we can't stat it, just try to descend */ }
                stack.Push(sub);
            }
        }
    }

    private static string? FindNfo(string folder)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(folder, "*.nfo"))
            {
                var name = Path.GetFileNameWithoutExtension(f).ToLower();
                if (name != "tvshow" && name != "season") return f;
            }
        }
        catch { }
        return null;
    }

    private static string? FindArtwork(string folder, string[] names, string kind)
    {
        foreach (var n in names)
        {
            var p = Path.Combine(folder, n);
            if (File.Exists(p)) return p;
        }
        try
        {
            foreach (var f in Directory.EnumerateFiles(folder))
            {
                var ext = Path.GetExtension(f).ToLower();
                if (!ImageExts.Contains(ext)) continue;
                var lower = Path.GetFileName(f).ToLower();
                if (lower.Contains($"-{kind}") || lower.StartsWith(kind + ".") || lower.EndsWith(kind + ext))
                    return f;
            }
        }
        catch { }
        return null;
    }

    private static string? CopyToCache(string srcPath, string dataDir, string movieKey, string kind)
    {
        try
        {
            var cacheDir = Path.Combine(dataDir, "cache", movieKey);
            Directory.CreateDirectory(cacheDir);
            var ext = Path.GetExtension(srcPath).ToLower();
            if (string.IsNullOrEmpty(ext)) ext = kind == "nfo" ? ".nfo" : ".jpg";
            var destName = $"{kind}{ext}";
            var destPath = Path.Combine(cacheDir, destName);

            // Skip re-copy when dest already mirrors source. Saves N file copies
            // on a routine rescan where most movies are unchanged. Compare mtime
            // (rounded to seconds — FAT/exFAT only stores 2-second precision)
            // and size; refusing to be cute about it keeps this resilient.
            if (File.Exists(destPath))
            {
                var srcInfo = new FileInfo(srcPath);
                var dstInfo = new FileInfo(destPath);
                if (srcInfo.Length == dstInfo.Length &&
                    Math.Abs((srcInfo.LastWriteTimeUtc - dstInfo.LastWriteTimeUtc).TotalSeconds) < 3)
                {
                    return $"cache/{movieKey}/{destName}";
                }
            }
            File.Copy(srcPath, destPath, overwrite: true);
            return $"cache/{movieKey}/{destName}";
        }
        catch { return null; }
    }

    public const string NoteSidecarFileName = "cinelibrary-note.txt";

    private static void ImportSidecarNoteIfNeeded(SqliteConnection conn, SqliteTransaction tx, int movieId, string folder)
    {
        var sidecar = Path.Combine(folder, NoteSidecarFileName);
        if (!File.Exists(sidecar)) return;

        // Only import when DB column is empty — protects user edits made
        // inside the app from being overwritten by a stale sidecar.
        using var check = conn.CreateCommand();
        check.Transaction = tx;
        check.CommandText = "SELECT note FROM movies WHERE id=@id";
        check.Parameters.AddWithValue("@id", movieId);
        var existing = check.ExecuteScalar();
        var hasDbNote = existing != null && existing != DBNull.Value &&
                        !string.IsNullOrWhiteSpace(existing.ToString());
        if (hasDbNote) return;

        string text;
        try { text = File.ReadAllText(sidecar); }
        catch { return; }
        if (string.IsNullOrWhiteSpace(text)) return;

        using var upd = conn.CreateCommand();
        upd.Transaction = tx;
        upd.CommandText = "UPDATE movies SET note=@n WHERE id=@id";
        upd.Parameters.AddWithValue("@n", text);
        upd.Parameters.AddWithValue("@id", movieId);
        upd.ExecuteNonQuery();
    }

    private static string ComputeMovieKey(string volumeSerial, string folderRelPath)
    {
        var input = $"{volumeSerial}|{folderRelPath}";
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16].ToLower();
    }
}
