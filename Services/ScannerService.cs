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
    public async Task ScanAsync(string volumeSerial, string driveRoot, IProgress<ScanProgress>? progress = null, CancellationToken ct = default, string? scanFolder = null)
    {
        await Task.Run(() => ScanSync(volumeSerial, driveRoot, progress, ct, scanFolder), ct);
    }

    private void ScanSync(string volumeSerial, string driveRoot, IProgress<ScanProgress>? progress, CancellationToken ct, string? scanFolder)
    {
        var conn = _db.GetConnection();
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
        stmtGetId.CommandText = "SELECT id FROM movies WHERE volume_serial=@s AND folder_rel_path=@f";
        stmtGetId.Parameters.AddWithValue("@s", volumeSerial);
        stmtGetId.Parameters.Add("@f", SqliteType.Text);

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

                if (existingId != null && existingId != DBNull.Value)
                {
                    // Clear params from any previous iteration before re-binding
                    stmtUpdate.Parameters.Clear();
                    var id = Convert.ToInt32(existingId);
                    BindMovieParams(stmtUpdate, parsed, videoRelPath, localPoster, localFanart, localNfo);
                    stmtUpdate.Parameters.AddWithValue("@id", id);
                    stmtUpdate.ExecuteNonQuery();
                    UpsertRelated(conn, tx, id, parsed);
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
                    var newId = Convert.ToInt32(lastId.ExecuteScalar());
                    UpsertRelated(conn, tx, newId, parsed);
                    inserted++;
                }
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

    private static void UpsertRelated(SqliteConnection conn, SqliteTransaction tx, int movieId, ParsedMovie p)
    {
        SqliteCommand Cmd(string sql)
        {
            var c = conn.CreateCommand();
            c.CommandText = sql;
            c.Transaction = tx;
            return c;
        }

        using (var d = Cmd("DELETE FROM movie_genres   WHERE movie_id=@id")) { d.Parameters.AddWithValue("@id", movieId); d.ExecuteNonQuery(); }
        using (var d = Cmd("DELETE FROM movie_directors WHERE movie_id=@id")) { d.Parameters.AddWithValue("@id", movieId); d.ExecuteNonQuery(); }
        using (var d = Cmd("DELETE FROM movie_actors   WHERE movie_id=@id")) { d.Parameters.AddWithValue("@id", movieId); d.ExecuteNonQuery(); }
        using (var d = Cmd("DELETE FROM movie_sets     WHERE movie_id=@id")) { d.Parameters.AddWithValue("@id", movieId); d.ExecuteNonQuery(); }

        foreach (var g in p.Genres)
        {
            using (var c = Cmd("INSERT OR IGNORE INTO genres (name) VALUES (@n)")) { c.Parameters.AddWithValue("@n", g); c.ExecuteNonQuery(); }
            int gid;
            using (var c = Cmd("SELECT id FROM genres WHERE name=@n")) { c.Parameters.AddWithValue("@n", g); gid = Convert.ToInt32(c.ExecuteScalar()); }
            using (var c = Cmd("INSERT OR IGNORE INTO movie_genres VALUES (@m,@g)")) { c.Parameters.AddWithValue("@m", movieId); c.Parameters.AddWithValue("@g", gid); c.ExecuteNonQuery(); }
        }
        foreach (var d in p.Directors)
        {
            using (var c = Cmd("INSERT OR IGNORE INTO directors (name) VALUES (@n)")) { c.Parameters.AddWithValue("@n", d); c.ExecuteNonQuery(); }
            int did;
            using (var c = Cmd("SELECT id FROM directors WHERE name=@n")) { c.Parameters.AddWithValue("@n", d); did = Convert.ToInt32(c.ExecuteScalar()); }
            using (var c = Cmd("INSERT OR IGNORE INTO movie_directors VALUES (@m,@d)")) { c.Parameters.AddWithValue("@m", movieId); c.Parameters.AddWithValue("@d", did); c.ExecuteNonQuery(); }
        }
        foreach (var a in p.Actors)
        {
            using (var c = Cmd("INSERT OR IGNORE INTO actors (name, thumb) VALUES (@n,@t)")) { c.Parameters.AddWithValue("@n", a.Name); c.Parameters.AddWithValue("@t", (object?)a.Thumb ?? DBNull.Value); c.ExecuteNonQuery(); }
            int aid;
            using (var c = Cmd("SELECT id FROM actors WHERE name=@n")) { c.Parameters.AddWithValue("@n", a.Name); aid = Convert.ToInt32(c.ExecuteScalar()); }
            using (var c = Cmd("INSERT OR REPLACE INTO movie_actors VALUES (@m,@a,@r,@o)")) { c.Parameters.AddWithValue("@m", movieId); c.Parameters.AddWithValue("@a", aid); c.Parameters.AddWithValue("@r", (object?)a.Role ?? DBNull.Value); c.Parameters.AddWithValue("@o", a.Order); c.ExecuteNonQuery(); }
        }
        foreach (var s in p.Sets)
        {
            using (var c = Cmd("INSERT OR IGNORE INTO sets (name) VALUES (@n)")) { c.Parameters.AddWithValue("@n", s); c.ExecuteNonQuery(); }
            int sid;
            using (var c = Cmd("SELECT id FROM sets WHERE name=@n")) { c.Parameters.AddWithValue("@n", s); sid = Convert.ToInt32(c.ExecuteScalar()); }
            using (var c = Cmd("INSERT OR IGNORE INTO movie_sets VALUES (@m,@s)")) { c.Parameters.AddWithValue("@m", movieId); c.Parameters.AddWithValue("@s", sid); c.ExecuteNonQuery(); }
        }
    }

    private static IEnumerable<string> FindMovieFolders(string root)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            if (FindNfo(dir) != null) yield return dir;
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
            File.Copy(srcPath, destPath, overwrite: true);
            return $"cache/{movieKey}/{destName}";
        }
        catch { return null; }
    }

    private static string ComputeMovieKey(string volumeSerial, string folderRelPath)
    {
        var input = $"{volumeSerial}|{folderRelPath}";
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16].ToLower();
    }
}
