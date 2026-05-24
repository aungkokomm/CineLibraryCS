using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace CineLibraryCS.Services;

/// <summary>
/// Reads and writes the per-movie personal-state sidecar file —
/// <c>cinelibrary-state.json</c> placed next to the movie's <c>.nfo</c>.
///
/// The sidecar travels with the movie folder, so removing a drive in
/// CineLibrary (which cascades and deletes the movie rows) and then
/// re-adding it later still gets your Watched / Favorite / Watchlist /
/// last-played / list-membership back — the next scan reads the JSON
/// and merges it into the freshly-inserted movie row.
///
/// Conflict rule (matches the existing note sidecar pattern):
///   • Empty DB row → import the sidecar.
///   • DB already has state → DB wins; sidecar is only used to add the
///     movie to lists named in the file (lists are additive).
///
/// All I/O is best-effort. Failures are silent — the sidecar is a
/// portable backup, never the source of truth at runtime.
/// </summary>
public static class MovieStateSidecar
{
    public const string FileName = "cinelibrary-state.json";

    /// <summary>JSON schema written to disk. Versioned for forward-compat.</summary>
    public class State
    {
        [JsonPropertyName("version")]       public int Version { get; set; } = 1;
        [JsonPropertyName("watched")]       public bool Watched { get; set; }
        [JsonPropertyName("favorite")]      public bool Favorite { get; set; }
        [JsonPropertyName("watchlist")]     public bool Watchlist { get; set; }
        [JsonPropertyName("lastPlayedUnix")]public long? LastPlayedUnix { get; set; }
        [JsonPropertyName("lists")]         public List<string> Lists { get; set; } = new();
        [JsonPropertyName("tags")]          public List<string> Tags { get; set; } = new();
        [JsonPropertyName("note")]          public string? Note { get; set; }
        [JsonPropertyName("updated")]       public string Updated { get; set; } =
            DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>True if the state carries anything worth recovering.</summary>
        public bool HasContent =>
            Watched || Favorite || Watchlist ||
            (LastPlayedUnix.HasValue && LastPlayedUnix.Value > 0) ||
            !string.IsNullOrWhiteSpace(Note) ||
            Lists.Count > 0 ||
            Tags.Count > 0;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Read ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Read the sidecar from a movie folder. Returns null when missing,
    /// unreadable, or malformed — never throws.
    /// </summary>
    public static State? TryRead(string movieFolderAbs)
    {
        try
        {
            var p = Path.Combine(movieFolderAbs, FileName);
            if (!File.Exists(p)) return null;
            var bytes = File.ReadAllBytes(p);
            return JsonSerializer.Deserialize<State>(bytes, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    // ── Write ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Best-effort write. Silent on every kind of failure (drive offline,
    /// read-only filesystem, permissions, locked file). Throttled by the
    /// caller — this method does no debouncing.
    /// </summary>
    public static void TryWrite(string movieFolderAbs, State state)
    {
        try
        {
            if (!Directory.Exists(movieFolderAbs)) return;
            state.Updated = DateTime.UtcNow.ToString("o",
                System.Globalization.CultureInfo.InvariantCulture);
            var path = Path.Combine(movieFolderAbs, FileName);
            // If state is empty, prefer to remove the file rather than
            // leave a stub with all-false fields — keeps the movie folder
            // tidy when the user un-marks everything.
            if (!state.HasContent)
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
                return;
            }
            var json = JsonSerializer.Serialize(state, JsonOpts);
            File.WriteAllText(path, json, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
            // Drive offline, read-only, locked, anti-virus, anything — skip.
        }
    }

    /// <summary>
    /// Compose a sidecar State from the DB row for a single movie. Returns
    /// null if the movie has no folder path or no online drive letter we
    /// can target.
    /// </summary>
    public static (string FolderAbs, State State)? Compose(
        DatabaseService db,
        int movieId,
        IReadOnlyDictionary<string, string> connectedDrives)
    {
        using var c = db.GetConnection().CreateCommand();
        c.CommandText = @"
            SELECT volume_serial, folder_rel_path, is_watched, is_favorite,
                   is_watchlist, last_played_at, note
              FROM movies WHERE id=@id";
        c.Parameters.AddWithValue("@id", movieId);
        using var r = c.ExecuteReader();
        if (!r.Read()) return null;
        var serial = r.GetString(0);
        var folderRel = r.IsDBNull(1) ? null : r.GetString(1);
        if (string.IsNullOrEmpty(folderRel)) return null;
        if (!connectedDrives.TryGetValue(serial, out var letter)) return null;
        var folderAbs = Path.Combine($"{letter}:\\", folderRel.Replace('/', '\\'));

        var state = new State
        {
            Watched          = r.GetInt32(2) == 1,
            Favorite         = r.GetInt32(3) == 1,
            Watchlist        = r.GetInt32(4) == 1,
            LastPlayedUnix   = r.IsDBNull(5) ? null : r.GetInt64(5),
            Note             = r.IsDBNull(6) ? null : r.GetString(6),
        };
        state.Lists = db.GetUserListNamesForMovie(movieId);
        state.Tags = db.GetTagNamesForMovie(movieId);
        return (folderAbs, state);
    }

    /// <summary>
    /// v2.9 — convenience wrapper used by callers that just want "sync
    /// whatever the DB currently has for this movie to its sidecar". Used
    /// by the detail dialog after tag mutations.
    /// </summary>
    public static void Sync(
        DatabaseService db,
        int movieId,
        IReadOnlyDictionary<string, string> connectedDrives)
    {
        var composed = Compose(db, movieId, connectedDrives);
        if (composed == null) return;
        TryWrite(composed.Value.FolderAbs, composed.Value.State);
    }

    /// <summary>
    /// Read-side import. Called from the scanner right after a movie row
    /// is inserted/updated. Merges sidecar values back into the DB row
    /// using the "DB wins if it already has data" rule. Lists are
    /// additive — never removed even if the sidecar omits them.
    /// </summary>
    public static void ImportIntoMovieRow(
        DatabaseService db,
        SqliteConnection conn,
        SqliteTransaction tx,
        int movieId,
        string folderAbs)
    {
        var s = TryRead(folderAbs);
        if (s == null) return;

        // Read current DB state — DB always wins where it already has a value.
        using var sel = conn.CreateCommand();
        sel.Transaction = tx;
        sel.CommandText = @"
            SELECT is_watched, is_favorite, is_watchlist, last_played_at, note
              FROM movies WHERE id=@id";
        sel.Parameters.AddWithValue("@id", movieId);
        bool dbWatched = false, dbFav = false, dbWatch = false;
        long dbLastPlayed = 0;
        string? dbNote = null;
        using (var r = sel.ExecuteReader())
        {
            if (!r.Read()) return;
            dbWatched     = r.GetInt32(0) == 1;
            dbFav         = r.GetInt32(1) == 1;
            dbWatch       = r.GetInt32(2) == 1;
            dbLastPlayed  = r.IsDBNull(3) ? 0L : r.GetInt64(3);
            dbNote        = r.IsDBNull(4) ? null : r.GetString(4);
        }

        var newWatched    = dbWatched    || s.Watched;
        var newFav        = dbFav        || s.Favorite;
        var newWatch      = dbWatch      || s.Watchlist;
        var newLastPlayed = Math.Max(dbLastPlayed, s.LastPlayedUnix ?? 0);
        var newNote = string.IsNullOrWhiteSpace(dbNote) ? s.Note : dbNote;

        using var upd = conn.CreateCommand();
        upd.Transaction = tx;
        upd.CommandText = @"
            UPDATE movies
               SET is_watched=@w, is_favorite=@f, is_watchlist=@wl,
                   last_played_at=@lp, note=COALESCE(@n, note)
             WHERE id=@id";
        upd.Parameters.AddWithValue("@w",  newWatched ? 1 : 0);
        upd.Parameters.AddWithValue("@f",  newFav     ? 1 : 0);
        upd.Parameters.AddWithValue("@wl", newWatch   ? 1 : 0);
        upd.Parameters.AddWithValue("@lp", newLastPlayed);
        upd.Parameters.AddWithValue("@n",  (object?)newNote ?? DBNull.Value);
        upd.Parameters.AddWithValue("@id", movieId);
        upd.ExecuteNonQuery();

        // Lists — additive. Each list name in the sidecar gets resolved
        // (or created) and the movie added to it.
        foreach (var listName in s.Lists.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(listName)) continue;
            EnsureMovieInList(conn, tx, movieId, listName);
        }

        // Tags — additive. Same pattern: ensure tag row, link movie.
        foreach (var tagName in s.Tags.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(tagName)) continue;
            EnsureMovieTag(conn, tx, movieId, tagName);
        }
    }

    private static void EnsureMovieTag(
        SqliteConnection conn, SqliteTransaction tx, int movieId, string tagName)
    {
        int tagId;
        using (var find = conn.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText = "SELECT id FROM tags WHERE name=@n";
            find.Parameters.AddWithValue("@n", tagName);
            var existing = find.ExecuteScalar();
            if (existing != null && existing != DBNull.Value)
            {
                tagId = Convert.ToInt32(existing);
            }
            else
            {
                using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = "INSERT INTO tags(name) VALUES(@n); SELECT last_insert_rowid();";
                ins.Parameters.AddWithValue("@n", tagName);
                tagId = Convert.ToInt32(ins.ExecuteScalar());
            }
        }
        using var link = conn.CreateCommand();
        link.Transaction = tx;
        link.CommandText = "INSERT OR IGNORE INTO movie_tags(movie_id, tag_id) VALUES(@m, @t)";
        link.Parameters.AddWithValue("@m", movieId);
        link.Parameters.AddWithValue("@t", tagId);
        link.ExecuteNonQuery();
    }

    private static void EnsureMovieInList(
        SqliteConnection conn, SqliteTransaction tx, int movieId, string listName)
    {
        int listId;
        using (var find = conn.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText = "SELECT id FROM user_lists WHERE name=@n";
            find.Parameters.AddWithValue("@n", listName);
            var existing = find.ExecuteScalar();
            if (existing != null && existing != DBNull.Value)
            {
                listId = Convert.ToInt32(existing);
            }
            else
            {
                using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = "INSERT INTO user_lists(name) VALUES(@n); SELECT last_insert_rowid();";
                ins.Parameters.AddWithValue("@n", listName);
                listId = Convert.ToInt32(ins.ExecuteScalar());
            }
        }
        using var link = conn.CreateCommand();
        link.Transaction = tx;
        link.CommandText = "INSERT OR IGNORE INTO user_list_movies(list_id, movie_id) VALUES(@l, @m)";
        link.Parameters.AddWithValue("@l", listId);
        link.Parameters.AddWithValue("@m", movieId);
        link.ExecuteNonQuery();
    }
}
