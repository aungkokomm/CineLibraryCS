using System.Text.Json;

namespace CineLibraryCS.Services;

/// <summary>
/// v4.6.0: IMDb rating + votes by IMDb id ("tt0111161"), ported from
/// CineLibrary Essentials' ImdbRatingService. IMDb's own API is paid, so this
/// uses Agregarr's free, open-source proxy of IMDb's official daily
/// title.ratings dataset (no key): https://agregarr.org/docs/imdb-ratings/
/// Information courtesy of IMDb (imdb.com), used with permission.
/// </summary>
public static class ImdbRatingClient
{
    private const string Endpoint = "https://api.agregarr.org/api/ratings";
    private const int MaxIdsPerRequest = 100;   // the proxy's documented batch limit

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public record Rating(double Value, int Votes);

    /// <summary>
    /// Ratings for many titles, 100 per request. Titles IMDb has no rating for
    /// are missing from the result; a request that fails leaves its titles out
    /// too (<paramref name="failedBatches"/> counts them). <paramref name="progress"/>
    /// gets how many ids have been asked about so far.
    /// </summary>
    public static async Task<(Dictionary<string, Rating> Ratings, int FailedBatches)> GetRatingsAsync(
        IEnumerable<string> imdbIds, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var result = new Dictionary<string, Rating>(StringComparer.OrdinalIgnoreCase);
        var ids = imdbIds.Where(IsImdbId).Select(id => id.Trim())
                         .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        int asked = 0, failed = 0;
        foreach (var batch in ids.Chunk(MaxIdsPerRequest))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var url = Endpoint + "?" + string.Join("&", batch.Select(id => "id=" + Uri.EscapeDataString(id)));
                foreach (var (id, rating) in ParseResponse(await Http.GetStringAsync(url, ct)))
                    result[id] = rating;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                failed++;
                System.Diagnostics.Debug.WriteLine($"IMDb ratings request failed: {ex.Message}");
            }
            asked += batch.Length;
            progress?.Report(asked);
        }
        return (result, failed);
    }

    /// <summary>[{"imdbId":"tt…","rating":9.3,"votes":3252447}, …]; no or zero ratings are skipped.</summary>
    public static List<(string ImdbId, Rating Rating)> ParseResponse(string json)
    {
        var list = new List<(string, Rating)>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (!el.TryGetProperty("imdbId", out var idEl) || idEl.ValueKind != JsonValueKind.String) continue;
                if (!el.TryGetProperty("rating", out var rEl) || rEl.ValueKind != JsonValueKind.Number) continue;
                var value = rEl.GetDouble();
                if (value <= 0) continue;
                var votes = el.TryGetProperty("votes", out var vEl) && vEl.ValueKind == JsonValueKind.Number
                    ? vEl.GetInt32() : 0;
                list.Add((idEl.GetString()!, new Rating(value, votes)));
            }
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"IMDb ratings response unreadable: {ex.Message}");
        }
        return list;
    }

    /// <summary>True for strings shaped like an IMDb title id ("tt" + digits).</summary>
    public static bool IsImdbId(string? id)
    {
        var s = id?.Trim();
        return s is { Length: >= 9 } && s.StartsWith("tt", StringComparison.OrdinalIgnoreCase) && s[2..].All(char.IsDigit);
    }
}
