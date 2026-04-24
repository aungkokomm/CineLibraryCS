using Microsoft.Data.Sqlite;
using CineLibraryCS.Models;
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

        // Create indexes for performance
        CreateIndexes();
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
        ");
    }

    // ── Drives ───────────────────────────────────────────────────────────────

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

    /// <summary>Purge movies flagged is_missing=1 for this drive (plus their cached artwork). Returns count deleted.</summary>
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

    public void UpdateDriveLastSeen(string serial, string letter)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE drives SET last_seen_letter=@l, last_connected_at=strftime('%s','now') WHERE volume_serial=@s";
        cmd.Parameters.AddWithValue("@l", letter);
        cmd.Parameters.AddWithValue("@s", serial);
        cmd.ExecuteNonQuery();
    }

    public void RenameDrive(string serial, string newLabel)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE drives SET label=@l WHERE volume_serial=@s";
        cmd.Parameters.AddWithValue("@l", newLabel);
        cmd.Parameters.AddWithValue("@s", serial);
        cmd.ExecuteNonQuery();
    }

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
        int? CollectionId = null,
        string WatchedFilter = "all",   // all | watched | unwatched
        bool FavoritesOnly = false,
        bool IsWatchlistOnly = false,
        int Limit = 60,
        int Offset = 0
    );

    public List<MovieListItem> GetMovies(ListOptions opts, Dictionary<string, string> connected)
    {
        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(opts.Search))
            where.Add(@"(m.title LIKE @q OR m.original_title LIKE @q OR m.plot LIKE @q
                OR EXISTS (SELECT 1 FROM movie_actors ma JOIN actors a ON a.id=ma.actor_id WHERE ma.movie_id=m.id AND a.name LIKE @q)
                OR EXISTS (SELECT 1 FROM movie_directors md JOIN directors d ON d.id=md.director_id WHERE md.movie_id=m.id AND d.name LIKE @q))");
        if (opts.DriveSerial != null) where.Add("m.volume_serial=@serial");
        if (opts.Genre != null) where.Add("EXISTS (SELECT 1 FROM movie_genres mg JOIN genres g ON g.id=mg.genre_id WHERE mg.movie_id=m.id AND g.name=@genre)");
        if (opts.Actor != null) where.Add("EXISTS (SELECT 1 FROM movie_actors ma JOIN actors a ON a.id=ma.actor_id WHERE ma.movie_id=m.id AND LOWER(a.name)=LOWER(@actor))");
        if (opts.Director != null) where.Add("EXISTS (SELECT 1 FROM movie_directors md JOIN directors d ON d.id=md.director_id WHERE md.movie_id=m.id AND LOWER(d.name)=LOWER(@director))");
        if (opts.CollectionId != null) where.Add("EXISTS (SELECT 1 FROM movie_sets ms WHERE ms.movie_id=m.id AND ms.set_id=@colId)");
        if (opts.WatchedFilter == "watched") where.Add("m.is_watched=1");
        else if (opts.WatchedFilter == "unwatched") where.Add("m.is_watched=0");
        if (opts.FavoritesOnly) where.Add("m.is_favorite=1");
        if (opts.IsWatchlistOnly) where.Add("m.is_watchlist=1");

        var sortCol = opts.SortKey switch
        {
            "year" => "m.year",
            "rating" => "m.rating",
            "runtime" => "m.runtime",
            "date_added" => "m.date_added",
            _ => "m.sort_title"
        };
        var sortDir = opts.SortDir == "desc" ? "DESC" : "ASC";
        var nullsLast = sortDir == "ASC" ? "NULLS LAST" : "NULLS FIRST";

        var whereStr = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT m.id, m.title, m.year, m.rating, m.runtime, m.local_poster,
                   m.is_missing, m.is_favorite, m.is_watched, m.volume_serial, d.label,
                   (SELECT GROUP_CONCAT(g.name, ', ')
                    FROM movie_genres mg JOIN genres g ON g.id=mg.genre_id
                    WHERE mg.movie_id=m.id) as genres_csv
            FROM movies m
            LEFT JOIN drives d ON d.volume_serial=m.volume_serial
            {whereStr}
            ORDER BY {sortCol} {sortDir} {nullsLast}, m.sort_title ASC
            LIMIT @lim OFFSET @off";

        if (!string.IsNullOrWhiteSpace(opts.Search))
            cmd.Parameters.AddWithValue("@q", $"%{opts.Search}%");
        if (opts.DriveSerial != null) cmd.Parameters.AddWithValue("@serial", opts.DriveSerial);
        if (opts.Genre != null) cmd.Parameters.AddWithValue("@genre", opts.Genre);
        if (opts.Actor != null) cmd.Parameters.AddWithValue("@actor", opts.Actor);
        if (opts.Director != null) cmd.Parameters.AddWithValue("@director", opts.Director);
        if (opts.CollectionId != null) cmd.Parameters.AddWithValue("@colId", opts.CollectionId);
        cmd.Parameters.AddWithValue("@lim", opts.Limit);
        cmd.Parameters.AddWithValue("@off", opts.Offset);

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
                IsOnline = connected.ContainsKey(serial),
            });
        }
        return list;
    }

    // ── Movie Detail ─────────────────────────────────────────────────────────

    public MovieDetail? GetMovieDetail(int id, Dictionary<string, string> connected)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            SELECT m.id, m.title, m.original_title, m.year, m.rating, m.runtime,
                   m.plot, m.tagline, m.mpaa, m.imdb_id, m.premiered, m.studio, m.country,
                   m.local_poster, m.local_fanart, m.is_missing, m.is_favorite, m.is_watched,
                   m.is_watchlist, m.volume_serial, d.label, m.folder_rel_path, m.video_file_rel_path,
                   m.outline
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
            };
        }

        // Load related data
        movie.Genres = GetMovieGenres(id);
        movie.Directors = GetMovieDirectors(id);
        movie.Actors = GetMovieActors(id);
        movie.Sets = GetMovieSets(id);
        return movie;
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

    public void ToggleFavorite(int id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE movies SET is_favorite = 1 - is_favorite WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void ToggleWatched(int id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE movies SET is_watched = 1 - is_watched WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    // ── Collections ──────────────────────────────────────────────────────────

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

    public string? GetPref(string key)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM preferences WHERE key=@k";
        cmd.Parameters.AddWithValue("@k", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetPref(string key, string value)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO preferences (key, value) VALUES (@k, @v)";
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
    }

    // ── Image cache ──────────────────────────────────────────────────────────

    public string? GetCachedImagePath(string? relPath)
    {
        if (relPath == null) return null;
        var full = Path.Combine(_dataDir, relPath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(full) ? full : null;
    }

    // ── Scanner support ──────────────────────────────────────────────────────

    public SqliteConnection GetConnection() => _conn;
    public string DataDir => _dataDir;

    // ── Statistics (v1.3) ───────────────────────────────────────────────────

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

    public int GetWatchlistCount()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM movies WHERE is_watchlist = 1 AND is_missing = 0";
        return (int)(long)cmd.ExecuteScalar()!;
    }

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

    public void Dispose() => _conn.Dispose();
}
