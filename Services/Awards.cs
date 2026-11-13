using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CineLibraryCS.Services;

/// <summary>
/// 5.4.1: awards and nominations, from Wikidata (TMDb has none): a person's by their
/// IMDb id, a film's by its IMDb id (the film's own, and the ones people won for it).
/// Kept in the database for a month (awards_wikidata), so a page opened again shows them
/// at once, offline too.
/// </summary>
public static class Awards
{
    public sealed class Award
    {
        public bool Won { get; set; }
        /// <summary>"Academy Award for Best Director"</summary>
        public string Name { get; set; } = "";
        public int? Year { get; set; }
        /// <summary>The film a person's award was for.</summary>
        public string? Work { get; set; }
        /// <summary>Who won a film's award ("Cillian Murphy"), when it went to someone.</summary>
        public string? Who { get; set; }
    }

    private const string Endpoint = "https://query.wikidata.org/sparql";
    private const long RefreshAfter = 30L * 24 * 3600;   // seconds
    private static readonly Regex ImdbId = new(@"^(nm|tt)\d{5,10}$");
    private static readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly HttpClient Http = MakeClient();

    private static HttpClient MakeClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CineLibrary/5.4 (desktop movie library)");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/sparql-results+json");
        return http;
    }

    /// <summary>A person's awards; null when Wikidata can't be reached and none are kept.</summary>
    public static Task<List<Award>?> ForPersonAsync(string? imdbId) => GetAsync(imdbId, "nm", id => $$"""
        SELECT DISTINCT ?kind ?awardLabel ?year ?workLabel WHERE {
          ?person wdt:P345 "{{id}}" .
          { ?person p:P166 ?st . ?st ps:P166 ?award . BIND("won" AS ?kind) }
          UNION { ?person p:P1411 ?st . ?st ps:P1411 ?award . BIND("nominated" AS ?kind) }
          OPTIONAL { ?st pq:P585 ?date . BIND(YEAR(?date) AS ?year) }
          OPTIONAL { ?st pq:P1686 ?work }
          SERVICE wikibase:label { bd:serviceParam wikibase:language "en,mul". }
        }
        """);

    /// <summary>A film's awards, its own and the ones people won for it; null as above.</summary>
    public static Task<List<Award>?> ForMovieAsync(string? imdbId) => GetAsync(imdbId, "tt", id => $$"""
        SELECT DISTINCT ?kind ?awardLabel ?year ?whoLabel WHERE {
          ?film wdt:P345 "{{id}}" .
          { ?film p:P166 ?st . ?st ps:P166 ?award . BIND("won" AS ?kind) }
          UNION { ?film p:P1411 ?st . ?st ps:P1411 ?award . BIND("nominated" AS ?kind) }
          UNION { ?st pq:P1686 ?film . ?who p:P166 ?st . ?st ps:P166 ?award . BIND("won" AS ?kind) }
          UNION { ?st pq:P1686 ?film . ?who p:P1411 ?st . ?st ps:P1411 ?award . BIND("nominated" AS ?kind) }
          OPTIONAL { ?st pq:P585 ?date . BIND(YEAR(?date) AS ?year) }
          SERVICE wikibase:label { bd:serviceParam wikibase:language "en,mul". }
        }
        """);

    private static async Task<List<Award>?> GetAsync(string? imdbId, string prefix, Func<string, string> query)
    {
        var id = imdbId?.Trim() ?? "";
        if (!ImdbId.IsMatch(id) || !id.StartsWith(prefix)) return new();
        var db = AppState.Instance.Db;
        var cached = db.GetAwardsCache(id);
        if (cached is { } c && DateTimeOffset.UtcNow.ToUnixTimeSeconds() - c.FetchedAt < RefreshAfter) return Read(c.Data);
        await _gate.WaitAsync();
        try
        {
            var json = await Http.GetStringAsync($"{Endpoint}?query={Uri.EscapeDataString(query(id))}");
            var list = Parse(json);
            db.SaveAwardsCache(id, JsonSerializer.Serialize(list));
            return list;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Awards lookup failed: {ex.Message}");
            return cached is { } old ? Read(old.Data) : null;
        }
        finally { _gate.Release(); }
    }

    private static List<Award> Read(string json)
    {
        try { return JsonSerializer.Deserialize<List<Award>>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    /// <summary>
    /// Wikidata's answer as awards: one line per award, year and win or nomination (the names
    /// of everyone who shared it together), a nomination left out when it was won, the
    /// latest first and the wins before the nominations.
    /// </summary>
    public static List<Award> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var raw = new List<Award>();
        foreach (var b in doc.RootElement.GetProperty("results").GetProperty("bindings").EnumerateArray())
        {
            string? Val(string name) => b.TryGetProperty(name, out var v) && v.TryGetProperty("value", out var s) && s.GetString() is { Length: > 0 } t ? t : null;
            var name = Val("awardLabel");
            if (name == null || IsEntityId(name)) continue;   // no English name
            raw.Add(new Award
            {
                Won = Val("kind") == "won",
                Name = name,
                Year = int.TryParse(Val("year"), out var y) ? y : null,
                Work = Val("workLabel") is { } w && !IsEntityId(w) ? w : null,
                Who = Val("whoLabel") is { } p && !IsEntityId(p) ? p : null,
            });
        }
        var merged = raw.GroupBy(a => (a.Won, a.Name, a.Year)).Select(g => new Award
        {
            Won = g.Key.Won, Name = g.Key.Name, Year = g.Key.Year,
            Work = g.Select(a => a.Work).FirstOrDefault(w => w != null),
            Who = g.Select(a => a.Who).Where(w => w != null).Distinct().ToList() is { Count: > 0 } who ? string.Join(", ", who) : null,
        }).ToList();
        var won = merged.Where(a => a.Won).Select(a => (a.Name, a.Year)).ToHashSet();
        return merged.Where(a => a.Won || !won.Contains((a.Name, a.Year)))
                     .OrderByDescending(a => a.Year ?? 0).ThenByDescending(a => a.Won).ThenBy(a => a.Name).ToList();
    }

    private static bool IsEntityId(string s) => s.Length > 1 && s[0] == 'Q' && s.Skip(1).All(char.IsDigit);

    // The best known first; the rest by how many wins, then how many lines
    private static readonly string[] Ranked =
    {
        "Academy Award", "Golden Globe Award", "BAFTA Award", "Screen Actors Guild Award", "Primetime Emmy Award",
        "Palme d'Or", "Cannes", "Golden Lion", "Venice", "Golden Bear", "Berlin", "National Film Award",
        "Filmfare Award", "César Award", "Critics' Choice", "Independent Spirit Award",
    };

    /// <summary>"Academy Award" from "Academy Award for Best Director".</summary>
    public static string Family(Award a) => a.Name.IndexOf(" for ", StringComparison.Ordinal) is > 0 and var at ? a.Name[..at] : a.Name;

    /// <summary>"Best Director" from "Academy Award for Best Director"; empty when the name is only the award.</summary>
    public static string What(Award a) => a.Name.IndexOf(" for ", StringComparison.Ordinal) is > 0 and var at ? a.Name[(at + 5)..] : "";

    public static List<(string Family, List<Award> Items)> Group(IEnumerable<Award> awards)
    {
        static int Rank(string family)
        {
            for (int i = 0; i < Ranked.Length; i++)
                if (family.StartsWith(Ranked[i], StringComparison.OrdinalIgnoreCase) || family.Contains(Ranked[i], StringComparison.OrdinalIgnoreCase))
                    return i;
            return Ranked.Length;
        }
        return awards.GroupBy(Family)
            .Select(g => (Family: g.Key, Items: g.OrderByDescending(a => a.Won).ThenByDescending(a => a.Year ?? 0).ToList()))
            .OrderBy(g => Rank(g.Family)).ThenByDescending(g => g.Items.Count(a => a.Won)).ThenByDescending(g => g.Items.Count)
            .ThenBy(g => g.Family).ToList();
    }

    /// <summary>"7 Oscars · 27 wins · 14 nominations" (the parts there are); empty for none.</summary>
    public static string Summary(IReadOnlyCollection<Award> awards)
    {
        var wins = awards.Count(a => a.Won);
        var noms = awards.Count - wins;
        var oscars = awards.Count(a => a.Won && Family(a) == "Academy Award");
        var parts = new List<string>();
        if (oscars > 0) parts.Add(oscars == 1 ? "1 Oscar" : $"{oscars} Oscars");
        if (wins > 0) parts.Add(wins == 1 ? "1 win" : $"{wins} wins");
        if (noms > 0) parts.Add(noms == 1 ? "1 nomination" : $"{noms} nominations");
        return string.Join(" · ", parts);
    }
}
