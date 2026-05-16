using Microsoft.Data.Sqlite;
using CineLibraryCS.Models;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace CineLibraryCS.Services;

public class DatabaseService : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly string _dataDir;

    public DatabaseService(string dataDir)
    {
        _dataDir = dataDir;
        Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, "cinelibrary.db");
        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        ExecutePragmas();
        CreateSchema();
        RunMigrations();
    }

    // ── Schema ──────────────────────────────────────────────────────────────

    private void ExecutePragmas()
    {
        Exec("PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;");
    }

    private void CreateSchema()
    {
        Exec(@"
CREATE TABLE IF NOT EXISTS drives (
    volume_serial TEXT PRIMARY KEY,
    label TEXT NOT NULL,
    last_seen_letter TEXT,
    last_connected_at INTEGER,
    movie_root_relative TEXT NOT NULL DEFAULT 'Movies'
);

CREATE TABLE IF NOT EXISTS drive_roots (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    volume_serial TEXT NOT NULL REFERENCES drives(volume_serial) ON DELETE CASCADE,
    root_path TEXT NOT NULL,
    UNIQUE(volume_serial, root_path)
);

CREATE TABLE IF NOT EXISTS movies (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    volume_serial TEXT NOT NULL REFERENCES drives(volume_serial) ON DELETE CASCADE,
    folder_rel_path TEXT NOT NULL,
    video_file_rel_path TEXT,
    title TEXT NOT NULL,
    original_title TEXT,
    sort_title TEXT,
    year INTEGER,
    rating REAL,
    votes INTEGER,
    runtime INTEGER,
    plot TEXT,
    outline TEXT,
    tagline TEXT,
    mpaa TEXT,
    imdb_id TEXT,
    tmdb_id TEXT,
    premiered TEXT,
    studio TEXT,
    country TEXT,
    trailer TEXT,
    local_poster TEXT,
    local_fanart TEXT,
    local_nfo TEXT,
    is_missing INTEGER DEFAULT 0,
    is_favorite INTEGER DEFAULT 0,
    is_watched INTEGER DEFAULT 0,
    date_added INTEGER DEFAULT (strftime('%s','now')),
    date_modified INTEGER DEFAULT (strftime('%s','now')),
    UNIQUE(volume_serial, folder_rel_path)
);

CREATE TABLE IF NOT EXISTS genres (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT UNIQUE NOT NULL
);
CREATE TABLE IF NOT EXISTS movie_genres (
    movie_id INTEGER REFERENCES movies(id) ON DELETE CASCADE,
    genre_id INTEGER REFERENCES genres(id) ON DELETE CASCADE,
    PRIMARY KEY(movie_id, genre_id)
);

CREATE TABLE IF NOT EXISTS directors (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT UNIQUE NOT NULL
);
CREATE TABLE IF NOT EXISTS movie_directors (
    movie_id INTEGER REFERENCES movies(id) ON DELETE CASCADE,
    director_id INTEGER REFERENCES directors(id) ON DELETE CASCADE,
    PRIMARY KEY(movie_id, director_id)
);

CREATE TABLE IF NOT EXISTS actors (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT UNIQUE NOT NULL,
    thumb TEXT
);
CREATE TABLE IF NOT EXISTS movie_actors (
    movie_id INTEGER REFERENCES movies(id) ON DELETE CASCADE,
    actor_id INTEGER REFERENCES actors(id) ON DELETE CASCADE,
    role TEXT,
    sort_order INTEGER DEFAULT 0,
    PRIMARY KEY(movie_id, actor_id)
);

CREATE TABLE IF NOT EXISTS sets (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT UNIQUE NOT NULL
);
CREATE TABLE IF NOT EXISTS movie_sets (
    movie_id INTEGER REFERENCES movies(id) ON DELETE CASCADE,
    set_id INTEGER REFERENCES sets(id) ON DELETE CASCADE,
    PRIMARY KEY(movie_id, set_id)
);

CREATE TABLE IF NOT EXISTS preferences (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

-- v1.9.2 — user-defined custom lists (e.g. ""Date night"", ""80s sci-fi"").
-- Movies can be in many lists; deleting a list cascades the join rows.
CREATE TABLE IF NOT EXISTS user_lists (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT UNIQUE NOT NULL,
    created_at INTEGER DEFAULT (strftime('%s','now')),
    sort_order INTEGER DEFAULT 0
);
CREATE TABLE IF NOT EXISTS user_list_movies (
    list_id INTEGER REFERENCES user_lists(id) ON DELETE CASCADE,
    movie_id INTEGER REFERENCES movies(id) ON DELETE CASCADE,
    added_at INTEGER DEFAULT (strftime('%s','now')),
    PRIMARY KEY (list_id, movie_id)
);
");
    }

    private void RunMigrations()
    {
        var cols = new HashSet<string>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(movies)";
        using var r = cmd.ExecuteReader();
        while (r.Read()) cols.Add(r.GetString(1));

        if (!cols.Contains("is_watched"))
            Exec("ALTER TABLE movies ADD COLUMN is_watched INTEGER DEFAULT 0");
        if (!cols.Contains("is_favorite"))
            Exec("ALTER TABLE movies ADD COLUMN is_favorite INTEGER DEFAULT 0");
        if (!cols.Contains("is_watchlist"))
            Exec("ALTER TABLE movies ADD COLUMN is_watchlist INTEGER DEFAULT 0");
        if (!cols.Contains("last_played_at"))
            Exec("ALTER TABLE movies ADD COLUMN last_played_at INTEGER DEFAULT 0");
        if (!cols.Contains("note"))
            Exec("ALTER TABLE movies ADD COLUMN note TEXT");
        // v2.2 — stream details + file info + writers fields
        if (!cols.Contains("video_width"))      Exec("ALTER TABLE movies ADD COLUMN video_width INTEGER");
        if (!cols.Contains("video_height"))     Exec("ALTER TABLE movies ADD COLUMN video_height INTEGER");
        if (!cols.Contains("video_codec"))      Exec("ALTER TABLE movies ADD COLUMN video_codec TEXT");
        if (!cols.Contains("video_aspect"))     Exec("ALTER TABLE movies ADD COLUMN video_aspect TEXT");
        if (!cols.Contains("hdr_type"))         Exec("ALTER TABLE movies ADD COLUMN hdr_type TEXT");
        if (!cols.Contains("audio_codec"))      Exec("ALTER TABLE movies ADD COLUMN audio_codec TEXT");
        if (!cols.Contains("audio_channels"))   Exec("ALTER TABLE movies ADD COLUMN audio_channels TEXT");
        if (!cols.Contains("audio_languages"))  Exec("ALTER TABLE movies ADD COLUMN audio_languages TEXT");
        if (!cols.Contains("subtitle_languages"))Exec("ALTER TABLE movies ADD COLUMN subtitle_languages TEXT");
        if (!cols.Contains("duration_seconds")) Exec("ALTER TABLE movies ADD COLUMN duration_seconds INTEGER");
        if (!cols.Contains("container_ext"))    Exec("ALTER TABLE movies ADD COLUMN container_ext TEXT");
        if (!cols.Contains("file_size_bytes"))  Exec("ALTER TABLE movies ADD COLUMN file_size_bytes INTEGER");

        // Multi-source ratings table (TMDb / IMDb / Rotten Tomatoes / …)
        Exec(@"
CREATE TABLE IF NOT EXISTS movie_ratings (
    movie_id INTEGER REFERENCES movies(id) ON DELETE CASCADE,
    source   TEXT NOT NULL,
    value    REAL NOT NULL,
    votes    INTEGER,
    PRIMARY KEY (movie_id, source)
);
");

        // Writers (separate table to mirror directors)
        Exec(@"
CREATE TABLE IF NOT EXISTS writers (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT UNIQUE NOT NULL
);
CREATE TABLE IF NOT EXISTS movie_writers (
    movie_id  INTEGER REFERENCES movies(id) ON DELETE CASCADE,
    writer_id INTEGER REFERENCES writers(id) ON DELETE CASCADE,
    PRIMARY KEY (movie_id, writer_id)
);
");

        // v1.9.2 — ensure user_lists tables exist on databases that pre-date them
        Exec(@"
CREATE TABLE IF NOT EXISTS user_lists (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT UNIQUE NOT NULL,
    created_at INTEGER DEFAULT (strftime('%s','now')),
    sort_order INTEGER DEFAULT 0
);
CREATE TABLE IF NOT EXISTS user_list_movies (
    list_id INTEGER REFERENCES user_lists(id) ON DELETE CASCADE,
    movie_id INTEGER REFERENCES movies(id) ON DELETE CASCADE,
    added_at INTEGER DEFAULT (strftime('%s','now')),
    PRIMARY KEY (list_id, movie_id)
);
");

        // v1.9.2 — one-shot normalization of pre-existing actor/set rows
        // (whitespace drift made "Tom Hanks" and "Tom Hanks " distinct rows,
        // which split actor/collection counts in the UI). Migration failures
        // are logged but never block app launch — user can still use the
        // catalog even if normalization couldn't complete on their data.
        var normPrefKey = "_migration_normalize_v192";
        if (GetPref(normPrefKey) != "done")
        {
            try
            {
                NormalizeAndMergeNames("actors",   "movie_actors",   "actor_id");
                NormalizeAndMergeNames("directors","movie_directors","director_id");
                NormalizeAndMergeNames("genres",   "movie_genres",   "genre_id");
                NormalizeAndMergeNames("sets",     "movie_sets",     "set_id");
                SetPref(normPrefKey, "done");
            }
            catch (Exception ex)
            {
                // Don't mark done — we'll retry next launch with fixed code.
                System.Diagnostics.Debug.WriteLine($"v1.9.2 normalization skipped: {ex.Message}");
            }
        }

        // v2.1.x — split multi-genre rows and fold aliases into canonical
        // English names. Migration key tracks the alias map version, so
        // expanding the alias dictionary (e.g. v2.1.1 adds Arabic, Hindi…)
        // triggers a re-run automatically — no manual user action needed.
        var genreSplitKey = $"_migration_split_genres_{GenreAliases.Version}";
        if (GetPref(genreSplitKey) != "done")
        {
            try { SplitAndAliasGenresMigration(); SetPref(genreSplitKey, "done"); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"genre split {GenreAliases.Version} skipped: {ex.Message}");
            }
        }

        // v2.1.1 — re-extract sets from cached .nfo files using the broader
        // parser (covers <collection>, <sets><set>…, <setname>, etc.).
        // Cheap targeted re-parse: doesn't re-copy artwork or re-process the
        // rest of the metadata — just rebuilds movie_sets linkage.
        var setRescanKey = "_migration_recompute_sets_v211";
        if (GetPref(setRescanKey) != "done")
        {
            try { RecomputeAllSetsFromNfos(); SetPref(setRescanKey, "done"); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"sets recompute v2.1.1 skipped: {ex.Message}");
            }
        }

        // v2.2 — re-extract stream details, multiple ratings, writers, trailer
        // from cached .nfo files. Same approach as the v2.1.1 sets migration —
        // user doesn't need to do a full rescan to gain the new fields.
        var richNfoKey = "_migration_rich_nfo_v220";
        if (GetPref(richNfoKey) != "done")
        {
            try { RecomputeRichNfoFields(); SetPref(richNfoKey, "done"); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"rich nfo recompute v2.2 skipped: {ex.Message}");
            }
        }

        // Create indexes for performance
        CreateIndexes();
    }

    /// <summary>
    /// Dedupe + normalize a name table. Order matters:
    ///   1. Read every row, normalize its name in memory.
    ///   2. Group by normalized name; pick MIN(id) as keeper per group.
    ///   3. Merge join rows from other ids → keeper, then delete the other rows.
    ///   4. UPDATE keepers to the normalized form (now safe — no collisions left).
    ///
    /// Doing it in this order matters because the name column has a UNIQUE
    /// constraint. If we tried to TRIM in place first, "Tom Hanks " collapsing
    /// to "Tom Hanks" would collide with an existing "Tom Hanks" row and throw,
    /// killing DB construction and bricking the app at launch (this was the
    /// v1.9.2 first-cut bug).
    /// </summary>

    /// <summary>
    /// One-shot v2.1.0 fix for genre rows that contain multiple genres in
    /// one string (e.g. "Action / Adventure / Sci-Fi") plus alias drift
    /// (Science Fiction → Sci-Fi). For every offender, splits into separate
    /// canonical names, repoints movie_genres links to the canonical rows,
    /// then deletes the original multi-genre row.
    /// </summary>
    /// <summary>
    /// Walks every movie's cached .nfo file, re-parses it for sets only via
    /// the broadened NfoParser, and rebuilds movie_sets linkage. Used as a
    /// one-shot v2.1.1 migration so users don't need to do a full rescan to
    /// pick up collections their nfos always had (in formats we didn't read).
    /// </summary>
    private void RecomputeAllSetsFromNfos()
    {
        // Snapshot (movie_id, nfo_path) pairs
        var movies = new List<(int id, string nfoRel)>();
        using (var sel = _conn.CreateCommand())
        {
            sel.CommandText = "SELECT id, local_nfo FROM movies WHERE local_nfo IS NOT NULL AND is_missing=0";
            using var r = sel.ExecuteReader();
            while (r.Read())
                movies.Add((r.GetInt32(0), r.GetString(1)));
        }
        if (movies.Count == 0) return;

        // Cache existing set names → ids (case-insensitive)
        var setCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using (var sel = _conn.CreateCommand())
        {
            sel.CommandText = "SELECT id, name FROM sets";
            using var r = sel.ExecuteReader();
            while (r.Read())
            {
                var nm = r.GetString(1).Trim();
                if (nm.Length > 0) setCache[nm] = r.GetInt32(0);
            }
        }

        using var tx = _conn.BeginTransaction();
        try
        {
            foreach (var (movieId, nfoRel) in movies)
            {
                var nfoPath = Path.Combine(_dataDir, nfoRel);
                if (!File.Exists(nfoPath)) continue;

                ParsedMovie? parsed;
                try { parsed = NfoParser.Parse(nfoPath); }
                catch { continue; }
                if (parsed == null) continue;

                // Drop existing links, re-insert from the re-parsed list
                using (var del = _conn.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM movie_sets WHERE movie_id=@m";
                    del.Parameters.AddWithValue("@m", movieId);
                    del.ExecuteNonQuery();
                }

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var raw in parsed.Sets)
                {
                    var name = (raw ?? "").Trim();
                    if (string.IsNullOrEmpty(name) || !seen.Add(name)) continue;

                    if (!setCache.TryGetValue(name, out var setId))
                    {
                        using var ins = _conn.CreateCommand();
                        ins.Transaction = tx;
                        ins.CommandText = "INSERT INTO sets (name) VALUES (@n); SELECT last_insert_rowid();";
                        ins.Parameters.AddWithValue("@n", name);
                        setId = Convert.ToInt32(ins.ExecuteScalar());
                        setCache[name] = setId;
                    }

                    using var link = _conn.CreateCommand();
                    link.Transaction = tx;
                    link.CommandText = "INSERT OR IGNORE INTO movie_sets VALUES (@m, @s)";
                    link.Parameters.AddWithValue("@m", movieId);
                    link.Parameters.AddWithValue("@s", setId);
                    link.ExecuteNonQuery();
                }
            }
            tx.Commit();
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }

    /// <summary>
    /// v2.2 one-shot: walks every cached .nfo and refreshes the new fields
    /// (stream details, multi-source ratings, writers, trailer) without
    /// touching artwork or other already-correct metadata.
    /// </summary>
    private void RecomputeRichNfoFields()
    {
        var rows = new List<(int id, string nfoRel, string? videoRel, string letter, string serial)>();
        var connected = GetConnectedDrives();
        using (var sel = _conn.CreateCommand())
        {
            sel.CommandText = @"SELECT id, local_nfo, video_file_rel_path, volume_serial
                                FROM movies WHERE local_nfo IS NOT NULL AND is_missing=0";
            using var r = sel.ExecuteReader();
            while (r.Read())
            {
                var serial = r.GetString(3);
                connected.TryGetValue(serial, out var letter);
                rows.Add((r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), letter ?? "", serial));
            }
        }
        if (rows.Count == 0) return;

        var writerCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var preload = _conn.CreateCommand();
            preload.CommandText = "SELECT id, name FROM writers";
            using var r = preload.ExecuteReader();
            while (r.Read()) writerCache[r.GetString(1)] = r.GetInt32(0);
        }
        catch { }

        using var tx = _conn.BeginTransaction();
        try
        {
            foreach (var row in rows)
            {
                var nfoPath = Path.Combine(_dataDir, row.nfoRel);
                if (!File.Exists(nfoPath)) continue;

                ParsedMovie? p;
                try { p = NfoParser.Parse(nfoPath); }
                catch { continue; }
                if (p == null) continue;

                var sd = p.Stream;

                // Update stream + trailer columns
                using (var upd = _conn.CreateCommand())
                {
                    upd.Transaction = tx;
                    upd.CommandText = @"UPDATE movies SET
                        trailer=@tr,
                        video_width=@vw, video_height=@vh, video_codec=@vc, video_aspect=@va,
                        hdr_type=@hdr, audio_codec=@ac, audio_channels=@ach,
                        audio_languages=@al, subtitle_languages=@sl, duration_seconds=@dur
                        WHERE id=@id";
                    upd.Parameters.AddWithValue("@id", row.id);
                    upd.Parameters.AddWithValue("@tr",  (object?)p.Trailer ?? DBNull.Value);
                    upd.Parameters.AddWithValue("@vw",  (object?)sd?.VideoWidth        ?? DBNull.Value);
                    upd.Parameters.AddWithValue("@vh",  (object?)sd?.VideoHeight       ?? DBNull.Value);
                    upd.Parameters.AddWithValue("@vc",  (object?)sd?.VideoCodec        ?? DBNull.Value);
                    upd.Parameters.AddWithValue("@va",  (object?)sd?.VideoAspect       ?? DBNull.Value);
                    upd.Parameters.AddWithValue("@hdr", (object?)sd?.HdrType           ?? DBNull.Value);
                    upd.Parameters.AddWithValue("@ac",  (object?)sd?.AudioCodec        ?? DBNull.Value);
                    upd.Parameters.AddWithValue("@ach", (object?)sd?.AudioChannels     ?? DBNull.Value);
                    upd.Parameters.AddWithValue("@al",  (object?)sd?.AudioLanguages    ?? DBNull.Value);
                    upd.Parameters.AddWithValue("@sl",  (object?)sd?.SubtitleLanguages ?? DBNull.Value);
                    upd.Parameters.AddWithValue("@dur", (object?)sd?.DurationSeconds   ?? DBNull.Value);
                    upd.ExecuteNonQuery();
                }

                // File size + container — only if drive online + file present
                if (!string.IsNullOrEmpty(row.letter) && row.videoRel != null)
                {
                    var full = Path.Combine($"{row.letter}:\\", row.videoRel.Replace('/', '\\'));
                    if (File.Exists(full))
                    {
                        long size = 0;
                        try { size = new FileInfo(full).Length; } catch { }
                        var ext = Path.GetExtension(full).TrimStart('.').ToLowerInvariant();
                        using var upd = _conn.CreateCommand();
                        upd.Transaction = tx;
                        upd.CommandText = "UPDATE movies SET file_size_bytes=@s, container_ext=@x WHERE id=@id";
                        upd.Parameters.AddWithValue("@s", size);
                        upd.Parameters.AddWithValue("@x", ext);
                        upd.Parameters.AddWithValue("@id", row.id);
                        upd.ExecuteNonQuery();
                    }
                }

                // Ratings — rebuild
                using (var del = _conn.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM movie_ratings WHERE movie_id=@id";
                    del.Parameters.AddWithValue("@id", row.id);
                    del.ExecuteNonQuery();
                }
                foreach (var rt in p.Ratings)
                {
                    using var ins = _conn.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = "INSERT OR REPLACE INTO movie_ratings VALUES (@m,@s,@v,@vt)";
                    ins.Parameters.AddWithValue("@m", row.id);
                    ins.Parameters.AddWithValue("@s", rt.Source);
                    ins.Parameters.AddWithValue("@v", rt.Value);
                    ins.Parameters.AddWithValue("@vt", (object?)rt.Votes ?? DBNull.Value);
                    ins.ExecuteNonQuery();
                }

                // Writers — rebuild
                using (var del = _conn.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM movie_writers WHERE movie_id=@id";
                    del.Parameters.AddWithValue("@id", row.id);
                    del.ExecuteNonQuery();
                }
                foreach (var name in p.Writers)
                {
                    var clean = name.Trim();
                    if (clean.Length == 0) continue;
                    if (!writerCache.TryGetValue(clean, out var wid))
                    {
                        using var ins = _conn.CreateCommand();
                        ins.Transaction = tx;
                        ins.CommandText = "INSERT INTO writers (name) VALUES (@n); SELECT last_insert_rowid();";
                        ins.Parameters.AddWithValue("@n", clean);
                        wid = Convert.ToInt32(ins.ExecuteScalar());
                        writerCache[clean] = wid;
                    }
                    using var link = _conn.CreateCommand();
                    link.Transaction = tx;
                    link.CommandText = "INSERT OR IGNORE INTO movie_writers VALUES (@m,@w)";
                    link.Parameters.AddWithValue("@m", row.id);
                    link.Parameters.AddWithValue("@w", wid);
                    link.ExecuteNonQuery();
                }
            }
            tx.Commit();
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }

    private void SplitAndAliasGenresMigration()
    {
        // Shared map — single source of truth for alias folding.
        var aliases = GenreAliases.Map;
        static string CanonicalSeg(string s)
        {
            var n = s.Trim();
            n = System.Text.RegularExpressions.Regex.Replace(n, @"\s+", " ");
            return n;
        }

        var rows = new List<(int id, string name)>();
        using (var sel = _conn.CreateCommand())
        {
            sel.CommandText = "SELECT id, name FROM genres";
            using var r = sel.ExecuteReader();
            while (r.Read()) rows.Add((r.GetInt32(0), r.GetString(1)));
        }

        var canon = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, name) in rows) canon[name] = id;

        using var tx = _conn.BeginTransaction();
        try
        {
            foreach (var (id, name) in rows)
            {
                var parts = name.Split(new[] { '/', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(CanonicalSeg)
                                .Where(s => s.Length > 0)
                                .Select(s => aliases.TryGetValue(s, out var c) ? c : s)
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList();

                if (parts.Count == 1 && string.Equals(parts[0], name, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (parts.Count == 0) continue;

                var partIds = new List<int>();
                foreach (var part in parts)
                {
                    if (!canon.TryGetValue(part, out var pid))
                    {
                        using var ins = _conn.CreateCommand();
                        ins.Transaction = tx;
                        ins.CommandText = "INSERT INTO genres (name) VALUES (@n); SELECT last_insert_rowid();";
                        ins.Parameters.AddWithValue("@n", part);
                        pid = Convert.ToInt32(ins.ExecuteScalar());
                        canon[part] = pid;
                    }
                    if (pid != id) partIds.Add(pid);
                }
                if (partIds.Count == 0) continue;

                var linkedMovies = new List<int>();
                using (var s2 = _conn.CreateCommand())
                {
                    s2.Transaction = tx;
                    s2.CommandText = "SELECT movie_id FROM movie_genres WHERE genre_id=@id";
                    s2.Parameters.AddWithValue("@id", id);
                    using var rr = s2.ExecuteReader();
                    while (rr.Read()) linkedMovies.Add(rr.GetInt32(0));
                }
                foreach (var mid in linkedMovies)
                    foreach (var pid in partIds)
                    {
                        using var link = _conn.CreateCommand();
                        link.Transaction = tx;
                        link.CommandText = "INSERT OR IGNORE INTO movie_genres VALUES (@m, @g)";
                        link.Parameters.AddWithValue("@m", mid);
                        link.Parameters.AddWithValue("@g", pid);
                        link.ExecuteNonQuery();
                    }

                using (var del1 = _conn.CreateCommand())
                {
                    del1.Transaction = tx;
                    del1.CommandText = "DELETE FROM movie_genres WHERE genre_id=@id";
                    del1.Parameters.AddWithValue("@id", id);
                    del1.ExecuteNonQuery();
                }
                using (var del2 = _conn.CreateCommand())
                {
                    del2.Transaction = tx;
                    del2.CommandText = "DELETE FROM genres WHERE id=@id";
                    del2.Parameters.AddWithValue("@id", id);
                    del2.ExecuteNonQuery();
                }
            }
            tx.Commit();
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }

    private void NormalizeAndMergeNames(string nameTable, string joinTable, string fkCol)
    {
        // 1. Snapshot all rows
        var rows = new List<(int id, string raw)>();
        using (var sel = _conn.CreateCommand())
        {
            sel.CommandText = $"SELECT id, name FROM {nameTable}";
            using var r = sel.ExecuteReader();
            while (r.Read()) rows.Add((r.GetInt32(0), r.GetString(1)));
        }

        // 2. Group by normalized lowercase name
        static string Normalize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var sb = new StringBuilder(s.Length);
            bool prevWhite = false;
            foreach (var ch in s)
            {
                if (char.IsWhiteSpace(ch))
                {
                    if (!prevWhite && sb.Length > 0) sb.Append(' ');
                    prevWhite = true;
                }
                else { sb.Append(ch); prevWhite = false; }
            }
            while (sb.Length > 0 && sb[^1] == ' ') sb.Length--;
            return sb.ToString();
        }

        var groups = new Dictionary<string, List<(int id, string raw)>>();
        foreach (var row in rows)
        {
            var key = Normalize(row.raw).ToLowerInvariant();
            if (string.IsNullOrEmpty(key)) continue;
            if (!groups.TryGetValue(key, out var list))
                groups[key] = list = new();
            list.Add(row);
        }

        // 3 + 4. One transaction for the whole table — large libraries can
        // produce thousands of UPDATEs, doing it in one tx is much faster
        // than per-row commits and either succeeds whole or rolls back.
        using var tx = _conn.BeginTransaction();
        try
        {
            foreach (var (key, list) in groups)
            {
                if (list.Count == 0) continue;
                // Keeper = lowest id; rest are duplicates
                list.Sort((a, b) => a.id.CompareTo(b.id));
                var keepId = list[0].id;
                var normalizedDisplay = Normalize(list[0].raw);

                for (int i = 1; i < list.Count; i++)
                {
                    var dupId = list[i].id;
                    // Repoint join rows to the keeper, ignoring those that
                    // would collide on (movie_id, fk) primary key — those
                    // are the same association via different ids and one
                    // row is enough.
                    using (var upd = _conn.CreateCommand())
                    {
                        upd.Transaction = tx;
                        upd.CommandText = $"UPDATE OR IGNORE {joinTable} SET {fkCol}=@k WHERE {fkCol}=@d";
                        upd.Parameters.AddWithValue("@k", keepId);
                        upd.Parameters.AddWithValue("@d", dupId);
                        upd.ExecuteNonQuery();
                    }
                    using (var del = _conn.CreateCommand())
                    {
                        del.Transaction = tx;
                        del.CommandText = $"DELETE FROM {joinTable} WHERE {fkCol}=@d";
                        del.Parameters.AddWithValue("@d", dupId);
                        del.ExecuteNonQuery();
                    }
                    using (var delName = _conn.CreateCommand())
                    {
                        delName.Transaction = tx;
                        delName.CommandText = $"DELETE FROM {nameTable} WHERE id=@d";
                        delName.Parameters.AddWithValue("@d", dupId);
                        delName.ExecuteNonQuery();
                    }
                }

                // Now safely update the keeper to its normalized display form
                // (no more collisions possible — duplicates are gone).
                if (normalizedDisplay != list[0].raw)
                {
                    using var updName = _conn.CreateCommand();
                    updName.Transaction = tx;
                    updName.CommandText = $"UPDATE {nameTable} SET name=@n WHERE id=@id";
                    updName.Parameters.AddWithValue("@n", normalizedDisplay);
                    updName.Parameters.AddWithValue("@id", keepId);
                    updName.ExecuteNonQuery();
                }
            }
            tx.Commit();
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }

    private void CreateIndexes()
    {
        Exec(@"
CREATE INDEX IF NOT EXISTS idx_movies_title ON movies(title);
CREATE INDEX IF NOT EXISTS idx_movies_year ON movies(year);
CREATE INDEX IF NOT EXISTS idx_movies_volume ON movies(volume_serial);
CREATE INDEX IF NOT EXISTS idx_movie_genres_genre_id ON movie_genres(genre_id);
CREATE INDEX IF NOT EXISTS idx_movie_genres_movie_id ON movie_genres(movie_id);
CREATE INDEX IF NOT EXISTS idx_movie_directors_director_id ON movie_directors(director_id);
CREATE INDEX IF NOT EXISTS idx_watchlist ON movies(is_watchlist) WHERE is_watchlist = 1;
CREATE INDEX IF NOT EXISTS idx_favorite ON movies(is_favorite) WHERE is_favorite = 1;
CREATE INDEX IF NOT EXISTS idx_watched ON movies(is_watched) WHERE is_watched = 1;
CREATE INDEX IF NOT EXISTS idx_last_played ON movies(last_played_at) WHERE last_played_at > 0;
CREATE INDEX IF NOT EXISTS idx_user_list_movies_movie ON user_list_movies(movie_id);
CREATE INDEX IF NOT EXISTS idx_user_list_movies_list ON user_list_movies(list_id);
        ");
    }

    // ── Drives ───────────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.Synchronized)]
    public List<Models.DriveInfo> GetDrives()
    {
        var list = new List<Models.DriveInfo>();
        var connected = GetConnectedDrives();

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            SELECT d.volume_serial, d.label, d.last_seen_letter, d.movie_root_relative,
                   COUNT(m.id) as movie_count,
                   SUM(CASE WHEN m.is_missing=1 THEN 1 ELSE 0 END) as missing_count
            FROM drives d
            LEFT JOIN movies m ON m.volume_serial = d.volume_serial
            GROUP BY d.volume_serial";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var serial = r.GetString(0);
            var letter = connected.TryGetValue(serial, out var l) ? l : null;
            list.Add(new Models.DriveInfo
            {
                VolumeSerial = serial,
                Label = r.GetString(1),
                LastSeenLetter = r.IsDBNull(2) ? null : r.GetString(2),
                MovieRootRelative = r.IsDBNull(3) ? "Movies" : r.GetString(3),
                IsConnected = letter != null,
                CurrentLetter = letter,
                MovieCount = r.IsDBNull(4) ? 0 : r.GetInt32(4),
                MissingCount = r.IsDBNull(5) ? 0 : r.GetInt32(5),
            });
        }
        r.Close();
        foreach (var d in list)
            d.Folders = GetDriveRoots(d.VolumeSerial);
        return list;
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public List<Models.DriveRoot> GetDriveRoots(string serial)
    {
        var list = new List<Models.DriveRoot>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, volume_serial, root_path FROM drive_roots WHERE volume_serial=@s ORDER BY root_path";
        cmd.Parameters.AddWithValue("@s", serial);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Models.DriveRoot
            {
                Id = r.GetInt32(0),
                VolumeSerial = r.GetString(1),
                RootPath = r.GetString(2),
            });
        }
        return list;
    }

    /// <summary>
    /// Delete movies whose folder_rel_path isn't under any of the drive's
    /// configured drive_roots. Used to undo the v1.9.2 "Refresh changes"
    /// bug that walked the whole drive and inserted episode .nfo files as
    /// fake movies. Returns count deleted. Also clears their cached artwork.
    /// </summary>
    [MethodImpl(MethodImplOptions.Synchronized)]
    public int RemoveMoviesOutsideDriveRoots(string serial)
    {
        var roots = GetDriveRoots(serial);
        if (roots.Count == 0) return 0;

        // Build the "keep" predicate
        var keepParts = new List<string>();
        var pars = new List<(string name, string val)>();
        for (int i = 0; i < roots.Count; i++)
        {
            var r = roots[i].RootPath ?? "";
            // Empty root_path means "whole drive" — match everything, nothing to remove
            if (string.IsNullOrEmpty(r)) return 0;
            keepParts.Add($"(folder_rel_path = @r{i} OR folder_rel_path LIKE @p{i})");
            pars.Add(($"@r{i}", r));
            pars.Add(($"@p{i}", r + "/%"));
        }
        var keepSql = string.Join(" OR ", keepParts);

        // First, collect cached artwork paths so we can clean them up
        using (var sel = _conn.CreateCommand())
        {
            sel.CommandText = $"SELECT local_poster, local_fanart, local_nfo FROM movies WHERE volume_serial=@s AND NOT ({keepSql})";
            sel.Parameters.AddWithValue("@s", serial);
            foreach (var (n, v) in pars) sel.Parameters.AddWithValue(n, v);
            using var r = sel.ExecuteReader();
            while (r.Read())
            {
                for (int i = 0; i < 3; i++)
                {
                    if (!r.IsDBNull(i))
                    {
                        var p = Path.Combine(_dataDir, r.GetString(i));
                        if (File.Exists(p)) try { File.Delete(p); } catch { }
                    }
                }
            }
        }

        using var del = _conn.CreateCommand();
        del.CommandText = $"DELETE FROM movies WHERE volume_serial=@s AND NOT ({keepSql})";
        del.Parameters.AddWithValue("@s", serial);
        foreach (var (n, v) in pars) del.Parameters.AddWithValue(n, v);
        return del.ExecuteNonQuery();
    }

    /// <summary>Purge movies flagged is_missing=1 for this drive (plus their cached artwork). Returns count deleted.</summary>
    [MethodImpl(MethodImplOptions.Synchronized)]
    public int CleanupMissingMovies(string serial)
    {
        // First, collect + delete cached artwork for missing rows
        using (var get = _conn.CreateCommand())
        {
            get.CommandText = "SELECT local_poster, local_fanart, local_nfo FROM movies WHERE volume_serial=@s AND is_missing=1";
            get.Parameters.AddWithValue("@s", serial);
            using var rr = get.ExecuteReader();
            while (rr.Read())
            {
                for (int i = 0; i < 3; i++)
                {
                    if (!rr.IsDBNull(i))
                    {
                        var p = Path.Combine(_dataDir, rr.GetString(i));
                        if (File.Exists(p)) try { File.Delete(p); } catch { }
                    }
                }
            }
        }

        using var del = _conn.CreateCommand();
        del.CommandText = "DELETE FROM movies WHERE volume_serial=@s AND is_missing=1";
        del.Parameters.AddWithValue("@s", serial);
        return del.ExecuteNonQuery();
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void AddDriveRoot(string serial, string relPath)
    {
        var norm = (relPath ?? "").Replace('\\', '/').Trim().TrimEnd('/');
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO drive_roots (volume_serial, root_path) VALUES (@s, @p)";
        cmd.Parameters.AddWithValue("@s", serial);
        cmd.Parameters.AddWithValue("@p", norm);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Removes a tracked folder and purges its movies (+ cached artwork) from the DB.</summary>
    [MethodImpl(MethodImplOptions.Synchronized)]
    public void RemoveDriveRoot(string serial, string relPath)
    {
        var norm = (relPath ?? "").Replace('\\', '/').Trim().TrimEnd('/');

        // Delete cached artwork for movies under this root
        using (var get = _conn.CreateCommand())
        {
            get.CommandText = @"SELECT local_poster, local_fanart, local_nfo FROM movies
                                WHERE volume_serial=@s AND (folder_rel_path=@r OR folder_rel_path LIKE @p)";
            get.Parameters.AddWithValue("@s", serial);
            get.Parameters.AddWithValue("@r", norm);
            get.Parameters.AddWithValue("@p", norm + "/%");
            using var rr = get.ExecuteReader();
            while (rr.Read())
            {
                for (int i = 0; i < 3; i++)
                {
                    if (!rr.IsDBNull(i))
                    {
                        var p = Path.Combine(_dataDir, rr.GetString(i));
                        if (File.Exists(p)) try { File.Delete(p); } catch { }
                    }
                }
            }
        }

        using var tx = _conn.BeginTransaction();
        using (var del1 = _conn.CreateCommand())
        {
            del1.Transaction = tx;
            del1.CommandText = @"DELETE FROM movies
                                 WHERE volume_serial=@s AND (folder_rel_path=@r OR folder_rel_path LIKE @p)";
            del1.Parameters.AddWithValue("@s", serial);
            del1.Parameters.AddWithValue("@r", norm);
            del1.Parameters.AddWithValue("@p", norm + "/%");
            del1.ExecuteNonQuery();
        }
        using (var del2 = _conn.CreateCommand())
        {
            del2.Transaction = tx;
            del2.CommandText = "DELETE FROM drive_roots WHERE volume_serial=@s AND root_path=@p";
            del2.Parameters.AddWithValue("@s", serial);
            del2.Parameters.AddWithValue("@p", norm);
            del2.ExecuteNonQuery();
        }
        tx.Commit();
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public Dictionary<string, string> GetConnectedDrives()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var drive in global::System.IO.DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            try
            {
                var serial = GetVolumeSerial(drive.Name);
                if (serial != null)
                    result[serial] = drive.Name.TrimEnd('\\').TrimEnd(':');
            }
            catch { /* ignore */ }
        }
        return result;
    }

    public static string? GetVolumeSerial(string drivePath)
    {
        try
        {
            uint serial = 0, maxLen = 0, flags = 0;
            var sb = new StringBuilder(256);
            if (GetVolumeInformation(drivePath, sb, 256, out serial, out maxLen, out flags, null, 0))
                return serial.ToString("X8");
        }
        catch { }
        return null;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
    private static extern bool GetVolumeInformation(
        string lpRootPathName, 
        [System.Runtime.InteropServices.Out] StringBuilder lpVolumeNameBuffer, 
        int nVolumeNameSize,
        out uint lpVolumeSerialNumber, 
        out uint lpMaximumComponentLength, 
        out uint lpFileSystemFlags,
        [System.Runtime.InteropServices.Out] StringBuilder? lpFileSystemNameBuffer, 
        int nFileSystemNameSize);

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void AddDrive(string volumeSerial, string label, string lastSeenLetter)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"INSERT OR IGNORE INTO drives (volume_serial, label, last_seen_letter, movie_root_relative)
                            VALUES (@s, @l, @let, 'Movies')";
        cmd.Parameters.AddWithValue("@s", volumeSerial);
        cmd.Parameters.AddWithValue("@l", label);
        cmd.Parameters.AddWithValue("@let", lastSeenLetter);
        cmd.ExecuteNonQuery();
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void UpdateDriveLastSeen(string serial, string letter)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE drives SET last_seen_letter=@l, last_connected_at=strftime('%s','now') WHERE volume_serial=@s";
        cmd.Parameters.AddWithValue("@l", letter);
        cmd.Parameters.AddWithValue("@s", serial);
        cmd.ExecuteNonQuery();
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void RenameDrive(string serial, string newLabel)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE drives SET label=@l WHERE volume_serial=@s";
        cmd.Parameters.AddWithValue("@l", newLabel);
        cmd.Parameters.AddWithValue("@s", serial);
        cmd.ExecuteNonQuery();
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void RemoveDrive(string serial)
    {
        // Clean up cached image files
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT local_poster, local_fanart, local_nfo FROM movies WHERE volume_serial=@s";
        cmd.Parameters.AddWithValue("@s", serial);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            foreach (var idx in new[] { 0, 1, 2 })
            {
                if (!r.IsDBNull(idx))
                {
                    var p = Path.Combine(_dataDir, r.GetString(idx));
                    if (File.Exists(p)) try { File.Delete(p); } catch { }
                }
            }
        }
        r.Close();

        using var del = _conn.CreateCommand();
        del.CommandText = "DELETE FROM drives WHERE volume_serial=@s";
        del.Parameters.AddWithValue("@s", serial);
        del.ExecuteNonQuery();
    }

    // ── Movies List ──────────────────────────────────────────────────────────

    public record ListOptions(
        string? Search = null,
        string SortKey = "title",
        string SortDir = "asc",
        string? DriveSerial = null,
        string? Genre = null,
        string? Actor = null,
        string? Director = null,
        string? Studio = null,
        int? CollectionId = null,
        string WatchedFilter = "all",   // all | watched | unwatched
        bool FavoritesOnly = false,
        bool IsWatchlistOnly = false,
        bool ContinueWatching = false,  // true = last_played_at > 0 AND is_watched = 0
        int? UserListId = null,
        int? DecadeStart = null,        // e.g. 1980 → year in [1980..1989]
        string? RatingBand = null,      // bucket key: "9","8","7","6","5","0"
        int Limit = 60,
        int Offset = 0
    );

    /// <summary>
    /// Total rows that would match these ListOptions ignoring Limit/Offset.
    /// Used for the "X of Y movies" header so we don't mislead the user
    /// with the per-page Movies.Count.
    /// </summary>
    [MethodImpl(MethodImplOptions.Synchronized)]
    public int GetMoviesCount(ListOptions opts)
    {
        var (whereStr, _) = BuildMovieListWhere(opts);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM movies m {whereStr}";
        BindMovieListParams(cmd, opts, includePaging: false);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    /// <summary>
    /// Split the user's search query into whitespace tokens, dropping empties.
    /// Each token is then AND'ed in the SQL so multi-word queries like
    /// "iron man 2008" match titles in any order with punctuation between
    /// them — e.g. "Iron Man (2008)" or "The Iron Man — 2008 Remaster".
    /// </summary>
    private static string[] TokenizeSearch(string s) =>
        s.Trim().Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Escape SQL LIKE wildcards in a user-provided token. Without this,
    /// typing "100%" or "snake_case" would behave as wildcards instead of
    /// literals. Combined with `ESCAPE '\'` in the LIKE clause.
    /// </summary>
    private static string EscapeLike(string s) => s
        .Replace(@"\", @"\\")
        .Replace("%", @"\%")
        .Replace("_", @"\_");

    private (string whereStr, List<string> clauses) BuildMovieListWhere(ListOptions opts)
    {
        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(opts.Search))
        {
            // Multi-token AND: every word in the query has to match somewhere
            // (title / original / plot / tagline / year / actor / director /
            // collection). Order doesn't matter — "hanks forrest" finds
            // "Forrest Gump" via Tom Hanks just as well as "forrest hanks".
            var tokens = TokenizeSearch(opts.Search);
            for (int i = 0; i < tokens.Length; i++)
            {
                var p = $"@q{i}";
                where.Add($@"(m.title LIKE {p} ESCAPE '\' OR m.original_title LIKE {p} ESCAPE '\'
                    OR m.plot LIKE {p} ESCAPE '\' OR m.tagline LIKE {p} ESCAPE '\'
                    OR CAST(m.year AS TEXT) LIKE {p} ESCAPE '\'
                    OR EXISTS (SELECT 1 FROM movie_actors ma JOIN actors a ON a.id=ma.actor_id WHERE ma.movie_id=m.id AND a.name LIKE {p} ESCAPE '\')
                    OR EXISTS (SELECT 1 FROM movie_directors md JOIN directors d ON d.id=md.director_id WHERE md.movie_id=m.id AND d.name LIKE {p} ESCAPE '\')
                    OR EXISTS (SELECT 1 FROM movie_sets ms JOIN sets s ON s.id=ms.set_id WHERE ms.movie_id=m.id AND s.name LIKE {p} ESCAPE '\'))");
            }
        }
        if (opts.DriveSerial != null) where.Add("m.volume_serial=@serial");
        if (opts.Genre != null) where.Add("EXISTS (SELECT 1 FROM movie_genres mg JOIN genres g ON g.id=mg.genre_id WHERE mg.movie_id=m.id AND g.name=@genre)");
        if (opts.Actor != null) where.Add("EXISTS (SELECT 1 FROM movie_actors ma JOIN actors a ON a.id=ma.actor_id WHERE ma.movie_id=m.id AND LOWER(a.name)=LOWER(@actor))");
        if (opts.Director != null) where.Add("EXISTS (SELECT 1 FROM movie_directors md JOIN directors d ON d.id=md.director_id WHERE md.movie_id=m.id AND LOWER(d.name)=LOWER(@director))");
        if (opts.Studio != null) where.Add("LOWER(m.studio)=LOWER(@studio)");
        if (opts.CollectionId != null) where.Add("EXISTS (SELECT 1 FROM movie_sets ms WHERE ms.movie_id=m.id AND ms.set_id=@colId)");
        if (opts.WatchedFilter == "watched") where.Add("m.is_watched=1");
        else if (opts.WatchedFilter == "unwatched") where.Add("m.is_watched=0");
        if (opts.FavoritesOnly) where.Add("m.is_favorite=1");
        if (opts.IsWatchlistOnly) where.Add("m.is_watchlist=1");
        if (opts.ContinueWatching) where.Add("m.last_played_at > 0 AND m.is_watched = 0");
        if (opts.UserListId != null) where.Add("EXISTS (SELECT 1 FROM user_list_movies ulm WHERE ulm.movie_id=m.id AND ulm.list_id=@listId)");
        if (opts.DecadeStart != null)
            where.Add("m.year >= @decLo AND m.year <= @decHi");
        if (opts.RatingBand != null)
            where.Add("m.rating >= @ratLo AND m.rating < @ratHi");
        return (where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "", where);
    }

    private void BindMovieListParams(SqliteCommand cmd, ListOptions opts, bool includePaging)
    {
        if (!string.IsNullOrWhiteSpace(opts.Search))
        {
            var tokens = TokenizeSearch(opts.Search);
            for (int i = 0; i < tokens.Length; i++)
                cmd.Parameters.AddWithValue($"@q{i}", $"%{EscapeLike(tokens[i])}%");
        }
        if (opts.DriveSerial != null) cmd.Parameters.AddWithValue("@serial", opts.DriveSerial);
        if (opts.Genre != null) cmd.Parameters.AddWithValue("@genre", opts.Genre);
        if (opts.Actor != null) cmd.Parameters.AddWithValue("@actor", opts.Actor);
        if (opts.Director != null) cmd.Parameters.AddWithValue("@director", opts.Director);
        if (opts.Studio != null) cmd.Parameters.AddWithValue("@studio", opts.Studio);
        if (opts.CollectionId != null) cmd.Parameters.AddWithValue("@colId", opts.CollectionId);
        if (opts.UserListId != null) cmd.Parameters.AddWithValue("@listId", opts.UserListId);
        if (opts.DecadeStart != null)
        {
            cmd.Parameters.AddWithValue("@decLo", opts.DecadeStart.Value);
            cmd.Parameters.AddWithValue("@decHi", opts.DecadeStart.Value + 9);
        }
        if (opts.RatingBand != null)
        {
            var (lo, hi) = opts.RatingBand switch
            {
                "9" => (9.0, 10.1),
                "8" => (8.0, 9.0),
                "7" => (7.0, 8.0),
                "6" => (6.0, 7.0),
                "5" => (5.0, 6.0),
                _   => (0.0, 5.0),
            };
            cmd.Parameters.AddWithValue("@ratLo", lo);
            cmd.Parameters.AddWithValue("@ratHi", hi);
        }
        if (includePaging)
        {
            cmd.Parameters.AddWithValue("@lim", opts.Limit);
            cmd.Parameters.AddWithValue("@off", opts.Offset);
        }
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public List<MovieListItem> GetMovies(ListOptions opts, Dictionary<string, string> connected)
    {
        var (whereStr, _) = BuildMovieListWhere(opts);
        var sortCol = opts.SortKey switch
        {
            "year" => "m.year",
            "rating" => "m.rating",
            "runtime" => "m.runtime",
            "date_added" => "m.date_added",
            "last_played" => "m.last_played_at",
            _ => "m.sort_title"
        };
        var sortDir = opts.SortDir == "desc" ? "DESC" : "ASC";
        var nullsLast = sortDir == "ASC" ? "NULLS LAST" : "NULLS FIRST";

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT m.id, m.title, m.year, m.rating, m.runtime, m.local_poster,
                   m.is_missing, m.is_favorite, m.is_watched, m.volume_serial, d.label,
                   (SELECT GROUP_CONCAT(g.name, ', ')
                    FROM movie_genres mg JOIN genres g ON g.id=mg.genre_id
                    WHERE mg.movie_id=m.id) as genres_csv,
                   m.is_watchlist
            FROM movies m
            LEFT JOIN drives d ON d.volume_serial=m.volume_serial
            {whereStr}
            ORDER BY {sortCol} {sortDir} {nullsLast}, m.sort_title ASC
            LIMIT @lim OFFSET @off";

        BindMovieListParams(cmd, opts, includePaging: true);

        var list = new List<MovieListItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var serial = r.GetString(9);
            list.Add(new MovieListItem
            {
                Id = r.GetInt32(0),
                Title = r.GetString(1),
                Year = r.IsDBNull(2) ? null : r.GetInt32(2),
                Rating = r.IsDBNull(3) ? null : r.GetDouble(3),
                Runtime = r.IsDBNull(4) ? null : r.GetInt32(4),
                LocalPoster = r.IsDBNull(5) ? null : r.GetString(5),
                IsMissing = r.GetInt32(6) == 1,
                IsFavorite = r.GetInt32(7) == 1,
                IsWatched = r.GetInt32(8) == 1,
                VolumeSerial = serial,
                DriveLabel = r.IsDBNull(10) ? null : r.GetString(10),
                GenresCsv = r.IsDBNull(11) ? null : r.GetString(11),
                IsWatchlist = !r.IsDBNull(12) && r.GetInt32(12) == 1,
                IsOnline = connected.ContainsKey(serial),
            });
        }
        return list;
    }

    // ── Movie Detail ─────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.Synchronized)]
    public MovieDetail? GetMovieDetail(int id, Dictionary<string, string> connected)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            SELECT m.id, m.title, m.original_title, m.year, m.rating, m.runtime,
                   m.plot, m.tagline, m.mpaa, m.imdb_id, m.premiered, m.studio, m.country,
                   m.local_poster, m.local_fanart, m.is_missing, m.is_favorite, m.is_watched,
                   m.is_watchlist, m.volume_serial, d.label, m.folder_rel_path, m.video_file_rel_path,
                   m.outline, m.note, m.trailer,
                   m.video_width, m.video_height, m.video_codec, m.video_aspect, m.hdr_type,
                   m.audio_codec, m.audio_channels, m.audio_languages, m.subtitle_languages,
                   m.duration_seconds, m.container_ext, m.file_size_bytes,
                   m.tmdb_id
            FROM movies m LEFT JOIN drives d ON d.volume_serial=m.volume_serial
            WHERE m.id=@id";
        cmd.Parameters.AddWithValue("@id", id);

        MovieDetail? movie = null;
        using (var r = cmd.ExecuteReader())
        {
            if (!r.Read()) return null;
            var serial = r.GetString(19);
            var letter = connected.TryGetValue(serial, out var l) ? l : null;
            var folderRel = r.IsDBNull(21) ? null : r.GetString(21);
            var videoRel = r.IsDBNull(22) ? null : r.GetString(22);
            var playable = letter != null && videoRel != null && !r.GetBoolean(15);

            movie = new MovieDetail
            {
                Id = r.GetInt32(0),
                Title = r.GetString(1),
                OriginalTitle = r.IsDBNull(2) ? null : r.GetString(2),
                Year = r.IsDBNull(3) ? null : r.GetInt32(3),
                Rating = r.IsDBNull(4) ? null : r.GetDouble(4),
                Runtime = r.IsDBNull(5) ? null : r.GetInt32(5),
                Plot = r.IsDBNull(6) ? null : r.GetString(6),
                Tagline = r.IsDBNull(7) ? null : r.GetString(7),
                Mpaa = r.IsDBNull(8) ? null : r.GetString(8),
                ImdbId = r.IsDBNull(9) ? null : r.GetString(9),
                Premiered = r.IsDBNull(10) ? null : r.GetString(10),
                Studio = r.IsDBNull(11) ? null : r.GetString(11),
                Country = r.IsDBNull(12) ? null : r.GetString(12),
                LocalPoster = r.IsDBNull(13) ? null : r.GetString(13),
                LocalFanart = r.IsDBNull(14) ? null : r.GetString(14),
                IsMissing = r.GetInt32(15) == 1,
                IsFavorite = r.GetInt32(16) == 1,
                IsWatched = r.GetInt32(17) == 1,
                IsWatchlist = r.GetInt32(18) == 1,
                VolumeSerial = serial,
                DriveLabel = r.IsDBNull(20) ? null : r.GetString(20),
                CurrentLetter = letter,
                IsOnline = letter != null,
                Playable = playable,
                FolderRelPath = folderRel,
                VideoFileRelPath = videoRel,
                Outline = r.IsDBNull(23) ? null : r.GetString(23),
                Note = r.IsDBNull(24) ? null : r.GetString(24),
                Trailer = r.IsDBNull(25) ? null : r.GetString(25),
                VideoWidth        = r.IsDBNull(26) ? null : r.GetInt32(26),
                VideoHeight       = r.IsDBNull(27) ? null : r.GetInt32(27),
                VideoCodec        = r.IsDBNull(28) ? null : r.GetString(28),
                VideoAspect       = r.IsDBNull(29) ? null : r.GetString(29),
                HdrType           = r.IsDBNull(30) ? null : r.GetString(30),
                AudioCodec        = r.IsDBNull(31) ? null : r.GetString(31),
                AudioChannels     = r.IsDBNull(32) ? null : r.GetString(32),
                AudioLanguages    = r.IsDBNull(33) ? null : r.GetString(33),
                SubtitleLanguages = r.IsDBNull(34) ? null : r.GetString(34),
                DurationSeconds   = r.IsDBNull(35) ? null : r.GetInt32(35),
                ContainerExt      = r.IsDBNull(36) ? null : r.GetString(36),
                FileSizeBytes     = r.IsDBNull(37) ? null : r.GetInt64(37),
                TmdbId            = r.IsDBNull(38) ? null : r.GetString(38),
            };
        }

        // Load related data
        movie.Genres = GetMovieGenres(id);
        movie.Directors = GetMovieDirectors(id);
        movie.Writers = GetMovieWriters(id);
        movie.Actors = GetMovieActors(id);
        movie.Sets = GetMovieSets(id);
        movie.AllRatings = GetMovieRatings(id);
        return movie;
    }

    private List<string> GetMovieWriters(int id)
    {
        var list = new List<string>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT w.name FROM movie_writers mw JOIN writers w ON w.id=mw.writer_id WHERE mw.movie_id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    private List<(string Source, double Value, int? Votes)> GetMovieRatings(int id)
    {
        var list = new List<(string, double, int?)>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT source, value, votes FROM movie_ratings WHERE movie_id=@id ORDER BY value DESC";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetString(0), r.GetDouble(1), r.IsDBNull(2) ? (int?)null : r.GetInt32(2)));
        return list;
    }

    private List<string> GetMovieGenres(int id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT g.name FROM movie_genres mg JOIN genres g ON g.id=mg.genre_id WHERE mg.movie_id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        var list = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    private List<string> GetMovieDirectors(int id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT d.name FROM movie_directors md JOIN directors d ON d.id=md.director_id WHERE md.movie_id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        var list = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    private List<Actor> GetMovieActors(int id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT a.name, ma.role, a.thumb, ma.sort_order FROM movie_actors ma JOIN actors a ON a.id=ma.actor_id WHERE ma.movie_id=@id ORDER BY ma.sort_order LIMIT 12";
        cmd.Parameters.AddWithValue("@id", id);
        var list = new List<Actor>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Actor
            {
                Name = r.GetString(0),
                Role = r.IsDBNull(1) ? null : r.GetString(1),
                Thumb = r.IsDBNull(2) ? null : r.GetString(2),
                SortOrder = r.GetInt32(3),
            });
        }
        return list;
    }

    private List<string> GetMovieSets(int id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT s.name FROM movie_sets ms JOIN sets s ON s.id=ms.set_id WHERE ms.movie_id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        var list = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    // ── Mutations ────────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void ToggleFavorite(int id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE movies SET is_favorite = 1 - is_favorite WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void ToggleWatched(int id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE movies SET is_watched = 1 - is_watched WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    // ── Collections ──────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.Synchronized)]
    public List<Collection> GetCollections()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            SELECT s.id, s.name, COUNT(ms.movie_id) as cnt
            FROM sets s JOIN movie_sets ms ON ms.set_id=s.id
            GROUP BY s.id ORDER BY s.name";
        var list = new List<Collection>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Collection
            {
                Id = r.GetInt32(0),
                Name = r.GetString(1),
                MovieCount = (int)r.GetInt64(2),
            });
        }
        return list;
    }

    // ── Facets ───────────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.Synchronized)]
    public List<GenreFacet> GetTopGenres(int top = 8)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            SELECT g.name, COUNT(mg.movie_id) as cnt
            FROM genres g JOIN movie_genres mg ON mg.genre_id=g.id
            GROUP BY g.id ORDER BY cnt DESC LIMIT @top";
        cmd.Parameters.AddWithValue("@top", top);
        var list = new List<GenreFacet>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new GenreFacet { Name = r.GetString(0), Count = (int)r.GetInt64(1) });
        return list;
    }

    // ── Stats ────────────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.Synchronized)]
    public LibraryStats GetStats()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*), SUM(is_missing), COALESCE(SUM(runtime),0), AVG(CASE WHEN rating IS NOT NULL THEN rating END),
                   (SELECT COUNT(*) FROM drives)
            FROM movies";
        using var r = cmd.ExecuteReader();
        r.Read();
        return new LibraryStats
        {
            TotalMovies = (int)r.GetInt64(0),
            TotalMissing = r.IsDBNull(1) ? 0 : (int)r.GetInt64(1),
            TotalRuntime = r.GetInt64(2),
            AvgRating = r.IsDBNull(3) ? null : r.GetDouble(3),
            TotalDrives = (int)r.GetInt64(4),
        };
    }

    // ── Preferences ──────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.Synchronized)]
    public string? GetPref(string key)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM preferences WHERE key=@k";
        cmd.Parameters.AddWithValue("@k", key);
        return cmd.ExecuteScalar() as string;
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void SetPref(string key, string value)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO preferences (key, value) VALUES (@k, @v)";
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
    }

    // ── Image cache ──────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.Synchronized)]
    public string? GetCachedImagePath(string? relPath)
    {
        if (relPath == null) return null;
        var full = Path.Combine(_dataDir, relPath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(full) ? full : null;
    }

    // ── Scanner support ──────────────────────────────────────────────────────

    public SqliteConnection GetConnection() => _conn;

    /// <summary>
    /// Opens a fresh connection to the same DB file, fully independent of the
    /// shared <see cref="_conn"/>. Used by long-running write paths
    /// (scanner, copy export) so a transaction on their connection doesn't
    /// poison concurrent reads on the main connection — Microsoft.Data.Sqlite
    /// throws "Execute requires the command to have a transaction object …"
    /// when a connection has a pending local transaction and a command run
    /// against it doesn't carry that same transaction reference.
    /// WAL mode (set on _conn) is per-DB, so a second connection is safe.
    /// </summary>
    public SqliteConnection OpenNewConnection()
    {
        var dbPath = Path.Combine(_dataDir, "cinelibrary.db");
        var c = new SqliteConnection($"Data Source={dbPath}");
        c.Open();
        using var pr = c.CreateCommand();
        pr.CommandText = "PRAGMA foreign_keys=ON;";
        pr.ExecuteNonQuery();
        return c;
    }
    public string DataDir => _dataDir;

    // ── Statistics (v1.3) ───────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.Synchronized)]
    public List<(int decade, int count, double avgRating)> GetMoviesByDecade()
    {
        var result = new List<(int, int, double)>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            SELECT 
                (year / 10) * 10 as decade,
                COUNT(*) as count,
                AVG(CAST(rating AS FLOAT)) as avgRating
            FROM movies
            WHERE year IS NOT NULL AND is_missing = 0
            GROUP BY (year / 10) * 10
            ORDER BY decade DESC";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add((
                reader.GetInt32(0),
                (int)reader.GetInt64(1),
                reader.IsDBNull(2) ? 0 : reader.GetDouble(2)
            ));
        }
        return result;
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public List<GenreFacet> GetTopDirectors(int limit = 10)
    {
        var result = new List<GenreFacet>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            SELECT d.name, COUNT(md.movie_id) as count
            FROM directors d
            LEFT JOIN movie_directors md ON d.id = md.director_id
            GROUP BY d.id, d.name
            ORDER BY count DESC
            LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new GenreFacet
            {
                Name = reader.GetString(0),
                Count = (int)reader.GetInt64(1)
            });
        }
        return result;
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public List<GenreFacet> GetTopActors(int limit = 10)
    {
        var result = new List<GenreFacet>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            SELECT a.name, COUNT(DISTINCT ma.movie_id) as count
            FROM actors a
            LEFT JOIN movie_actors ma ON a.id = ma.actor_id
            GROUP BY a.id, a.name
            ORDER BY count DESC
            LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new GenreFacet
            {
                Name = reader.GetString(0),
                Count = (int)reader.GetInt64(1)
            });
        }
        return result;
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public (int watched, int total, double percent) GetWatchProgress()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                SUM(CASE WHEN is_watched = 1 THEN 1 ELSE 0 END) as watched,
                COUNT(*) as total
            FROM movies
            WHERE is_missing = 0";

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            int watched = reader.IsDBNull(0) ? 0 : (int)reader.GetInt64(0);
            int total = (int)reader.GetInt64(1);
            double percent = total > 0 ? (watched * 100.0 / total) : 0;
            return (watched, total, percent);
        }
        return (0, 0, 0);
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public double GetTotalRuntimeHours()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT SUM(CAST(runtime AS FLOAT)) FROM movies WHERE runtime IS NOT NULL AND is_missing = 0";

        var result = cmd.ExecuteScalar();
        if (result is not DBNull && result != null)
        {
            return (double)result / 60.0; // Convert minutes to hours
        }
        return 0;
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public int GetWatchlistCount()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM movies WHERE is_watchlist = 1 AND is_missing = 0";
        return (int)(long)cmd.ExecuteScalar()!;
    }

    // ── Browse pages (v2.1.0) ────────────────────────────────────────────────

    /// <summary>One entry in a browse-by-X grid: a category label, how
    /// many movies it has, and a representative fanart for the banner.</summary>
    public record BrowseEntry(string Key, string Label, int Count, string? SampleFanart, string? SamplePoster);

    public enum BrowseFacet { Genre, Decade, Rating, Studio }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public List<BrowseEntry> GetBrowseEntries(BrowseFacet facet)
    {
        return facet switch
        {
            BrowseFacet.Genre   => BrowseByGenre(),
            BrowseFacet.Decade  => BrowseByDecade(),
            BrowseFacet.Rating  => BrowseByRating(),
            BrowseFacet.Studio  => BrowseByStudio(),
            _ => new()
        };
    }

    private List<BrowseEntry> BrowseByGenre()
    {
        var list = new List<BrowseEntry>();
        using var cmd = _conn.CreateCommand();
        // For each genre: count + a representative fanart from a high-rated member.
        cmd.CommandText = @"
            SELECT g.name,
                   COUNT(DISTINCT m.id) c,
                   (SELECT m2.local_fanart FROM movies m2
                    JOIN movie_genres mg2 ON mg2.movie_id=m2.id
                    WHERE mg2.genre_id=g.id AND m2.local_fanart IS NOT NULL AND m2.is_missing=0
                    ORDER BY COALESCE(m2.rating,0) DESC LIMIT 1) AS fanart,
                   (SELECT m3.local_poster FROM movies m3
                    JOIN movie_genres mg3 ON mg3.movie_id=m3.id
                    WHERE mg3.genre_id=g.id AND m3.local_poster IS NOT NULL AND m3.is_missing=0
                    ORDER BY COALESCE(m3.rating,0) DESC LIMIT 1) AS poster
            FROM genres g
            JOIN movie_genres mg ON mg.genre_id=g.id
            JOIN movies m ON m.id=mg.movie_id AND m.is_missing=0
            GROUP BY g.id
            HAVING c > 0
            ORDER BY c DESC, g.name ASC";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new BrowseEntry(
                Key: r.GetString(0),
                Label: r.GetString(0),
                Count: r.GetInt32(1),
                SampleFanart: r.IsDBNull(2) ? null : r.GetString(2),
                SamplePoster: r.IsDBNull(3) ? null : r.GetString(3)));
        }
        return list;
    }

    private List<BrowseEntry> BrowseByDecade()
    {
        var list = new List<BrowseEntry>();
        using var cmd = _conn.CreateCommand();
        // Group by decade = (year/10)*10 — e.g. 2024 → 2020
        cmd.CommandText = @"
            SELECT (m.year/10)*10 AS decade,
                   COUNT(*) c,
                   (SELECT local_fanart FROM movies m2
                    WHERE (m2.year/10)*10 = (m.year/10)*10 AND m2.local_fanart IS NOT NULL AND m2.is_missing=0
                    ORDER BY COALESCE(m2.rating,0) DESC LIMIT 1) AS fanart,
                   (SELECT local_poster FROM movies m3
                    WHERE (m3.year/10)*10 = (m.year/10)*10 AND m3.local_poster IS NOT NULL AND m3.is_missing=0
                    ORDER BY COALESCE(m3.rating,0) DESC LIMIT 1) AS poster
            FROM movies m
            WHERE m.year IS NOT NULL AND m.is_missing=0
            GROUP BY decade
            ORDER BY decade DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var dec = r.GetInt32(0);
            list.Add(new BrowseEntry(
                Key: dec.ToString(),
                Label: $"{dec}s",
                Count: r.GetInt32(1),
                SampleFanart: r.IsDBNull(2) ? null : r.GetString(2),
                SamplePoster: r.IsDBNull(3) ? null : r.GetString(3)));
        }
        return list;
    }

    private List<BrowseEntry> BrowseByRating()
    {
        // Buckets: 9+, 8–9, 7–8, 6–7, 5–6, <5
        var bands = new (string key, string label, double lo, double hi)[]
        {
            ("9",  "9+ Stars",   9.0, 10.1),
            ("8",  "8–9 Stars",  8.0, 9.0),
            ("7",  "7–8 Stars",  7.0, 8.0),
            ("6",  "6–7 Stars",  6.0, 7.0),
            ("5",  "5–6 Stars",  5.0, 6.0),
            ("0",  "Under 5",    0.0, 5.0),
        };
        var list = new List<BrowseEntry>();
        foreach (var b in bands)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(*),
                       (SELECT local_fanart FROM movies WHERE rating >= @lo AND rating < @hi AND local_fanart IS NOT NULL AND is_missing=0
                        ORDER BY rating DESC LIMIT 1),
                       (SELECT local_poster FROM movies WHERE rating >= @lo AND rating < @hi AND local_poster IS NOT NULL AND is_missing=0
                        ORDER BY rating DESC LIMIT 1)
                FROM movies WHERE rating >= @lo AND rating < @hi AND is_missing=0";
            cmd.Parameters.AddWithValue("@lo", b.lo);
            cmd.Parameters.AddWithValue("@hi", b.hi);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) continue;
            var count = r.GetInt32(0);
            if (count == 0) continue;
            list.Add(new BrowseEntry(
                Key: b.key, Label: b.label, Count: count,
                SampleFanart: r.IsDBNull(1) ? null : r.GetString(1),
                SamplePoster: r.IsDBNull(2) ? null : r.GetString(2)));
        }
        return list;
    }

    private List<BrowseEntry> BrowseByStudio()
    {
        var list = new List<BrowseEntry>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            SELECT studio,
                   COUNT(*),
                   (SELECT local_fanart FROM movies m2 WHERE m2.studio=m.studio AND m2.local_fanart IS NOT NULL AND m2.is_missing=0
                    ORDER BY COALESCE(m2.rating,0) DESC LIMIT 1),
                   (SELECT local_poster FROM movies m3 WHERE m3.studio=m.studio AND m3.local_poster IS NOT NULL AND m3.is_missing=0
                    ORDER BY COALESCE(m3.rating,0) DESC LIMIT 1)
            FROM movies m
            WHERE m.studio IS NOT NULL AND m.studio <> '' AND m.is_missing=0
            GROUP BY m.studio
            HAVING COUNT(*) > 1
            ORDER BY COUNT(*) DESC, m.studio ASC
            LIMIT 60";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new BrowseEntry(
                Key: r.GetString(0),
                Label: r.GetString(0),
                Count: r.GetInt32(1),
                SampleFanart: r.IsDBNull(2) ? null : r.GetString(2),
                SamplePoster: r.IsDBNull(3) ? null : r.GetString(3)));
        }
        return list;
    }

    /// <summary>Collection grid entries — uses each set's highest-rated
    /// member's poster as the cover.</summary>
    public record CollectionEntry(int Id, string Name, int Count, string? CoverPoster);

    [MethodImpl(MethodImplOptions.Synchronized)]
    public List<CollectionEntry> GetCollectionGrid()
    {
        var list = new List<CollectionEntry>();
        using var cmd = _conn.CreateCommand();
        // Only real franchises: at least 2 online movies in the same set.
        // (A "set" with one member is just MediaElch noise — TMDB sometimes
        // assigns single films to a one-entry collection.)
        cmd.CommandText = @"
            SELECT s.id, s.name, COUNT(DISTINCT m2.id) c,
                   (SELECT local_poster FROM movies m
                    JOIN movie_sets ms2 ON ms2.movie_id=m.id
                    WHERE ms2.set_id=s.id AND m.local_poster IS NOT NULL AND m.is_missing=0
                    ORDER BY COALESCE(m.rating,0) DESC LIMIT 1) AS cover
            FROM sets s
            JOIN movie_sets ms ON ms.set_id=s.id
            JOIN movies m2 ON m2.id=ms.movie_id AND m2.is_missing=0
            GROUP BY s.id
            HAVING c >= 2
            ORDER BY s.name ASC";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new CollectionEntry(
                Id: r.GetInt32(0),
                Name: r.GetString(1),
                Count: r.GetInt32(2),
                CoverPoster: r.IsDBNull(3) ? null : r.GetString(3)));
        }
        return list;
    }

    // ── User Lists (v1.9.2) ──────────────────────────────────────────────────

    public record UserList(int Id, string Name, int MovieCount);

    [MethodImpl(MethodImplOptions.Synchronized)]
    public List<UserList> GetUserLists()
    {
        var list = new List<UserList>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"SELECT ul.id, ul.name, COUNT(ulm.movie_id)
                            FROM user_lists ul
                            LEFT JOIN user_list_movies ulm ON ulm.list_id=ul.id
                            GROUP BY ul.id
                            ORDER BY ul.sort_order, ul.name";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new UserList(r.GetInt32(0), r.GetString(1),
                                  r.IsDBNull(2) ? 0 : r.GetInt32(2)));
        return list;
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public int CreateUserList(string name)
    {
        var trimmed = (name ?? "").Trim();
        if (string.IsNullOrEmpty(trimmed)) throw new ArgumentException("List name is empty");
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT INTO user_lists (name) VALUES (@n); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@n", trimmed);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void RenameUserList(int listId, string newName)
    {
        var trimmed = (newName ?? "").Trim();
        if (string.IsNullOrEmpty(trimmed)) return;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE user_lists SET name=@n WHERE id=@id";
        cmd.Parameters.AddWithValue("@n", trimmed);
        cmd.Parameters.AddWithValue("@id", listId);
        cmd.ExecuteNonQuery();
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void DeleteUserList(int listId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM user_lists WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", listId);
        cmd.ExecuteNonQuery();
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void AddMovieToUserList(int listId, int movieId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO user_list_movies (list_id, movie_id) VALUES (@l, @m)";
        cmd.Parameters.AddWithValue("@l", listId);
        cmd.Parameters.AddWithValue("@m", movieId);
        cmd.ExecuteNonQuery();
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void RemoveMovieFromUserList(int listId, int movieId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM user_list_movies WHERE list_id=@l AND movie_id=@m";
        cmd.Parameters.AddWithValue("@l", listId);
        cmd.Parameters.AddWithValue("@m", movieId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Source-folder info for every movie in a user list. Used by the
    /// "Copy movies to folder" feature (#bucket-style export).
    /// </summary>
    public record MovieCopySource(int Id, string Title, string VolumeSerial, string FolderRelPath, int? Year);

    [MethodImpl(MethodImplOptions.Synchronized)]
    public List<MovieCopySource> GetMoviesForCopy(int listId)
    {
        var list = new List<MovieCopySource>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"SELECT m.id, m.title, m.volume_serial, m.folder_rel_path, m.year
                            FROM user_list_movies ulm
                            JOIN movies m ON m.id = ulm.movie_id
                            WHERE ulm.list_id = @id AND m.is_missing = 0
                            ORDER BY m.sort_title";
        cmd.Parameters.AddWithValue("@id", listId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new MovieCopySource(
                Id: r.GetInt32(0),
                Title: r.GetString(1),
                VolumeSerial: r.GetString(2),
                FolderRelPath: r.GetString(3),
                Year: r.IsDBNull(4) ? null : r.GetInt32(4)));
        }
        return list;
    }

    /// <summary>List IDs that already contain this movie. Used for menu state.</summary>
    [MethodImpl(MethodImplOptions.Synchronized)]
    public HashSet<int> GetUserListsForMovie(int movieId)
    {
        var set = new HashSet<int>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT list_id FROM user_list_movies WHERE movie_id=@m";
        cmd.Parameters.AddWithValue("@m", movieId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add(r.GetInt32(0));
        return set;
    }

    /// <summary>
    /// Save user note to the row. Empty/null clears it.
    /// Sidecar file write is handled by the caller (needs the drive letter).
    /// </summary>
    [MethodImpl(MethodImplOptions.Synchronized)]
    public void SetNote(int movieId, string? note)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE movies SET note=@n WHERE id=@id";
        cmd.Parameters.AddWithValue("@n", string.IsNullOrWhiteSpace(note) ? (object)DBNull.Value : note);
        cmd.Parameters.AddWithValue("@id", movieId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Stamps last_played_at = now for "Continue Watching" tracking.
    /// Called when the user hits Play in the detail dialog.
    /// </summary>
    [MethodImpl(MethodImplOptions.Synchronized)]
    public void MarkPlayed(int movieId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE movies SET last_played_at = strftime('%s','now') WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", movieId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Count of "in progress" movies — played at least once and not yet watched.
    /// Used to gate the sidebar Continue Watching shortcut visibility/badge.
    /// </summary>
    [MethodImpl(MethodImplOptions.Synchronized)]
    public int GetContinueWatchingCount()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"SELECT COUNT(*) FROM movies
                            WHERE last_played_at > 0 AND is_watched = 0 AND is_missing = 0";
        return (int)(long)cmd.ExecuteScalar()!;
    }

    /// <summary>
    /// Picks a random unwatched movie id, preferring online drives.
    /// Returns null if the library is empty or fully watched.
    /// </summary>
    [MethodImpl(MethodImplOptions.Synchronized)]
    public int? GetRandomUnwatchedId(Dictionary<string, string> connected)
    {
        if (connected.Count == 0)
        {
            using var any = _conn.CreateCommand();
            any.CommandText = "SELECT id FROM movies WHERE is_watched=0 AND is_missing=0 ORDER BY RANDOM() LIMIT 1";
            var v = any.ExecuteScalar();
            return v == null || v == DBNull.Value ? null : Convert.ToInt32(v);
        }

        // Restrict to drives that are currently online so the user can actually play it.
        var serials = string.Join(",", connected.Keys.Select(s => $"'{s.Replace("'", "''")}'"));
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $@"SELECT id FROM movies
                              WHERE is_watched=0 AND is_missing=0
                                AND volume_serial IN ({serials})
                              ORDER BY RANDOM() LIMIT 1";
        var val = cmd.ExecuteScalar();
        if (val == null || val == DBNull.Value)
        {
            // Fall back to any unwatched (drive offline) so the user still gets a pick.
            using var fb = _conn.CreateCommand();
            fb.CommandText = "SELECT id FROM movies WHERE is_watched=0 AND is_missing=0 ORDER BY RANDOM() LIMIT 1";
            val = fb.ExecuteScalar();
            if (val == null || val == DBNull.Value) return null;
        }
        return Convert.ToInt32(val);
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void SetWatchlist(int movieId, bool isWatchlist)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE movies SET is_watchlist = @val WHERE id = @id";
        cmd.Parameters.AddWithValue("@val", isWatchlist ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", movieId);
        cmd.ExecuteNonQuery();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void Dispose() => _conn.Dispose();
}
