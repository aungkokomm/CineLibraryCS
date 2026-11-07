using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CineLibraryCS.Services.Playback;

/// <summary>5.3.0: where an item's intro, recap and credits are, and a scene after them (ms); null when not known.
/// VersionLength: how long the release version the times were taken on is, to tell another cut from this file.</summary>
public sealed record SkipTimes(long? IntroStart, long? IntroEnd, long? RecapStart, long? RecapEnd, long? Credits, long? AfterCredits,
                               long? VersionLength = null)
{
    public static readonly SkipTimes None = new(null, null, null, null, null, null);
}

/// <summary>5.3.0: TheIntroDB's times as a state file keeps them, so they travel with the drive.</summary>
public sealed class SkipTimesEntry
{
    [JsonPropertyName("fetchedUnix")]     public long FetchedUnix { get; set; }
    [JsonPropertyName("introStartMs")]    public long? IntroStart { get; set; }
    [JsonPropertyName("introEndMs")]      public long? IntroEnd { get; set; }
    [JsonPropertyName("recapStartMs")]    public long? RecapStart { get; set; }
    [JsonPropertyName("recapEndMs")]      public long? RecapEnd { get; set; }
    [JsonPropertyName("creditsMs")]       public long? Credits { get; set; }
    [JsonPropertyName("afterCreditsMs")]  public long? AfterCredits { get; set; }
    [JsonPropertyName("versionLengthMs")] public long? VersionLength { get; set; }

    [JsonIgnore] public SkipTimes Times => new(IntroStart, IntroEnd, RecapStart, RecapEnd, Credits, AfterCredits, VersionLength);

    public static SkipTimesEntry From(long fetchedUnix, SkipTimes t) => new()
    {
        FetchedUnix = fetchedUnix, IntroStart = t.IntroStart, IntroEnd = t.IntroEnd, RecapStart = t.RecapStart,
        RecapEnd = t.RecapEnd, Credits = t.Credits, AfterCredits = t.AfterCredits, VersionLength = t.VersionLength,
    };
}

/// <summary>5.3.0: a season's learned times, as the show's state file keeps them.</summary>
public sealed class SeasonTimes
{
    [JsonPropertyName("introStartMs")]     public long? IntroStart { get; set; }
    [JsonPropertyName("introEndMs")]       public long? IntroEnd { get; set; }
    [JsonPropertyName("creditsFromEndMs")] public long? CreditsFromEnd { get; set; }
}

/// <summary>
/// 5.3.0: intro, recap and credits times from TheIntroDB (theintrodb.org), the community database
/// Jellyfin, Emby, Kodi and mpv read too: by the film's TMDB id (else its IMDb id), or the show's with
/// the season and episode, and the file's length, which picks the release version it matches
/// (theatrical, extended). Asked once for each item, the first time it plays, and the answer kept for
/// good (found or not), here and in the drive's state file: playing it again never asks. Off in Settings.
/// </summary>
internal static partial class TheIntroDb
{
    private static readonly HttpClient Http = CreateClient();
    private static DateTimeOffset _waitUntil;   // after "too many requests", not asked again until then

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"CineLibrary/{typeof(TheIntroDb).Assembly.GetName().Version?.ToString(3)}");
        return http;
    }

    /// <summary>The times for a film or an episode of this length, kept or asked for; null when it can't be
    /// asked (no id, offline: asked again the next time it plays). Never throws.</summary>
    public static async Task<SkipTimes?> GetAsync(PlaybackItem item, long length)
    {
        var db = AppState.Instance.Db;
        var episode = item.Kind == PlaybackKind.Episode;
        try
        {
            // asked once, never again. Except an episode kept with credits past its own version's end, from before
            // that was checked (no kept time is like that since): asked once more, for the credits as below
            if (db.GetSkipTimes(episode, item.Id) is { } kept
                && !(episode && kept.Times is { Credits: long keptCredits, VersionLength: long keptVersion } && keptCredits >= keptVersion))
                return kept.Times;
            if (DateTimeOffset.UtcNow < _waitUntil) return null;
            var (imdb, tmdb, season, number) = db.GetSkipLookup(episode, item.Id);
            var id = int.TryParse(tmdb, NumberStyles.None, CultureInfo.InvariantCulture, out var t) && t > 0 ? $"tmdb_id={t}"
                   : imdb?.Trim() is { } i && ImdbId().IsMatch(i) ? $"imdb_id={i}" : null;
            if (id is null || episode && (season is null || number is null)) return null;
            var url = $"https://api.theintrodb.org/v4/media?{id}" + (episode ? $"&season={season}&episode={number}" : "")
                      + (length > 0 ? $"&duration_ms={length}" : "") + "&details=true";
            using var response = await Http.GetAsync(url);
            var status = (int)response.StatusCode;
            if (status == 429)
            {
                var wait = response.Headers.RetryAfter?.Delta ?? RetryAfter(await response.Content.ReadAsStringAsync()) ?? TimeSpan.FromMinutes(15);
                _waitUntil = DateTimeOffset.UtcNow + (wait > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : wait);
                PlayerLog.Write($"TheIntroDB: too many requests, waiting until {_waitUntil:u}");
                return null;
            }
            // Nothing there (404), or a question it can't answer (400, 422): kept too, so it's not asked again
            if (status is 404 or 400 or 422) return Keep(SkipTimes.None);
            if (!response.IsSuccessStatusCode) return null;   // its own trouble: asked the next time it plays
            var times = Parse(await response.Content.ReadAsStringAsync());
            PlayerLog.Write($"TheIntroDB {id}{(episode ? $" S{season:D2}E{number:D2}" : "")}: {times}");
            // An episode whose version has no credits (or a bad time): the version most people timed, as far
            // from the end (credits run about as long in any cut, as a season learns them). Still this one lookup
            if (episode && times is { Credits: null, VersionLength: long version } && length > 0
                && await CreditsFromEndAsync(id, season, number) is long fromEnd && length - fromEnd < version)
            {
                times = times with { Credits = length - fromEnd };
                PlayerLog.Write($"TheIntroDB: credits {fromEnd} ms from the end, as in its main version: {times.Credits}");
            }
            return Keep(times);

            SkipTimes Keep(SkipTimes found)
            {
                db.SetSkipTimes(episode, item.Id, found);
                AppState.Instance.SavePlaybackState(episode, item.Id);   // into the state file, with the drive
                return found;
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException
                                    or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            PlayerLog.Write("TheIntroDB: " + e.Message);
            return null;
        }
    }

    /// <summary>How far from the end an episode's credits start in the version most people timed (asked
    /// without a length): 10 s to 6 minutes, as a season learns them; null when it doesn't say.</summary>
    private static async Task<long?> CreditsFromEndAsync(string id, int? season, int? number)
    {
        try
        {
            using var response = await Http.GetAsync($"https://api.theintrodb.org/v4/media?{id}&season={season}&episode={number}&details=true");
            if (!response.IsSuccessStatusCode) return null;
            var main = Parse(await response.Content.ReadAsStringAsync());
            return main is { Credits: long credits, VersionLength: long version } && version - credits is >= 10_000 and <= 360_000
                ? version - credits : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            PlayerLog.Write("TheIntroDB, main version: " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// TheIntroDB's answer: intro, recap, credits and preview, each a list of runs with start_ms and end_ms
    /// (a start of null is the beginning, an end of null the end). Credits that stop before the end have a
    /// scene after them, unless what follows is a preview of the next one. group_duration_ms is the length
    /// of the release version the times are for.
    /// </summary>
    internal static SkipTimes Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return SkipTimes.None;
        static long? Ms(JsonElement e, string field) =>
            e.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Number ? (long)v.GetDouble() : null;
        List<(long? Start, long? End)> Runs(string kind) =>
            root.TryGetProperty(kind, out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Where(s => s.ValueKind == JsonValueKind.Object)
                      .Select(s => (Ms(s, "start_ms"), Ms(s, "end_ms"))).OrderBy(r => r.Item1 ?? 0).ToList()
                : [];
        var intro = Runs("intro").FirstOrDefault(r => r.End is > 0);
        var recap = Runs("recap").FirstOrDefault(r => r.End is > 0);
        var credits = Runs("credits").FirstOrDefault(r => r.Start is > 0);
        // credits past the end of their own version are a bad entry (one for a 52:59 cut said 55:56)
        if (credits.Start >= Ms(root, "group_duration_ms")) credits = default;
        var previews = Runs("preview");
        long? after = credits.End is long end && !previews.Any(p => p.Start is long s && Math.Abs(s - end) < 5000) ? end : null;
        return new SkipTimes(intro.End is null ? null : intro.Start ?? 0, intro.End, recap.End is null ? null : recap.Start ?? 0, recap.End,
                             credits.Start, credits.Start is null ? null : after, Ms(root, "group_duration_ms"));
    }

    /// <summary>
    /// The times as they fit this file. More than 5 minutes from the version they're for, it's another
    /// version or another episode: none of them. Over a minute (an extended cut, a PAL copy 4% faster) the
    /// credits would come early and mark it watched early: not those. The version not known, credits more
    /// than 20 minutes from the end aren't this file's credits.
    /// </summary>
    internal static SkipTimes? Matched(SkipTimes t, long length)
    {
        var noCredits = t with { Credits = null, AfterCredits = null };
        if (t.VersionLength is long version)
        {
            var apart = Math.Abs(version - length);
            return apart > 300_000 ? null : apart > 60_000 ? noCredits : t;
        }
        return t.Credits is long c && length - c > 1_200_000 ? noCredits : t;
    }

    /// <summary>A 429 answer's retry_after_seconds.</summary>
    private static TimeSpan? RetryAfter(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("retry_after_seconds", out var v)
                   && v.ValueKind == JsonValueKind.Number ? TimeSpan.FromSeconds(Math.Max(1, v.GetDouble())) : null;
        }
        catch (JsonException) { return null; }
    }

    [GeneratedRegex(@"^tt\d{7,8}$")]
    private static partial Regex ImdbId();
}
