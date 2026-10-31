using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace CineLibraryCS.Services.Playback;

/// <summary>What to look for: a film, or an episode of a show.</summary>
internal sealed record SubtitleFilm(string Title, int? Year, string? ImdbId, int? Season, int? Episode);

/// <summary>A subtitle file found online. ExactMatch: made for this very file.</summary>
internal sealed record SubtitleHit(string Language, string Code, string FileName, long FileId, bool ExactMatch);

/// <summary>What a search came back with; Error is a line to show when it failed.</summary>
internal sealed record SubtitleResults(IReadOnlyList<SubtitleHit> Hits, string? Error);

/// <summary>Where a download was saved, or the line to show when it failed.</summary>
internal sealed record SubtitleSaved(string? Path, string? Error);

/// <summary>
/// v4.10.0: finds subtitles on OpenSubtitles.com, with no login: by the file's hash and the
/// film's IMDb number, else its title, in the language chosen in Settings and in English.
/// </summary>
internal static class SubtitleSearch
{
    private const int MostFound = 30, BiggestFile = 8 * 1024 * 1024, SmallestFile = 1024;
    private static string _endpoint = "https://api.opensubtitles.com/api/v1";
    private static readonly string Agent = $"CineLibrary v{typeof(SubtitleSearch).Assembly.GetName().Version?.ToString(3)}";
    private static readonly string Key = ReadKey();
    private static readonly HttpClient Http = CreateClient();

    /// <summary>False in a build made without the key: the menu then doesn't offer the search.</summary>
    internal static bool Available = Key.Length > 0;

    // The names in Settings > Playback, as OpenSubtitles writes them
    private static readonly Dictionary<string, string> Codes = new()
    {
        ["English"] = "en", ["Hindi"] = "hi", ["Burmese"] = "my", ["Tamil"] = "ta", ["Telugu"] = "te",
        ["Malayalam"] = "ml", ["Kannada"] = "kn", ["Bengali"] = "bn", ["Marathi"] = "mr", ["Urdu"] = "ur",
        ["Arabic"] = "ar", ["Chinese"] = "zh-cn", ["French"] = "fr", ["German"] = "de", ["Indonesian"] = "id",
        ["Italian"] = "it", ["Japanese"] = "ja", ["Korean"] = "ko", ["Portuguese"] = "pt-pt", ["Russian"] = "ru",
        ["Spanish"] = "es", ["Thai"] = "th", ["Turkish"] = "tr", ["Vietnamese"] = "vi",
    };

    // Built in from ThirdParty\opensubtitles.key, which stays out of the public source
    private static string ReadKey()
    {
        using var stream = typeof(SubtitleSearch).Assembly.GetManifestResourceStream("OpenSubtitlesKey");
        if (stream is null) return "";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = BiggestFile };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", Agent);
        return client;
    }

    /// <summary>The best first: the chosen language before English, an exact match before the rest.</summary>
    public static async Task<SubtitleResults> FindAsync(SubtitleFilm film, string path)
    {
        var codes = new[] { Codes.GetValueOrDefault(UiSettings.PreferredSubtitles), "en" }.OfType<string>().Distinct().ToList();
        try
        {
            var hits = new List<SubtitleHit>();
            var byFile = await Task.Run(() => ByFile(film, path, codes));
            if (byFile.Count > 1) hits = Rank(await SearchAsync(byFile), film, codes);
            if (hits.Count == 0) hits = Rank(await SearchAsync(ByTitle(film, codes)), film, codes);
            return new(hits, null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            PlayerLog.Write("subtitle search failed: " + e.Message);
            return new([], "The subtitle service didn't answer");
        }
    }

    /// <summary>Saves it beside the film under a name VLC finds next time, else in the data folder.</summary>
    public static async Task<SubtitleSaved> DownloadAsync(SubtitleHit hit, string filmPath)
    {
        const string Failed = "Couldn't get those subtitles";
        try
        {
            using var request = Api(HttpMethod.Post, "/download");
            request.Content = new StringContent($"{{\"file_id\":{hit.FileId}}}", Encoding.UTF8, "application/json");
            using var response = await Http.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.NotAcceptable) return new(null, "Today's subtitle downloads are used up");
            response.EnsureSuccessStatusCode();
            using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            PlayerLog.Write($"subtitle downloads left today: {answer.RootElement.GetProperty("remaining")}");
            var text = await Http.GetByteArrayAsync(answer.RootElement.GetProperty("link").GetString());
            if (text.Length < SmallestFile) return new(null, Failed);   // a message, not a film's subtitles
            return Save(text, hit, filmPath) is { } path ? new(path, null) : new(null, Failed);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException or IOException)
        {
            PlayerLog.Write("subtitle download failed: " + e.Message);
            return new(null, Failed);
        }
    }

    // Only OpenSubtitles' own address gets the key, not the link a file comes from
    private static HttpRequestMessage Api(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, _endpoint + path);
        request.Headers.Add("Api-Key", Key);
        request.Headers.Add("Accept", "application/json");
        return request;
    }

    private static async Task<string> SearchAsync(SortedDictionary<string, string> query)
    {
        // Their rule: names in order, in lower case, or the answer is a redirect
        var line = string.Join('&', query.Select(q => $"{q.Key}={Uri.EscapeDataString(q.Value)}"));
        using var request = Api(HttpMethod.Get, "/subtitles?" + line);
        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static SortedDictionary<string, string> Query(SubtitleFilm film, List<string> codes)
    {
        var query = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["languages"] = string.Join(',', codes.Order(StringComparer.Ordinal)) };
        if (film.Season is { } season && film.Episode is { } episode)
        {
            query["season_number"] = season.ToString();
            query["episode_number"] = episode.ToString();
        }
        return query;
    }

    private static SortedDictionary<string, string> ByFile(SubtitleFilm film, string path, List<string> codes)
    {
        var query = Query(film, codes);
        if (Fingerprint(path) is { } hash) query["moviehash"] = hash;
        var imdb = new string((film.ImdbId ?? "").Where(char.IsDigit).ToArray()).TrimStart('0');
        if (imdb.Length > 0) query[film.Season is null ? "imdb_id" : "parent_imdb_id"] = imdb;
        return query;
    }

    private static SortedDictionary<string, string> ByTitle(SubtitleFilm film, List<string> codes)
    {
        var query = Query(film, codes);
        query["query"] = film.Title.ToLowerInvariant();
        if (film.Year is { } year) query["year"] = year.ToString();
        return query;
    }

    /// <summary>OpenSubtitles' hash of a video: its size plus the 64-bit words of its first and last 64 KiB.</summary>
    private static string? Fingerprint(string path)
    {
        const int Chunk = 65536;
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1);
            if (file.Length < Chunk) return null;
            var hash = (ulong)file.Length;
            var buffer = new byte[Chunk];
            foreach (var start in new[] { 0, file.Length - Chunk })
            {
                file.Position = start;
                file.ReadExactly(buffer);
                foreach (var word in MemoryMarshal.Cast<byte, ulong>(buffer)) hash = unchecked(hash + word);
            }
            return hash.ToString("x16");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static List<SubtitleHit> Rank(string json, SubtitleFilm film, List<string> codes)
    {
        using var answer = JsonDocument.Parse(json);
        var kept = new List<(SubtitleHit Hit, int Language, long Downloads)>();
        foreach (var row in answer.RootElement.GetProperty("data").EnumerateArray())
        {
            var found = row.GetProperty("attributes");
            var code = found.GetProperty("language").GetString() ?? "";
            var files = found.GetProperty("files");
            if (!codes.Contains(code) || files.GetArrayLength() != 1) continue;   // a film in two parts
            if (Flag(found, "foreign_parts_only") || Flag(found, "machine_translated")) continue;
            if (!Fits(found, film)) continue;
            var file = files[0];
            found.TryGetProperty("download_count", out var downloads);
            var hit = new SubtitleHit(Codes.First(c => c.Value == code).Key, code, file.GetProperty("file_name").GetString() ?? "", file.GetProperty("file_id").GetInt64(), Flag(found, "moviehash_match"));
            kept.Add((hit, codes.IndexOf(code), downloads.ValueKind == JsonValueKind.Number ? downloads.GetInt64() : 0));
        }
        return kept.OrderBy(k => k.Language).ThenByDescending(k => k.Hit.ExactMatch).ThenByDescending(k => k.Downloads).Take(MostFound).Select(k => k.Hit).ToList();
    }

    private static bool Flag(JsonElement found, string name) => found.TryGetProperty(name, out var flag) && flag.ValueKind == JsonValueKind.True;

    // A search by words also finds other films of the same name
    private static bool Fits(JsonElement found, SubtitleFilm film)
    {
        if (film.Year is not { } year || film.Season != null || Flag(found, "moviehash_match")) return true;
        if (!found.TryGetProperty("feature_details", out var details) || !details.TryGetProperty("year", out var when) || when.ValueKind != JsonValueKind.Number) return true;
        return Math.Abs(when.GetInt32() - year) <= 1;
    }

    private static string? Save(byte[] text, SubtitleHit hit, string filmPath)
    {
        var name = Path.GetFileNameWithoutExtension(filmPath);
        foreach (var folder in new[] { Path.GetDirectoryName(filmPath) ?? "", Path.Combine(AppState.Instance.DataDir, "subtitles") })
        {
            try
            {
                Directory.CreateDirectory(folder);
                for (var n = 1; n < 100; n++)
                {
                    var path = Path.Combine(folder, $"{name}.{hit.Code}{(n > 1 ? $".{n}" : "")}.srt");
                    if (File.Exists(path)) continue;   // never over one that's there
                    File.WriteAllBytes(path, text);
                    return path;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                PlayerLog.Write($"can't save subtitles in {folder}: {e.Message}");   // a read-only drive
            }
        }
        return null;
    }
}
