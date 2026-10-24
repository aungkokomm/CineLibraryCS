using CineLibraryCS.Models;
using CineLibraryCS.Services.Tmdb;

namespace CineLibraryCS.Services;

/// <summary>
/// v4.8.0: Fetch missing info from TMDB for a movie, shared by the movie window
/// (which lets you pick the match) and the selection bar's bulk fetch (which
/// takes only a sure match). Fill-only throughout: see FillMovieGaps and NfoFill.
/// </summary>
public static class MovieFetch
{
    /// <summary>
    /// Fills the movie's gaps from TMDb's <paramref name="d"/>: poster and fanart
    /// when it has none, the details, genres, director, writers and cast (with
    /// photos), then onto its drive now if it's connected, else when it is.
    /// <paramref name="status"/> hears "Fetching cast photos…".
    /// </summary>
    public static async Task<AppState.DriveSave> ApplyAsync(TmdbClient client, TmdbMovie d, MovieDetail m,
        Action<string>? status = null)
    {
        var db = AppState.Instance.Db;

        // Poster / fanart → portable cache, only if currently missing.
        string? posterRel = null, fanartRel = null;
        if (string.IsNullOrWhiteSpace(m.LocalPoster) && !string.IsNullOrEmpty(d.PosterPath))
            posterRel = await DownloadArtAsync(client, d.PosterPath!, "manual_posters", d.TmdbId);
        if (string.IsNullOrWhiteSpace(m.LocalFanart) && !string.IsNullOrEmpty(d.BackdropPath))
            fanartRel = await DownloadArtAsync(client, d.BackdropPath!, "manual_fanart", d.TmdbId);

        var studio = d.ProductionCompanies.Count > 0 ? d.ProductionCompanies[0].Name : null;
        var country = d.ProductionCountries.Count > 0 ? d.ProductionCountries[0].Name : null;

        db.FillMovieGaps(
            m.Id,
            year: d.Year > 0 ? d.Year : null,
            rating: d.Rating > 0 ? d.Rating : null,
            votes: d.VoteCount > 0 ? d.VoteCount : null,
            runtime: d.Runtime > 0 ? d.Runtime : null,
            plot: string.IsNullOrWhiteSpace(d.Overview) ? null : d.Overview,
            tagline: string.IsNullOrWhiteSpace(d.Tagline) ? null : d.Tagline,
            mpaa: string.IsNullOrWhiteSpace(d.Certification) ? null : d.Certification,
            imdbId: string.IsNullOrWhiteSpace(d.ImdbId) ? null : d.ImdbId,
            tmdbId: d.TmdbId.ToString(),
            premiered: string.IsNullOrWhiteSpace(d.ReleaseDate) ? null : d.ReleaseDate,
            studio: studio, country: country,
            posterRel: posterRel, fanartRel: fanartRel);

        // Genres / directors / writers — fill-only, so the detail window's
        // Genres and Director rows aren't left blank on a partial scrape.
        var genreNames = new List<string>();
        foreach (var g in d.Genres)
            if (!string.IsNullOrWhiteSpace(g.Name)) genreNames.Add(g.Name);
        db.FillMovieGenreDirectorWriter(m.Id, genreNames, d.Directors, d.Writers);

        // Cast — fetch photos for the top-billed cast and link them. Runs
        // even when the movie already has cast, because MediaElch NFOs often
        // store actor <thumb>s as TMDb http URLs (blank offline) or none at
        // all; we download portable copies and AddManualActors upgrades those
        // weak thumbs to the local cache.
        if (d.Cast.Count > 0)
        {
            status?.Invoke("Fetching cast photos…");
            var actors = new List<(string, string?, int, string?)>();
            foreach (var c in d.Cast)
            {
                if (string.IsNullOrWhiteSpace(c.Name)) continue;
                string? thumbRel = null;
                if (!string.IsNullOrEmpty(c.ProfilePath))
                    thumbRel = await DownloadArtRawAsync(client, c.ProfilePath!, "manual_actors", "w185");
                actors.Add((c.Name, string.IsNullOrWhiteSpace(c.Character) ? null : c.Character,
                            c.Order, thumbRel));
            }
            db.AddManualActors(m.Id, actors);
        }

        // v4.6.0: onto the drive now if it's connected, else when it is
        return await Task.Run(() => AppState.Instance.SyncFetchedTitle(false, m.Id, m.VolumeSerial));
    }

    /// <summary>
    /// The TMDb movie without asking: by its TMDb id, else its IMDb id, else a
    /// search that finds exactly one film of its title and year. Null when there's
    /// no sure match (the movie window lets you pick). Throws when TMDb can't be reached.
    /// </summary>
    public static async Task<TmdbMovie?> SureMatchAsync(TmdbClient client, MovieDetail m)
    {
        if (int.TryParse(m.TmdbId?.Trim(), out var tmdbId) && tmdbId > 0)
            return await client.GetMovieDetailsAsync(tmdbId) ?? throw new InvalidOperationException("No details from TMDb");
        if (ImdbRatingClient.IsImdbId(m.ImdbId) && await client.FindMovieByImdbAsync(m.ImdbId!.Trim()) is int found)
            return await client.GetMovieDetailsAsync(found) ?? throw new InvalidOperationException("No details from TMDb");
        if (m.Year is not > 0 || string.IsNullOrWhiteSpace(m.Title)) return null;
        var key = Key(m.Title);
        var hits = (await client.SearchMovieAsync(m.Title, m.Year))
            .Where(r => r.Year == m.Year && (Key(r.Title) == key || Key(r.OriginalTitle) == key))
            .ToList();
        return hits.Count == 1
            ? await client.GetMovieDetailsAsync(hits[0].TmdbId) ?? throw new InvalidOperationException("No details from TMDb")
            : null;
    }

    // Titles compared by their letters and digits: "Spider-Man: No Way Home" = "Spider Man No Way Home"
    private static string Key(string? title)
        => new string((title ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    public record BulkResult(int Updated, int Saved, int Waiting, List<string> NoMatch, int Failed, bool Stopped);

    /// <summary>
    /// The selection bar's Fetch missing info: each movie in turn, with a sure
    /// match only. <paramref name="progress"/> hears how many are done and the
    /// title being fetched. A stop ends after the current movie.
    /// </summary>
    public static async Task<BulkResult> FetchManyAsync(IReadOnlyList<int> movieIds,
        IProgress<(int Done, string Title)> progress, CancellationToken ct)
    {
        var app = AppState.Instance;
        using var client = new TmdbClient();
        int updated = 0, saved = 0, waiting = 0, failed = 0;
        var noMatch = new List<string>();
        for (int i = 0; i < movieIds.Count; i++)
        {
            if (ct.IsCancellationRequested) return new(updated, saved, waiting, noMatch, failed, Stopped: true);
            var m = app.Db.GetMovieDetail(movieIds[i], app.Connected);
            if (m == null) continue;
            progress.Report((i, m.Title));
            try
            {
                var d = await SureMatchAsync(client, m);
                if (d == null) { noMatch.Add(m.Year is > 0 ? $"{m.Title} ({m.Year})" : m.Title); continue; }
                var where = await ApplyAsync(client, d, m);
                updated++;
                if (where == AppState.DriveSave.Saved) saved++;
                else if (where == AppState.DriveSave.Waiting) waiting++;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Bulk fetch of '{m.Title}' failed: {ex.Message}");
                failed++;
            }
        }
        progress.Report((movieIds.Count, ""));
        return new(updated, saved, waiting, noMatch, failed, Stopped: false);
    }

    /// <summary>Downloads original-size art into a data-folder subfolder named by
    /// tmdb id; returns the data-relative path, or null on failure.</summary>
    internal static async Task<string?> DownloadArtAsync(
        TmdbClient client, string tmdbPath, string subfolder, int tmdbId)
    {
        var fileName = $"{tmdbId}-{Guid.NewGuid():N}.jpg";
        var full = Path.Combine(AppState.Instance.DataDir, subfolder, fileName);
        return await client.DownloadImageAsync(client.GetImageUrl(tmdbPath, "original"), full)
            ? $"{subfolder}/{fileName}" : null;
    }

    /// <summary>Downloads art keyed by its TMDb file name (used for actor photos);
    /// returns the data-relative path, or null on failure.</summary>
    internal static async Task<string?> DownloadArtRawAsync(
        TmdbClient client, string tmdbPath, string subfolder, string size)
    {
        var fileName = tmdbPath.TrimStart('/');
        var full = Path.Combine(AppState.Instance.DataDir, subfolder, fileName);
        if (File.Exists(full)) return $"{subfolder}/{fileName}";   // v4.8.0: the same photo, fetched for another title
        return await client.DownloadImageAsync(client.GetImageUrl(tmdbPath, size), full)
            ? $"{subfolder}/{fileName}" : null;
    }
}
