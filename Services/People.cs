using System.Globalization;
using System.Text;
using System.Text.Json;
using CineLibraryCS.Models;
using CineLibraryCS.Services.Tmdb;

namespace CineLibraryCS.Services;

/// <summary>
/// 5.4.0: who an actor or director is, from TMDb: their biography, dates and
/// films, shown above their films in the library. TMDb knows people by its own
/// id, which the .nfo files don't carry, so the person is found in the TMDb cast
/// list of one of their titles in the library. The answer is kept in the
/// database (people_tmdb), so a person opened again shows at once, offline too,
/// and is asked again after a month.
/// </summary>
public static class People
{
    private const long RefreshAfter = 30L * 24 * 3600;   // seconds
    private static readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// The person by the name in the library. Info is null when TMDb doesn't know
    /// them; Offline is true when TMDb couldn't be reached and nothing is kept.
    /// </summary>
    public static async Task<(PersonInfo? Info, bool Offline)> GetAsync(string name)
    {
        var db = AppState.Instance.Db;
        await _gate.WaitAsync();
        try
        {
            var cached = db.GetPersonCache(name);
            if (cached != null && DateTimeOffset.UtcNow.ToUnixTimeSeconds() - cached.FetchedAt < RefreshAfter)
                return (cached.Info, false);
            try
            {
                using var client = new TmdbClient();
                var id = await FindIdAsync(client, db, name);
                var info = id > 0 ? Parse(await client.GetPersonJsonAsync(id)) : null;
                if (info?.ProfilePath is { Length: > 0 } profile && !File.Exists(PhotoPath(info.TmdbId)))
                    await client.DownloadImageAsync(client.GetImageUrl(profile, "w185"), PhotoPath(info.TmdbId));
                db.SavePersonCache(name, info);
                return (info, false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Person lookup failed: {ex.Message}");
                return (cached?.Info, cached == null);
            }
        }
        finally { _gate.Release(); }
    }

    /// <summary>Where the person's TMDb photo is kept.</summary>
    public static string PhotoPath(int tmdbId) => Path.Combine(AppState.Instance.DataDir, "people", $"{tmdbId}.jpg");

    /// <summary>
    /// The person's TMDb id: from the cast or crew of a few of their titles in the
    /// library, else TMDb's people search when the name is an exact match. 0 if none.
    /// </summary>
    private static async Task<int> FindIdAsync(TmdbClient client, DatabaseService db, string name)
    {
        foreach (var (show, tmdbText, imdb) in db.GetPersonTitleIds(name))
        {
            int tmdb = int.TryParse(tmdbText, out var t) ? t : 0;
            if (tmdb <= 0 && !show && !string.IsNullOrWhiteSpace(imdb))
                tmdb = await client.FindMovieByImdbAsync(imdb.Trim()) ?? 0;
            if (tmdb <= 0) continue;
            if (await client.GetCreditsJsonAsync(tmdb, show) is { } credits && FindInList(credits, name, "cast", "crew") is > 0 and var id)
                return id;
        }
        return FindInList(await client.SearchPersonJsonAsync(name), name, "results");
    }

    /// <summary>The id of the first person called <paramref name="name"/> in these arrays of a TMDb answer.</summary>
    public static int FindInList(string json, string name, params string[] lists)
    {
        var key = NameKey(name);
        if (key.Length == 0) return 0;
        using var doc = JsonDocument.Parse(json);
        foreach (var list in lists)
            if (doc.RootElement.TryGetProperty(list, out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var e in arr.EnumerateArray())
                    if ((NameKey(Str(e, "name")) == key || NameKey(Str(e, "original_name")) == key) && Int(e, "id") is > 0 and var id)
                        return id;
        return 0;
    }

    /// <summary>A name for matching: no accents, case, dots or extra spaces ("Robert Downey Jr." = "robert downey jr").</summary>
    public static string NameKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var sb = new StringBuilder();
        foreach (var c in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            else if (char.IsWhiteSpace(c) || c == '-') { if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' '); }
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Reads TMDb's /person answer (with combined_credits and external_ids). Null when it isn't one.</summary>
    public static PersonInfo? Parse(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var p = new PersonInfo
        {
            TmdbId = Int(r, "id"),
            Name = Str(r, "name") ?? "",
            Biography = (Str(r, "biography") ?? "").Trim(),
            Birthday = Str(r, "birthday"),
            Deathday = Str(r, "deathday"),
            PlaceOfBirth = Str(r, "place_of_birth")?.Trim(),
            ProfilePath = Str(r, "profile_path"),
            ImdbId = Str(r, "imdb_id") ?? (r.TryGetProperty("external_ids", out var ext) ? Str(ext, "imdb_id") : null),
        };
        if (r.TryGetProperty("combined_credits", out var cc))
        {
            p.Acted = Films(cc, "cast", e => !AsThemselves(e)).OrderByDescending(f => f.Votes).Take(60).ToList();
            p.Directed = Films(cc, "crew", e => Str(e, "job") == "Director").ToList();
        }
        return p.TmdbId > 0 ? p : null;
    }

    /// <summary>A documentary, or a role as themselves ("Self", "Himself (archive footage)"):
    /// making-ofs and interviews, not the films an actor is known for.</summary>
    private static bool AsThemselves(JsonElement e)
    {
        if (e.TryGetProperty("genre_ids", out var genres) && genres.ValueKind == JsonValueKind.Array
            && genres.EnumerateArray().Any(g => g.ValueKind == JsonValueKind.Number && g.GetInt32() == 99))
            return true;
        var role = (Str(e, "character") ?? "").TrimStart();
        return new[] { "Self", "Himself", "Herself", "Themselves" }.Any(w =>
            role.StartsWith(w, StringComparison.OrdinalIgnoreCase)
            && (role.Length == w.Length || !char.IsLetter(role[w.Length])));
    }

    private static IEnumerable<PersonFilm> Films(JsonElement credits, string list, Func<JsonElement, bool> keep)
    {
        if (!credits.TryGetProperty(list, out var arr) || arr.ValueKind != JsonValueKind.Array) yield break;
        var seen = new HashSet<int>();
        foreach (var e in arr.EnumerateArray())
        {
            if (Str(e, "media_type") != "movie" || !keep(e)) continue;
            if (e.TryGetProperty("adult", out var adult) && adult.ValueKind == JsonValueKind.True) continue;
            var id = Int(e, "id");
            if (id <= 0 || !seen.Add(id)) continue;
            yield return new PersonFilm
            {
                TmdbId = id,
                Title = Str(e, "title") ?? "",
                OriginalTitle = Str(e, "original_title"),
                ReleaseDate = Str(e, "release_date"),
                PosterPath = Str(e, "poster_path"),
                Votes = Int(e, "vote_count"),
            };
        }
    }

    /// <summary>The films for the person's "not in your library" cards: a director's every film
    /// you don't have and haven't let go (Watched &amp; Gone), the newest and the ones still to come
    /// first; an actor's 20 best-known released ones. With how many they directed, how many of
    /// them are in the library and how many in Watched &amp; Gone.</summary>
    public static (List<PersonFilm> Missing, int Total, int Owned, int Gone) Strip(PersonInfo p, bool director,
        OwnedMovies owned, OwnedMovies gone, DateTime today)
    {
        var films = director ? p.Directed : p.Acted;
        var have = films.Count(owned.Has);
        var had = films.Count(f => !owned.Has(f) && gone.Has(f));
        var missing = films.Where(f => !owned.Has(f) && !gone.Has(f));
        missing = director
            ? missing.OrderByDescending(f => f.ReleaseDate ?? "9999")
            : missing.Where(f => Date(f.ReleaseDate) is { } d && d <= today).OrderByDescending(f => f.Votes).Take(20);
        return (missing.ToList(), films.Count, have, had);
    }

    /// <summary>"2014", "Coming 2027", or "Coming" when TMDb has no date yet.</summary>
    public static string YearText(PersonFilm f, DateTime today)
        => Date(f.ReleaseDate) is not { } d ? "Coming"
         : d > today ? $"Coming {d.Year}"
         : d.Year.ToString(CultureInfo.InvariantCulture);

    /// <summary>The person's facts, each with its kind for its colour: born ("9 July 1956"),
    /// age ("70", while living), died ("12 March 2020, aged 80") and place (where they were born).</summary>
    public static List<(string Kind, string Label, string Value)> Facts(PersonInfo p, DateTime today)
    {
        var born = Date(p.Birthday);
        var died = Date(p.Deathday);
        var facts = new List<(string, string, string)>();
        if (born is { } b)
        {
            facts.Add(("born", "Born", Day(b)));
            if (died == null) facts.Add(("age", "Age", Age(b, today).ToString(CultureInfo.InvariantCulture)));
        }
        if (died is { } d)
            facts.Add(("died", "Died", born is { } b2 ? $"{Day(d)}, aged {Age(b2, d)}" : Day(d)));
        if (!string.IsNullOrWhiteSpace(p.PlaceOfBirth))
            facts.Add(("place", "Birthplace", p.PlaceOfBirth));
        return facts;
    }

    /// <summary>5.4.1: whether the film is out (a date that has come).</summary>
    public static bool IsReleased(PersonFilm f, DateTime today) => Date(f.ReleaseDate) is { } d && d <= today;

    /// <summary>5.4.1: the year they were born, for their age in each film.</summary>
    public static int? BirthYear(PersonInfo p) => Date(p.Birthday)?.Year;

    /// <summary>
    /// 5.4.1: the people born on this day of the year, the oldest first. Someone born on
    /// 29 February is on the 28th in the years without one.
    /// </summary>
    public static List<(string Name, PersonInfo Info)> BornOn(IEnumerable<(string Name, PersonInfo Info)> people, DateTime day)
    {
        bool Matches(DateTime b) => (b.Month, b.Day) == (day.Month, day.Day)
            || (b.Month == 2 && b.Day == 29 && day.Month == 2 && day.Day == 28 && !DateTime.IsLeapYear(day.Year));
        return people.Where(p => Date(p.Info.Birthday) is { } b && Matches(b))
                     .OrderBy(p => p.Info.Birthday).ToList();
    }

    /// <summary>5.4.1: "1956 · 70 today", or for someone who has died "1930 · would have been 96".</summary>
    public static string BirthdayLine(PersonInfo p, DateTime day)
    {
        if (Date(p.Birthday) is not { } b) return "";
        var years = day.Year - b.Year;
        return Date(p.Deathday) != null ? $"{b.Year} · would have been {years}" : $"{b.Year} · {years} today";
    }

    private static string Day(DateTime d) => d.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

    private static int Age(DateTime born, DateTime on)
        => on.Year - born.Year - (on.Month < born.Month || on.Month == born.Month && on.Day < born.Day ? 1 : 0);

    private static DateTime? Date(string? s)
        => DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    private static int Int(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;
}

/// <summary>5.4.0: the movies in the library (or in Watched &amp; Gone), to tell which of a
/// person's films you have: by TMDb id, else by title and year (a year either side, as
/// release years differ).</summary>
public sealed class OwnedMovies
{
    private readonly Dictionary<int, int> _byTmdb = new();      // TMDb id -> movie row
    private readonly Dictionary<string, int> _byTitle = new();  // "title|year" -> movie row

    public OwnedMovies(IEnumerable<(int Id, string? TmdbId, string? Title, string? OriginalTitle, int? Year)> movies)
    {
        foreach (var (row, tmdb, title, original, year) in movies)
        {
            if (int.TryParse(tmdb, out var id) && id > 0) _byTmdb.TryAdd(id, row);
            if (year is not > 0) continue;
            foreach (var t in new[] { title, original })
                if (People.NameKey(t) is { Length: > 0 } key) _byTitle.TryAdd($"{key}|{year}", row);
        }
    }

    public bool Has(PersonFilm f) => Find(f) != null;

    /// <summary>The movie row that is this film, or null.</summary>
    public int? Find(PersonFilm f)
    {
        if (_byTmdb.TryGetValue(f.TmdbId, out var row)) return row;
        if (f.ReleaseDate is not { Length: >= 4 } date || !int.TryParse(date[..4], out var year)) return null;
        foreach (var t in new[] { f.Title, f.OriginalTitle })
            if (People.NameKey(t) is { Length: > 0 } key)
                for (var y = year - 1; y <= year + 1; y++)
                    if (_byTitle.TryGetValue($"{key}|{y}", out row)) return row;
        return null;
    }
}
