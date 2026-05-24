using System.Text.Json;
using System.Text.Json.Serialization;

namespace CineLibraryCS.Services;

/// <summary>
/// v2.9 — Backup / restore of all *personal* state in one JSON file.
///
/// "Personal" = everything the user creates: favorites, watchlist, watched
/// flags, last-played timestamps, notes, lists, tags, per-episode state,
/// and the watch-events audit log. Catalogued metadata (titles, plots,
/// posters) is NOT exported — it comes back from MediaElch's .nfo files
/// on the next scan.
///
/// Conflict policy on import (the "additive" rule):
///   • Lists and tags are ensure-created (created if missing, otherwise
///     re-used).
///   • Movie / show / episode rows are matched by their natural key
///     (volume_serial + folder_rel_path for movies/shows; show key +
///     SxxExx for episodes). Rows in the backup that don't have a
///     matching DB row are skipped — usually means the drive isn't
///     mounted right now.
///   • For matched rows, DB-true wins (OR semantics for booleans;
///     MAX for last_played_at; DB note kept if non-empty).
///   • Tags and list memberships are OR-merged (additive — never removed).
///   • watch_events are appended verbatim (history is append-only).
///
/// Result: importing the same file twice is a no-op; importing on top of
/// a different DB merges everything without ever dropping data.
/// </summary>
public static class BackupService
{
    private const int CurrentSchemaVersion = 1;

    // ── Schema classes ────────────────────────────────────────────────────

    public class Backup
    {
        [JsonPropertyName("schema")]     public int Schema { get; set; } = CurrentSchemaVersion;
        [JsonPropertyName("app")]        public string App { get; set; } = "CineLibrary";
        [JsonPropertyName("appVersion")] public string AppVersion { get; set; } = "";
        [JsonPropertyName("createdUtc")] public string CreatedUtc { get; set; } =
            DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);

        [JsonPropertyName("lists")]    public List<string> Lists { get; set; } = new();
        [JsonPropertyName("tags")]     public List<string> Tags { get; set; } = new();
        [JsonPropertyName("movies")]   public List<MovieEntry> Movies { get; set; } = new();
        [JsonPropertyName("shows")]    public List<ShowEntry> Shows { get; set; } = new();
        [JsonPropertyName("events")]   public List<WatchEvent> Events { get; set; } = new();
    }

    public class MovieEntry
    {
        // Natural key — survives DB rebuilds and id renumbering
        [JsonPropertyName("volumeSerial")]  public string VolumeSerial { get; set; } = "";
        [JsonPropertyName("folderRelPath")] public string FolderRelPath { get; set; } = "";
        // Display hint only — not used for matching
        [JsonPropertyName("title")]         public string Title { get; set; } = "";

        [JsonPropertyName("watched")]        public bool Watched { get; set; }
        [JsonPropertyName("favorite")]       public bool Favorite { get; set; }
        [JsonPropertyName("watchlist")]      public bool Watchlist { get; set; }
        [JsonPropertyName("lastPlayedUnix")] public long? LastPlayedUnix { get; set; }
        [JsonPropertyName("note")]           public string? Note { get; set; }
        [JsonPropertyName("lists")]          public List<string> Lists { get; set; } = new();
        [JsonPropertyName("tags")]           public List<string> Tags { get; set; } = new();
    }

    public class ShowEntry
    {
        [JsonPropertyName("volumeSerial")]  public string VolumeSerial { get; set; } = "";
        [JsonPropertyName("folderRelPath")] public string FolderRelPath { get; set; } = "";
        [JsonPropertyName("title")]         public string Title { get; set; } = "";

        [JsonPropertyName("favorite")]  public bool Favorite { get; set; }
        [JsonPropertyName("watchlist")] public bool Watchlist { get; set; }
        [JsonPropertyName("note")]      public string? Note { get; set; }
        [JsonPropertyName("lists")]     public List<string> Lists { get; set; } = new();
        [JsonPropertyName("tags")]      public List<string> Tags { get; set; } = new();
        [JsonPropertyName("episodes")]  public Dictionary<string, EpisodeEntry> Episodes { get; set; } = new();
    }

    public class EpisodeEntry
    {
        [JsonPropertyName("watched")]        public bool Watched { get; set; }
        [JsonPropertyName("lastPlayedUnix")] public long? LastPlayedUnix { get; set; }
        [JsonPropertyName("favorite")]       public bool Favorite { get; set; }
        [JsonPropertyName("note")]           public string? Note { get; set; }
    }

    public class WatchEvent
    {
        [JsonPropertyName("kind")]       public string Kind { get; set; } = "";   // 'movie' | 'episode'
        // For movies: same natural key as MovieEntry. For episodes: show-key + SxxExx.
        [JsonPropertyName("volumeSerial")]  public string VolumeSerial { get; set; } = "";
        [JsonPropertyName("folderRelPath")] public string FolderRelPath { get; set; } = "";
        [JsonPropertyName("code")]          public string? Code { get; set; }     // episodes only: "S01E03"
        [JsonPropertyName("watchedAt")]     public long WatchedAt { get; set; }
        [JsonPropertyName("action")]        public string Action { get; set; } = "";
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Export ────────────────────────────────────────────────────────────

    /// <summary>
    /// Compose a full backup of personal state from the current DB.
    /// Includes every row regardless of online/offline status — backup
    /// must survive even if drives aren't plugged in.
    /// </summary>
    public static Backup BuildSnapshot(DatabaseService db, string appVersion)
    {
        var b = new Backup { AppVersion = appVersion };
        var conn = db.GetConnection();

        // Lists + tags — names are the natural keys for re-creation on import.
        using (var c = conn.CreateCommand())
        {
            c.CommandText = "SELECT name FROM user_lists ORDER BY name COLLATE NOCASE";
            using var r = c.ExecuteReader();
            while (r.Read()) b.Lists.Add(r.GetString(0));
        }
        using (var c = conn.CreateCommand())
        {
            c.CommandText = "SELECT name FROM tags ORDER BY name COLLATE NOCASE";
            using var r = c.ExecuteReader();
            while (r.Read()) b.Tags.Add(r.GetString(0));
        }

        // Movies — only rows carrying any personal state. Empty rows are
        // not worth exporting and just bloat the file.
        var movieIndex = new Dictionary<int, MovieEntry>();
        using (var c = conn.CreateCommand())
        {
            c.CommandText = @"
                SELECT id, volume_serial, folder_rel_path, title,
                       is_watched, is_favorite, is_watchlist, last_played_at, note
                  FROM movies
                 WHERE is_watched=1 OR is_favorite=1 OR is_watchlist=1
                    OR last_played_at>0
                    OR (note IS NOT NULL AND TRIM(note) != '')
                    OR EXISTS (SELECT 1 FROM movie_tags mt WHERE mt.movie_id = movies.id)
                    OR EXISTS (SELECT 1 FROM user_list_movies ulm WHERE ulm.movie_id = movies.id)";
            using var r = c.ExecuteReader();
            while (r.Read())
            {
                var id = r.GetInt32(0);
                var folderRel = r.IsDBNull(2) ? "" : r.GetString(2);
                if (string.IsNullOrEmpty(folderRel)) continue;
                var entry = new MovieEntry
                {
                    VolumeSerial = r.GetString(1),
                    FolderRelPath = folderRel,
                    Title = r.IsDBNull(3) ? "" : r.GetString(3),
                    Watched = r.GetInt32(4) == 1,
                    Favorite = r.GetInt32(5) == 1,
                    Watchlist = r.GetInt32(6) == 1,
                    LastPlayedUnix = r.IsDBNull(7) ? null : r.GetInt64(7),
                    Note = r.IsDBNull(8) ? null : r.GetString(8),
                };
                b.Movies.Add(entry);
                movieIndex[id] = entry;
            }
        }
        // Movie list memberships
        foreach (var (movieId, entry) in movieIndex)
            entry.Lists = db.GetUserListNamesForMovie(movieId);
        // Movie tags
        foreach (var (movieId, entry) in movieIndex)
            entry.Tags = db.GetTagNamesForMovie(movieId);

        // Shows + their episodes
        var showIndex = new Dictionary<int, ShowEntry>();
        using (var c = conn.CreateCommand())
        {
            c.CommandText = @"
                SELECT id, volume_serial, folder_rel_path, title,
                       is_favorite, is_watchlist, note
                  FROM tv_shows";
            using var r = c.ExecuteReader();
            while (r.Read())
            {
                var id = r.GetInt32(0);
                var folderRel = r.IsDBNull(2) ? "" : r.GetString(2);
                if (string.IsNullOrEmpty(folderRel)) continue;
                var entry = new ShowEntry
                {
                    VolumeSerial = r.GetString(1),
                    FolderRelPath = folderRel,
                    Title = r.IsDBNull(3) ? "" : r.GetString(3),
                    Favorite = r.GetInt32(4) == 1,
                    Watchlist = r.GetInt32(5) == 1,
                    Note = r.IsDBNull(6) ? null : r.GetString(6),
                };
                showIndex[id] = entry;
            }
        }
        // Show episodes (only rows with personal state)
        using (var c = conn.CreateCommand())
        {
            c.CommandText = @"
                SELECT show_id, season, episode, is_watched, last_played_at, is_favorite, note
                  FROM tv_episodes
                 WHERE is_watched=1 OR last_played_at>0
                    OR is_favorite=1 OR (note IS NOT NULL AND TRIM(note) != '')";
            using var r = c.ExecuteReader();
            while (r.Read())
            {
                if (!showIndex.TryGetValue(r.GetInt32(0), out var show)) continue;
                var code = $"S{r.GetInt32(1):D2}E{r.GetInt32(2):D2}";
                show.Episodes[code] = new EpisodeEntry
                {
                    Watched = r.GetInt32(3) == 1,
                    LastPlayedUnix = r.IsDBNull(4) ? null : (r.GetInt64(4) > 0 ? r.GetInt64(4) : null),
                    Favorite = !r.IsDBNull(5) && r.GetInt32(5) == 1,
                    Note = r.IsDBNull(6) ? null : r.GetString(6),
                };
            }
        }
        // Show list memberships + tags
        foreach (var (showId, entry) in showIndex)
        {
            entry.Lists = db.GetUserListNamesForShow(showId);
            entry.Tags = db.GetTagNamesForShow(showId);
        }
        // Only export shows that carry *something* — fully blank shows
        // (no fav/wl/note/lists/tags/episodes) wouldn't bring back anything.
        foreach (var entry in showIndex.Values)
        {
            if (entry.Favorite || entry.Watchlist || !string.IsNullOrWhiteSpace(entry.Note)
                || entry.Lists.Count > 0 || entry.Tags.Count > 0 || entry.Episodes.Count > 0)
                b.Shows.Add(entry);
        }

        // Watch events — append-only log. Resolve item_id back to natural key.
        using (var c = conn.CreateCommand())
        {
            c.CommandText = @"
                SELECT e.item_kind, e.watched_at, e.action,
                       m.volume_serial, m.folder_rel_path,
                       s.volume_serial, s.folder_rel_path, ep.season, ep.episode
                  FROM watch_events e
             LEFT JOIN movies m
                       ON e.item_kind = 'movie' AND m.id = e.item_id
             LEFT JOIN tv_episodes ep
                       ON e.item_kind = 'episode' AND ep.id = e.item_id
             LEFT JOIN tv_shows s
                       ON e.item_kind = 'episode' AND s.id = ep.show_id
                 ORDER BY e.watched_at";
            using var r = c.ExecuteReader();
            while (r.Read())
            {
                var kind = r.GetString(0);
                var ev = new WatchEvent
                {
                    Kind = kind,
                    WatchedAt = r.GetInt64(1),
                    Action = r.GetString(2),
                };
                if (kind == "movie")
                {
                    if (r.IsDBNull(3) || r.IsDBNull(4)) continue;
                    ev.VolumeSerial = r.GetString(3);
                    ev.FolderRelPath = r.GetString(4);
                }
                else // episode
                {
                    if (r.IsDBNull(5) || r.IsDBNull(6) || r.IsDBNull(7) || r.IsDBNull(8)) continue;
                    ev.VolumeSerial = r.GetString(5);
                    ev.FolderRelPath = r.GetString(6);
                    ev.Code = $"S{r.GetInt32(7):D2}E{r.GetInt32(8):D2}";
                }
                b.Events.Add(ev);
            }
        }

        return b;
    }

    public static void WriteToFile(Backup snapshot, string path)
    {
        var json = JsonSerializer.Serialize(snapshot, JsonOpts);
        File.WriteAllText(path, json,
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static Backup? ReadFromFile(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<Backup>(File.ReadAllBytes(path), JsonOpts);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"BackupService.ReadFromFile failed: {ex.Message}");
            return null;
        }
    }

    // ── Import ────────────────────────────────────────────────────────────

    public record ImportResult(
        int MoviesMerged,
        int MoviesSkipped,
        int ShowsMerged,
        int ShowsSkipped,
        int EpisodesMerged,
        int ListsCreated,
        int TagsCreated,
        int EventsAppended);

    /// <summary>
    /// Merge a backup into the current DB using the "additive — DB wins"
    /// rule. Caller wraps in a single transaction so a partial import
    /// never leaves the DB inconsistent.
    /// </summary>
    public static ImportResult Import(DatabaseService db, Backup b)
    {
        var conn = db.GetConnection();
        int moviesMerged = 0, moviesSkipped = 0;
        int showsMerged = 0, showsSkipped = 0, epsMerged = 0;
        int listsCreated = 0, tagsCreated = 0, eventsAppended = 0;

        using var tx = conn.BeginTransaction();
        try
        {
            // Lists — ensure-create. Track which ones we had to create.
            var listIdByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in b.Lists.Concat(
                         b.Movies.SelectMany(m => m.Lists))
                     .Concat(b.Shows.SelectMany(s => s.Lists))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                int id; bool created;
                (id, created) = EnsureList(conn, tx, name);
                listIdByName[name] = id;
                if (created) listsCreated++;
            }

            // Tags — same.
            var tagIdByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in b.Tags.Concat(
                         b.Movies.SelectMany(m => m.Tags))
                     .Concat(b.Shows.SelectMany(s => s.Tags))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                int id; bool created;
                (id, created) = EnsureTag(conn, tx, name);
                tagIdByName[name] = id;
                if (created) tagsCreated++;
            }

            // Movies — natural-key lookup, additive merge.
            foreach (var entry in b.Movies)
            {
                var movieId = FindMovieId(conn, tx, entry.VolumeSerial, entry.FolderRelPath);
                if (movieId == null) { moviesSkipped++; continue; }
                MergeMovieEntry(conn, tx, movieId.Value, entry, listIdByName, tagIdByName);
                moviesMerged++;
            }

            // Shows — natural-key lookup, additive merge of show + episodes.
            foreach (var entry in b.Shows)
            {
                var showId = FindShowId(conn, tx, entry.VolumeSerial, entry.FolderRelPath);
                if (showId == null) { showsSkipped++; continue; }
                MergeShowEntry(conn, tx, showId.Value, entry, listIdByName, tagIdByName, ref epsMerged);
                showsMerged++;
            }

            // Watch events — append-only. We don't dedupe on (kind,item,watched_at,action)
            // because two genuine events can share a timestamp at second resolution.
            foreach (var e in b.Events)
            {
                int? itemId = null;
                if (e.Kind == "movie")
                    itemId = FindMovieId(conn, tx, e.VolumeSerial, e.FolderRelPath);
                else if (e.Kind == "episode" && !string.IsNullOrEmpty(e.Code))
                {
                    var showId = FindShowId(conn, tx, e.VolumeSerial, e.FolderRelPath);
                    if (showId != null && TryParseCode(e.Code, out var s, out var ep))
                        itemId = FindEpisodeId(conn, tx, showId.Value, s, ep);
                }
                if (itemId == null) continue;
                using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = "INSERT INTO watch_events(item_kind, item_id, watched_at, action) VALUES(@k,@i,@w,@a)";
                ins.Parameters.AddWithValue("@k", e.Kind);
                ins.Parameters.AddWithValue("@i", itemId.Value);
                ins.Parameters.AddWithValue("@w", e.WatchedAt);
                ins.Parameters.AddWithValue("@a", e.Action);
                ins.ExecuteNonQuery();
                eventsAppended++;
            }

            tx.Commit();
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }

        return new ImportResult(moviesMerged, moviesSkipped, showsMerged, showsSkipped,
            epsMerged, listsCreated, tagsCreated, eventsAppended);
    }

    // ── Import helpers ────────────────────────────────────────────────────

    private static (int Id, bool Created) EnsureList(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        Microsoft.Data.Sqlite.SqliteTransaction tx, string name)
    {
        using (var find = conn.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText = "SELECT id FROM user_lists WHERE name=@n";
            find.Parameters.AddWithValue("@n", name);
            var v = find.ExecuteScalar();
            if (v != null && v != DBNull.Value) return (Convert.ToInt32(v), false);
        }
        using var ins = conn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = "INSERT INTO user_lists(name) VALUES(@n); SELECT last_insert_rowid();";
        ins.Parameters.AddWithValue("@n", name);
        return (Convert.ToInt32(ins.ExecuteScalar()), true);
    }

    private static (int Id, bool Created) EnsureTag(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        Microsoft.Data.Sqlite.SqliteTransaction tx, string name)
    {
        using (var find = conn.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText = "SELECT id FROM tags WHERE name=@n";
            find.Parameters.AddWithValue("@n", name);
            var v = find.ExecuteScalar();
            if (v != null && v != DBNull.Value) return (Convert.ToInt32(v), false);
        }
        using var ins = conn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = "INSERT INTO tags(name) VALUES(@n); SELECT last_insert_rowid();";
        ins.Parameters.AddWithValue("@n", name);
        return (Convert.ToInt32(ins.ExecuteScalar()), true);
    }

    private static int? FindMovieId(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        Microsoft.Data.Sqlite.SqliteTransaction tx, string serial, string folderRel)
    {
        using var c = conn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = "SELECT id FROM movies WHERE volume_serial=@s AND folder_rel_path=@f LIMIT 1";
        c.Parameters.AddWithValue("@s", serial);
        c.Parameters.AddWithValue("@f", folderRel);
        var v = c.ExecuteScalar();
        return v == null || v == DBNull.Value ? null : Convert.ToInt32(v);
    }

    private static int? FindShowId(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        Microsoft.Data.Sqlite.SqliteTransaction tx, string serial, string folderRel)
    {
        using var c = conn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = "SELECT id FROM tv_shows WHERE volume_serial=@s AND folder_rel_path=@f LIMIT 1";
        c.Parameters.AddWithValue("@s", serial);
        c.Parameters.AddWithValue("@f", folderRel);
        var v = c.ExecuteScalar();
        return v == null || v == DBNull.Value ? null : Convert.ToInt32(v);
    }

    private static int? FindEpisodeId(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        Microsoft.Data.Sqlite.SqliteTransaction tx, int showId, int season, int episode)
    {
        using var c = conn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = "SELECT id FROM tv_episodes WHERE show_id=@s AND season=@se AND episode=@ep LIMIT 1";
        c.Parameters.AddWithValue("@s", showId);
        c.Parameters.AddWithValue("@se", season);
        c.Parameters.AddWithValue("@ep", episode);
        var v = c.ExecuteScalar();
        return v == null || v == DBNull.Value ? null : Convert.ToInt32(v);
    }

    private static bool TryParseCode(string code, out int season, out int episode)
    {
        var m = System.Text.RegularExpressions.Regex.Match(code, @"[Ss](\d+)[Ee](\d+)");
        if (!m.Success) { season = 0; episode = 0; return false; }
        season = int.Parse(m.Groups[1].Value);
        episode = int.Parse(m.Groups[2].Value);
        return true;
    }

    private static void MergeMovieEntry(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        Microsoft.Data.Sqlite.SqliteTransaction tx, int movieId, MovieEntry e,
        Dictionary<string, int> listIds, Dictionary<string, int> tagIds)
    {
        // Read current DB state, OR-merge booleans, MAX last_played, keep DB note if non-empty.
        bool dbW = false, dbF = false, dbWl = false;
        long dbLp = 0;
        string? dbNote = null;
        using (var sel = conn.CreateCommand())
        {
            sel.Transaction = tx;
            sel.CommandText = "SELECT is_watched, is_favorite, is_watchlist, last_played_at, note FROM movies WHERE id=@id";
            sel.Parameters.AddWithValue("@id", movieId);
            using var r = sel.ExecuteReader();
            if (r.Read())
            {
                dbW = r.GetInt32(0) == 1; dbF = r.GetInt32(1) == 1; dbWl = r.GetInt32(2) == 1;
                dbLp = r.IsDBNull(3) ? 0 : r.GetInt64(3);
                dbNote = r.IsDBNull(4) ? null : r.GetString(4);
            }
        }
        var newLp = Math.Max(dbLp, e.LastPlayedUnix ?? 0);
        var newNote = string.IsNullOrWhiteSpace(dbNote) ? e.Note : dbNote;

        using var upd = conn.CreateCommand();
        upd.Transaction = tx;
        upd.CommandText = @"UPDATE movies SET is_watched=@w, is_favorite=@f, is_watchlist=@wl,
                                last_played_at=@lp, note=COALESCE(@n, note) WHERE id=@id";
        upd.Parameters.AddWithValue("@w",  (dbW  || e.Watched)   ? 1 : 0);
        upd.Parameters.AddWithValue("@f",  (dbF  || e.Favorite)  ? 1 : 0);
        upd.Parameters.AddWithValue("@wl", (dbWl || e.Watchlist) ? 1 : 0);
        upd.Parameters.AddWithValue("@lp", newLp);
        upd.Parameters.AddWithValue("@n",  (object?)newNote ?? DBNull.Value);
        upd.Parameters.AddWithValue("@id", movieId);
        upd.ExecuteNonQuery();

        foreach (var listName in e.Lists.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!listIds.TryGetValue(listName, out var listId)) continue;
            using var link = conn.CreateCommand();
            link.Transaction = tx;
            link.CommandText = "INSERT OR IGNORE INTO user_list_movies(list_id, movie_id) VALUES(@l,@m)";
            link.Parameters.AddWithValue("@l", listId);
            link.Parameters.AddWithValue("@m", movieId);
            link.ExecuteNonQuery();
        }
        foreach (var tagName in e.Tags.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!tagIds.TryGetValue(tagName, out var tagId)) continue;
            using var link = conn.CreateCommand();
            link.Transaction = tx;
            link.CommandText = "INSERT OR IGNORE INTO movie_tags(movie_id, tag_id) VALUES(@m,@t)";
            link.Parameters.AddWithValue("@m", movieId);
            link.Parameters.AddWithValue("@t", tagId);
            link.ExecuteNonQuery();
        }
    }

    private static void MergeShowEntry(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        Microsoft.Data.Sqlite.SqliteTransaction tx, int showId, ShowEntry e,
        Dictionary<string, int> listIds, Dictionary<string, int> tagIds,
        ref int episodesMerged)
    {
        bool dbF = false, dbWl = false; string? dbNote = null;
        using (var sel = conn.CreateCommand())
        {
            sel.Transaction = tx;
            sel.CommandText = "SELECT is_favorite, is_watchlist, note FROM tv_shows WHERE id=@id";
            sel.Parameters.AddWithValue("@id", showId);
            using var r = sel.ExecuteReader();
            if (r.Read())
            {
                dbF = r.GetInt32(0) == 1; dbWl = r.GetInt32(1) == 1;
                dbNote = r.IsDBNull(2) ? null : r.GetString(2);
            }
        }
        using (var upd = conn.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = @"UPDATE tv_shows SET is_favorite=@f, is_watchlist=@w, note=COALESCE(@n, note) WHERE id=@id";
            upd.Parameters.AddWithValue("@f", (dbF || e.Favorite) ? 1 : 0);
            upd.Parameters.AddWithValue("@w", (dbWl || e.Watchlist) ? 1 : 0);
            upd.Parameters.AddWithValue("@n", string.IsNullOrWhiteSpace(dbNote)
                ? (object?)e.Note ?? DBNull.Value : DBNull.Value);
            upd.Parameters.AddWithValue("@id", showId);
            upd.ExecuteNonQuery();
        }

        foreach (var listName in e.Lists.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!listIds.TryGetValue(listName, out var listId)) continue;
            using var link = conn.CreateCommand();
            link.Transaction = tx;
            link.CommandText = "INSERT OR IGNORE INTO user_list_shows(list_id, show_id) VALUES(@l,@s)";
            link.Parameters.AddWithValue("@l", listId);
            link.Parameters.AddWithValue("@s", showId);
            link.ExecuteNonQuery();
        }
        foreach (var tagName in e.Tags.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!tagIds.TryGetValue(tagName, out var tagId)) continue;
            using var link = conn.CreateCommand();
            link.Transaction = tx;
            link.CommandText = "INSERT OR IGNORE INTO tv_show_tags(show_id, tag_id) VALUES(@s,@t)";
            link.Parameters.AddWithValue("@s", showId);
            link.Parameters.AddWithValue("@t", tagId);
            link.ExecuteNonQuery();
        }

        foreach (var (code, est) in e.Episodes)
        {
            if (!TryParseCode(code, out var season, out var ep)) continue;
            using var upd = conn.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = @"UPDATE tv_episodes SET
                                    is_watched = CASE WHEN is_watched=1 OR @w=1 THEN 1 ELSE 0 END,
                                    last_played_at = MAX(last_played_at, @lp),
                                    is_favorite = CASE WHEN is_favorite=1 OR @f=1 THEN 1 ELSE 0 END,
                                    note = CASE WHEN note IS NULL OR TRIM(note)='' THEN COALESCE(@n, note) ELSE note END
                                 WHERE show_id=@id AND season=@se AND episode=@ep";
            upd.Parameters.AddWithValue("@w",  est.Watched ? 1 : 0);
            upd.Parameters.AddWithValue("@lp", est.LastPlayedUnix ?? 0);
            upd.Parameters.AddWithValue("@f",  est.Favorite ? 1 : 0);
            upd.Parameters.AddWithValue("@n",  (object?)est.Note ?? DBNull.Value);
            upd.Parameters.AddWithValue("@id", showId);
            upd.Parameters.AddWithValue("@se", season);
            upd.Parameters.AddWithValue("@ep", ep);
            if (upd.ExecuteNonQuery() > 0) episodesMerged++;
        }
    }
}
