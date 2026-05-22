using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace CineLibraryCS.Services;

/// <summary>
/// v2.8 — per-show personal-state sidecar. Lives as
/// <c>cinelibrary-state.json</c> in the show folder (next to tvshow.nfo),
/// the same filename the movie sidecar uses — a folder is only ever one
/// or the other, so there's no clash. Holds show-level favorite /
/// watchlist / note plus a per-episode watched map keyed "S01E03".
///
/// Same survival guarantee as movies: remove a drive, re-add it, and the
/// next scan reads this file and restores everything.
/// </summary>
public static class TvStateSidecar
{
    public const string FileName = "cinelibrary-state.json";

    public class EpisodeState
    {
        [JsonPropertyName("watched")]        public bool Watched { get; set; }
        [JsonPropertyName("lastPlayedUnix")] public long? LastPlayedUnix { get; set; }
    }

    public class State
    {
        [JsonPropertyName("version")]   public int Version { get; set; } = 1;
        [JsonPropertyName("kind")]      public string Kind { get; set; } = "tvshow";
        [JsonPropertyName("favorite")]  public bool Favorite { get; set; }
        [JsonPropertyName("watchlist")] public bool Watchlist { get; set; }
        [JsonPropertyName("note")]      public string? Note { get; set; }
        [JsonPropertyName("lists")]     public List<string> Lists { get; set; } = new();
        [JsonPropertyName("episodes")]  public Dictionary<string, EpisodeState> Episodes { get; set; } = new();
        [JsonPropertyName("updated")]   public string Updated { get; set; } =
            DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);

        public bool HasContent =>
            Favorite || Watchlist || !string.IsNullOrWhiteSpace(Note) || Lists.Count > 0 ||
            Episodes.Values.Any(e => e.Watched || (e.LastPlayedUnix ?? 0) > 0);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string Key(int season, int episode) => $"S{season:D2}E{episode:D2}";

    public static State? TryRead(string showFolderAbs)
    {
        try
        {
            var p = Path.Combine(showFolderAbs, FileName);
            if (!File.Exists(p)) return null;
            var s = JsonSerializer.Deserialize<State>(File.ReadAllBytes(p), JsonOpts);
            // Only treat as TV state if it actually is (the movie sidecar
            // has no "episodes"/kind=tvshow). Guards against a folder that
            // somehow has a movie-shaped file.
            return s;
        }
        catch { return null; }
    }

    public static void TryWrite(string showFolderAbs, State state)
    {
        try
        {
            if (!Directory.Exists(showFolderAbs)) return;
            state.Updated = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
            var path = Path.Combine(showFolderAbs, FileName);
            if (!state.HasContent)
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
                return;
            }
            var json = JsonSerializer.Serialize(state, JsonOpts);
            File.WriteAllText(path, json, new System.Text.UTF8Encoding(false));
        }
        catch { }
    }

    /// <summary>
    /// Build a State from the DB for a show. Returns null if the show has
    /// no folder or its drive isn't connected (can't write to it anyway).
    /// </summary>
    public static (string FolderAbs, State State)? Compose(
        DatabaseService db, int showId, IReadOnlyDictionary<string, string> connected)
    {
        var conn = db.GetConnection();
        string serial, folderRel;
        var state = new State();
        using (var c = conn.CreateCommand())
        {
            c.CommandText = "SELECT volume_serial, folder_rel_path, is_favorite, is_watchlist, note FROM tv_shows WHERE id=@id";
            c.Parameters.AddWithValue("@id", showId);
            using var r = c.ExecuteReader();
            if (!r.Read()) return null;
            serial = r.GetString(0);
            folderRel = r.IsDBNull(1) ? "" : r.GetString(1);
            state.Favorite = r.GetInt32(2) == 1;
            state.Watchlist = r.GetInt32(3) == 1;
            state.Note = r.IsDBNull(4) ? null : r.GetString(4);
        }
        if (string.IsNullOrEmpty(folderRel)) return null;
        if (!connected.TryGetValue(serial, out var letter)) return null;
        var folderAbs = Path.Combine($"{letter}:\\", folderRel.Replace('/', '\\'));

        state.Lists = db.GetUserListNamesForShow(showId);

        using (var c = conn.CreateCommand())
        {
            c.CommandText = "SELECT season, episode, is_watched, last_played_at FROM tv_episodes WHERE show_id=@id";
            c.Parameters.AddWithValue("@id", showId);
            using var r = c.ExecuteReader();
            while (r.Read())
            {
                var watched = r.GetInt32(2) == 1;
                var lp = r.IsDBNull(3) ? 0L : r.GetInt64(3);
                if (!watched && lp == 0) continue;
                state.Episodes[Key(r.GetInt32(0), r.GetInt32(1))] =
                    new EpisodeState { Watched = watched, LastPlayedUnix = lp > 0 ? lp : null };
            }
        }
        return (folderAbs, state);
    }

    /// <summary>
    /// Read-side import during a scan. DB wins where it already has a
    /// value (favorite/watchlist/note); per-episode watched is OR-merged.
    /// </summary>
    public static void ImportIntoShow(
        DatabaseService db, SqliteConnection conn, SqliteTransaction tx, int showId, string folderAbs)
    {
        var s = TryRead(folderAbs);
        if (s == null) return;

        // Show-level: DB wins if it already has a value.
        bool dbFav = false, dbWatch = false; string? dbNote = null;
        using (var sel = conn.CreateCommand())
        {
            sel.Transaction = tx;
            sel.CommandText = "SELECT is_favorite, is_watchlist, note FROM tv_shows WHERE id=@id";
            sel.Parameters.AddWithValue("@id", showId);
            using var r = sel.ExecuteReader();
            if (r.Read())
            {
                dbFav = r.GetInt32(0) == 1;
                dbWatch = r.GetInt32(1) == 1;
                dbNote = r.IsDBNull(2) ? null : r.GetString(2);
            }
        }
        using (var upd = conn.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = "UPDATE tv_shows SET is_favorite=@f, is_watchlist=@w, note=COALESCE(@n, note) WHERE id=@id";
            upd.Parameters.AddWithValue("@f", (dbFav || s.Favorite) ? 1 : 0);
            upd.Parameters.AddWithValue("@w", (dbWatch || s.Watchlist) ? 1 : 0);
            upd.Parameters.AddWithValue("@n", string.IsNullOrWhiteSpace(dbNote) ? (object?)s.Note ?? DBNull.Value : DBNull.Value);
            upd.Parameters.AddWithValue("@id", showId);
            upd.ExecuteNonQuery();
        }

        // Lists — additive, create-if-missing (same rule as movies).
        foreach (var listName in s.Lists.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(listName)) continue;
            int listId;
            using (var find = conn.CreateCommand())
            {
                find.Transaction = tx;
                find.CommandText = "SELECT id FROM user_lists WHERE name=@n";
                find.Parameters.AddWithValue("@n", listName);
                var ex = find.ExecuteScalar();
                if (ex != null && ex != DBNull.Value) listId = Convert.ToInt32(ex);
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
            link.CommandText = "INSERT OR IGNORE INTO user_list_shows(list_id, show_id) VALUES(@l,@s)";
            link.Parameters.AddWithValue("@l", listId);
            link.Parameters.AddWithValue("@s", showId);
            link.ExecuteNonQuery();
        }

        // Per-episode watched — OR-merge (DB true stays true).
        foreach (var (code, est) in s.Episodes)
        {
            var m = System.Text.RegularExpressions.Regex.Match(code, @"[Ss](\d+)[Ee](\d+)");
            if (!m.Success) continue;
            int season = int.Parse(m.Groups[1].Value);
            int ep = int.Parse(m.Groups[2].Value);
            using var upd = conn.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = @"UPDATE tv_episodes
                   SET is_watched = CASE WHEN is_watched=1 OR @w=1 THEN 1 ELSE 0 END,
                       last_played_at = MAX(last_played_at, @lp)
                 WHERE show_id=@id AND season=@se AND episode=@ep";
            upd.Parameters.AddWithValue("@w", est.Watched ? 1 : 0);
            upd.Parameters.AddWithValue("@lp", est.LastPlayedUnix ?? 0);
            upd.Parameters.AddWithValue("@id", showId);
            upd.Parameters.AddWithValue("@se", season);
            upd.Parameters.AddWithValue("@ep", ep);
            upd.ExecuteNonQuery();
        }
    }
}
