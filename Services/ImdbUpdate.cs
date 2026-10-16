using System.Xml.Linq;
using CineLibraryCS.Services.Tmdb;

namespace CineLibraryCS.Services;

/// <summary>
/// v4.6.0: "Update IMDb ratings" (Drives page). Gets the IMDb rating and votes
/// of every movie and show (IMDb ids from TMDb for .nfo files that only have a
/// TMDb id), shows them at once, and writes them into each .nfo's &lt;ratings&gt;
/// like CineLibrary Essentials' Refresh IMDb ratings: on connected drives now,
/// on the others when they're plugged in (<see cref="WritePending"/>).
/// </summary>
public static class ImdbUpdate
{
    public record Result(int Rated, int Written, int Waiting, int NotOnImdb, int NoId, int LookedUp,
        int WriteFailed, bool Unreachable);

    public static async Task<Result> RunAsync(bool preferImdb, IProgress<string> status, CancellationToken ct)
    {
        var db = AppState.Instance.Db;
        var titles = await Task.Run(db.GetTitlesForImdb, ct);

        // 1. The IMDb id of each title; from TMDb when the .nfo only has a TMDb id
        var ids = new Dictionary<DatabaseService.ImdbTitle, (string Id, bool Added)>();
        var needId = new List<(DatabaseService.ImdbTitle Title, int TmdbId)>();
        foreach (var t in titles)
        {
            if (ImdbRatingClient.IsImdbId(t.ImdbId)) ids[t] = (t.ImdbId!.Trim(), false);
            else if (int.TryParse(t.TmdbId?.Trim(), out var tmdbId) && tmdbId > 0) needId.Add((t, tmdbId));
        }
        int lookedUp = 0;
        if (needId.Count > 0)
        {
            using var tmdb = new TmdbClient();
            for (int i = 0; i < needId.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                status.Report($"Finding IMDb ids on TMDb… {i + 1:N0} of {needId.Count:N0}");
                try
                {
                    var id = await tmdb.GetImdbIdAsync(needId[i].TmdbId, needId[i].Title.Show, ct);
                    if (ImdbRatingClient.IsImdbId(id)) { ids[needId[i].Title] = (id!.Trim(), true); lookedUp++; }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch { /* offline, or not on TMDb: left without */ }
            }
        }

        // 2. The ratings, 100 titles a request
        var unique = ids.Values.Select(v => v.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        status.Report($"Getting IMDb ratings… 0 of {unique:N0}");
        var (ratings, failedBatches) = await ImdbRatingClient.GetRatingsAsync(ids.Values.Select(v => v.Id),
            new Progress<int>(n => status.Report($"Getting IMDb ratings… {n:N0} of {unique:N0}")), ct);
        if (ratings.Count == 0 && unique > 0 && failedBatches > 0)
            return new Result(0, 0, db.CountImdbPending(), 0, titles.Count - ids.Count, lookedUp, 0, Unreachable: true);

        // 3. Shown in CineLibrary straight away, and queued for the .nfo files
        var items = ids.Where(kv => ratings.ContainsKey(kv.Value.Id))
            .Select(kv => (kv.Key, kv.Value.Id, kv.Value.Added, ratings[kv.Value.Id].Value, ratings[kv.Value.Id].Votes))
            .ToList();
        await Task.Run(() => db.SaveImdbRatings(items, preferImdb), CancellationToken.None);

        // 4. Written into the .nfo files on connected drives (a stop leaves the
        //    rest queued, written later like a disconnected drive's)
        var (written, failed) = await Task.Run(() => WritePending(null,
            (n, total) => status.Report($"Writing .nfo files… {n:N0} of {total:N0}"), ct), CancellationToken.None);

        return new Result(items.Count, written, db.CountImdbPending(), ids.Count - items.Count,
            titles.Count - ids.Count, lookedUp, failed, Unreachable: false);
    }

    // One writer at a time: the button, start-up and a drive plugged in can overlap
    private static readonly object Gate = new();

    /// <summary>
    /// Writes the queued IMDb ratings into the .nfo files on connected drives
    /// (optionally one drive): only the &lt;ratings&gt; block, plus the IMDb id
    /// where it was missing. Others wait. Call off the UI thread.
    /// </summary>
    public static (int Written, int Failed) WritePending(string? onlyVolumeSerial = null,
        Action<int, int>? progress = null, CancellationToken ct = default)
    {
        lock (Gate)
        {
            var app = AppState.Instance;
            var db = app.Db;
            var connected = app.Connected;
            var pending = db.GetImdbNfoPending(onlyVolumeSerial)
                .Where(p => p.FolderRel != null && connected.ContainsKey(p.Serial)).ToList();
            var done = new List<DatabaseService.ImdbPending>();
            int written = 0, failed = 0;
            for (int i = 0; i < pending.Count && !ct.IsCancellationRequested; i++)
            {
                var p = pending[i];
                progress?.Invoke(i + 1, pending.Count);
                var folder = Path.Combine($"{connected[p.Serial]}:\\", p.FolderRel!.Replace('/', '\\'));
                if (!Directory.Exists(folder)) continue;   // waits
                var nfo = p.Show ? Path.Combine(folder, "tvshow.nfo") : ScannerService.FindNfo(folder);
                if (nfo == null || !File.Exists(nfo)) { db.DropImdbPending(p.Show, p.Id); continue; }
                try
                {
                    var doc = XDocument.Load(nfo);
                    bool changed = NfoRatings.ApplyImdb(doc.Root!, p.Rating, p.Votes, p.PreferImdb);
                    if (p.AddId) changed |= NfoRatings.EnsureImdbId(doc.Root!, p.ImdbId);
                    if (changed)
                    {
                        NfoRatings.Save(doc, nfo);
                        // CineLibrary's own copy of the .nfo follows (start-up re-reads use it)
                        var cached = p.LocalNfo == null ? null : db.GetCachedImagePath(p.LocalNfo);
                        if (cached != null) File.Copy(nfo, cached, overwrite: true);
                        written++;
                    }
                    done.Add(p);
                }
                catch (Exception ex)
                {
                    failed++;   // read-only, in use, not XML: tried again next time
                    System.Diagnostics.Debug.WriteLine($"IMDb rating not written to {nfo}: {ex.Message}");
                }
                if (done.Count >= 200) { db.FinishImdbNfoWrites(done); done.Clear(); }
            }
            db.FinishImdbNfoWrites(done);
            return (written, failed);
        }
    }
}
