using System.Text.RegularExpressions;
using CineLibraryCS.Services.Tmdb;

namespace CineLibraryCS.Services;

/// <summary>
/// v4.6.0: the aired episodes of a show that aren't on disk, from TMDb's episode
/// lists (a port of CineLibrary Essentials' check). Specials (season 0) and
/// anything after TMDb's last aired episode never count as missing.
/// </summary>
public static class MissingEpisodes
{
    /// <summary>
    /// One missing episode, or a whole missing season when Episode is 0 (Count
    /// is then how many of its episodes have aired).
    /// </summary>
    public record Gap(int Season, int Episode, string Title, string? AirDate, int Count = 1);

    // "S01E01E02", "S01E01-E02", "S01E01-02": the last number is the last episode
    private static readonly Regex MultiEpisode = new(
        @"S\d{1,3}E\d{1,3}(?:(?:[\-_]?E|-)(?<x>\d{1,3})(?!\d))+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The last episode a file holds: 2 for "S01E01E02", else the first episode.</summary>
    public static int LastEpisodeOf(string? fileName, int firstEpisode)
    {
        if (string.IsNullOrEmpty(fileName)) return firstEpisode;
        var m = MultiEpisode.Match(fileName);
        if (!m.Success) return firstEpisode;
        var last = m.Groups["x"].Captures.Select(c => int.Parse(c.Value)).DefaultIfEmpty(firstEpisode).Max();
        // Only a short run after the first episode is believable as a double episode
        return last > firstEpisode && last - firstEpisode <= 5 ? last : firstEpisode;
    }

    /// <summary>Every (season, episode) the files cover, double episodes counted in full.</summary>
    public static HashSet<(int Season, int Episode)> Covered(
        IEnumerable<(int Season, int Episode, string? VideoRel)> onDisk)
    {
        var set = new HashSet<(int, int)>();
        foreach (var (season, episode, video) in onDisk)
        {
            var last = LastEpisodeOf(video == null ? null : Path.GetFileNameWithoutExtension(video), episode);
            for (int e = episode; e <= last; e++) set.Add((season, e));
        }
        return set;
    }

    public static List<Gap> Find(
        ISet<(int Season, int Episode)> local,
        TmdbTvSeasons show,
        IReadOnlyDictionary<int, List<TmdbEpisode>> seasonEpisodes)
    {
        var gaps = new List<Gap>();
        if (show.LastAired is not { } last || last.Season <= 0) return gaps;   // nothing has aired yet

        bool Aired(int s, int e) => s < last.Season || (s == last.Season && e <= last.Episode);
        var localSeasons = local.Select(x => x.Season).ToHashSet();

        // Seasons you have: each aired episode that isn't on disk
        foreach (var (season, episodes) in seasonEpisodes)
        {
            if (season <= 0 || !localSeasons.Contains(season)) continue;
            foreach (var ep in episodes)
                if (ep.Number > 0 && Aired(season, ep.Number) && !local.Contains((season, ep.Number)))
                    gaps.Add(new Gap(season, ep.Number, ep.Name, ep.AirDate));
        }

        // Seasons you don't have at all: one entry each
        foreach (var (number, count) in show.Seasons)
        {
            if (number <= 0 || localSeasons.Contains(number)) continue;
            var aired = number < last.Season ? count : number == last.Season ? last.Episode : 0;
            if (aired > 0) gaps.Add(new Gap(number, 0, $"Season {number}", null, aired));
        }

        return gaps.OrderBy(g => g.Season).ThenBy(g => g.Episode).ToList();
    }

    /// <summary>How many episodes the gaps add up to.</summary>
    public static int Total(IEnumerable<Gap> gaps) => gaps.Sum(g => g.Count);

    // TMDb's answers for this session, by show: a show page opened again (or
    // another season tab) doesn't ask again.
    private static readonly Dictionary<int, (TmdbTvSeasons Show, Dictionary<int, List<TmdbEpisode>> Seasons)> _cache = new();
    private static readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// The gaps for a show from TMDb, fetching the episode lists of the seasons
    /// on disk. Null when TMDb can't be reached (offline).
    /// </summary>
    public static async Task<List<Gap>?> CheckAsync(int tmdbId, ISet<(int Season, int Episode)> local)
    {
        await _gate.WaitAsync();
        try
        {
            using var client = new TmdbClient();
            if (!_cache.TryGetValue(tmdbId, out var data))
            {
                var show = await client.GetTvSeasonsAsync(tmdbId);
                if (show == null) return null;
                data = (show, new Dictionary<int, List<TmdbEpisode>>());
                _cache[tmdbId] = data;
            }
            foreach (var season in local.Select(x => x.Season).Where(s => s > 0).Distinct())
                if (!data.Seasons.ContainsKey(season))
                    data.Seasons[season] = await client.GetTvSeasonEpisodesAsync(tmdbId, season);
            return Find(local, data.Show, data.Seasons);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Missing episodes check failed: {ex.Message}");
            return null;
        }
        finally { _gate.Release(); }
    }
}
