using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace CineLibraryCS.Services.Tmdb;

/// <summary>
/// Minimal, movie-only TMDb client — a trimmed port of CineLibrary Essentials'
/// scraper. CineLibrary is otherwise fully offline; this client is reached ONLY
/// when the user clicks "Add watched movie" in Watched &amp; Gone to record a
/// film they watched but never kept on disk. Search → pick → details → poster.
/// </summary>
public sealed class TmdbClient : IDisposable
{
    // Shared embedded key (same one CineLibrary Essentials ships). Works out of
    // the box; a user can swap in their own via TMDb if they ever want to.
    public const string DefaultApiKey = "bbbafb01eb3938531c9270a7147fbb5f";

    private const string BaseUrl = "https://api.themoviedb.org/3";
    private const int RequestDelayMs = 250;

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _language;
    private DateTime _lastRequest = DateTime.MinValue;

    public TmdbClient(string? apiKey = null, string language = "en")
    {
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? DefaultApiKey : apiKey!;
        _language = string.IsNullOrWhiteSpace(language) ? "en" : language;

        // TMDb's CDN returns gzip even unasked; without auto-decompression the
        // raw bytes fail JSON parsing and every search silently returns nothing.
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip
                                   | DecompressionMethods.Deflate
                                   | DecompressionMethods.Brotli,
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
    }

    private string WithLanguage(string url) =>
        url.Contains("&language=") || url.Contains("?language=")
            ? url
            : $"{url}&language={_language}";

    /// <summary>Searches TMDb by title (and optional year). Returns the top matches.</summary>
    public async Task<List<TmdbMovie>> SearchMovieAsync(string title, int? year = null)
    {
        await RateLimitAsync();

        var query = Uri.EscapeDataString(title ?? string.Empty);
        var url = $"{BaseUrl}/search/movie?api_key={_apiKey}&query={query}&include_adult=false";
        if (year.HasValue && year.Value > 0)
            url += $"&primary_release_year={year}";
        url = WithLanguage(url);

        var resp = await _http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<TmdbSearchResult>(json);
        return result?.Results ?? new List<TmdbMovie>();
    }

    /// <summary>
    /// Full details for one movie — runtime, genres, studios, countries, imdb id,
    /// tagline, plus US certification parsed from the appended release_dates block.
    /// </summary>
    public async Task<TmdbMovie?> GetMovieDetailsAsync(int tmdbId)
    {
        await RateLimitAsync();

        var url = WithLanguage(
            $"{BaseUrl}/movie/{tmdbId}?api_key={_apiKey}&append_to_response=release_dates,credits");

        var resp = await _http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var movie = JsonSerializer.Deserialize<TmdbMovie>(json, options);
        if (movie == null) return null;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("release_dates", out var rd))
            movie.Certification = ParseUsCertification(rd);
        if (root.TryGetProperty("credits", out var credits))
        {
            movie.Cast = ParseCast(credits);
            ParseCrew(credits, movie);
        }

        return movie;
    }

    /// <summary>Pulls Directors (job == Director) and Writers (department ==
    /// Writing) out of credits.crew — the two crew groups Kodi/MediaElch surface.</summary>
    private static void ParseCrew(JsonElement credits, TmdbMovie movie)
    {
        if (!credits.TryGetProperty("crew", out var crew) || crew.ValueKind != JsonValueKind.Array)
            return;
        foreach (var m in crew.EnumerateArray())
        {
            var name = m.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(name)) continue;
            var job = m.TryGetProperty("job", out var j) ? j.GetString() ?? "" : "";
            var dept = m.TryGetProperty("department", out var d) ? d.GetString() ?? "" : "";

            if (string.Equals(job, "Director", StringComparison.OrdinalIgnoreCase))
            {
                if (!movie.Directors.Contains(name)) movie.Directors.Add(name);
            }
            else if (string.Equals(dept, "Writing", StringComparison.OrdinalIgnoreCase))
            {
                if (!movie.Writers.Contains(name)) movie.Writers.Add(name);
            }
        }
    }

    /// <summary>Top-billed cast (capped) from credits.cast, kept in billing order.</summary>
    private static List<TmdbCastMember> ParseCast(JsonElement credits)
    {
        var list = new List<TmdbCastMember>();
        if (!credits.TryGetProperty("cast", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var m in arr.EnumerateArray().Take(15))
        {
            list.Add(new TmdbCastMember
            {
                Name = m.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                Character = m.TryGetProperty("character", out var c) ? c.GetString() ?? "" : "",
                ProfilePath = m.TryGetProperty("profile_path", out var pp) ? pp.GetString() : null,
                Order = m.TryGetProperty("order", out var o) ? o.GetInt32() : 0,
            });
        }
        return list;
    }

    /// <summary>Builds a full image URL. "original" for the saved poster, smaller for thumbs.</summary>
    public string GetImageUrl(string? imagePath, string size = "original") =>
        string.IsNullOrEmpty(imagePath) ? string.Empty : $"https://image.tmdb.org/t/p/{size}{imagePath}";

    /// <summary>Downloads an image to <paramref name="destFullPath"/>. Returns false on any failure.</summary>
    public async Task<bool> DownloadImageAsync(string url, string destFullPath)
    {
        try
        {
            var bytes = await _http.GetByteArrayAsync(url);
            Directory.CreateDirectory(Path.GetDirectoryName(destFullPath)!);
            await File.WriteAllBytesAsync(destFullPath, bytes);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TMDb image download failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Prefers the US theatrical certification; falls back to the first non-empty one.</summary>
    private static string ParseUsCertification(JsonElement rd)
    {
        if (!rd.TryGetProperty("results", out var results)) return string.Empty;

        foreach (var region in results.EnumerateArray())
        {
            if (!region.TryGetProperty("iso_3166_1", out var iso)) continue;
            if (!string.Equals(iso.GetString(), "US", StringComparison.OrdinalIgnoreCase)) continue;
            if (!region.TryGetProperty("release_dates", out var dates)) continue;
            foreach (var d in dates.EnumerateArray())
                if (d.TryGetProperty("certification", out var cert))
                {
                    var s = cert.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s!;
                }
        }
        foreach (var region in results.EnumerateArray())
        {
            if (!region.TryGetProperty("release_dates", out var dates)) continue;
            foreach (var d in dates.EnumerateArray())
                if (d.TryGetProperty("certification", out var cert))
                {
                    var s = cert.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s!;
                }
        }
        return string.Empty;
    }

    private async Task RateLimitAsync()
    {
        var elapsed = DateTime.Now - _lastRequest;
        if (elapsed.TotalMilliseconds < RequestDelayMs)
            await Task.Delay((int)(RequestDelayMs - elapsed.TotalMilliseconds));
        _lastRequest = DateTime.Now;
    }

    public void Dispose() => _http.Dispose();
}
