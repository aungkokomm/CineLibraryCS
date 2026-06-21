using System.Text.Json.Serialization;

namespace CineLibraryCS.Services.Tmdb;

// ─────────────────────────────────────────────────────────────────────────────
//  Movie-only TMDb models, ported (trimmed) from CineLibrary Essentials.
//  CineLibrary stays an offline browser; the ONLY place these are used is the
//  "Add watched movie" dialog, which lets the user record a film they watched
//  but never had on disk straight into Watched & Gone. TV / cast / crew fields
//  from the original Essentials model are intentionally dropped — we only need
//  enough to build a Watched & Gone record card.
// ─────────────────────────────────────────────────────────────────────────────

public class TmdbMovie
{
    [JsonPropertyName("id")]
    public int TmdbId { get; set; }

    [JsonPropertyName("imdb_id")]
    public string? ImdbId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("original_title")]
    public string OriginalTitle { get; set; } = string.Empty;

    [JsonPropertyName("tagline")]
    public string Tagline { get; set; } = string.Empty;

    [JsonPropertyName("release_date")]
    public string ReleaseDate { get; set; } = string.Empty;

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("vote_average")]
    public double Rating { get; set; }

    [JsonPropertyName("vote_count")]
    public int VoteCount { get; set; }

    [JsonPropertyName("runtime")]
    public int Runtime { get; set; }

    [JsonPropertyName("genres")]
    public List<TmdbNamed> Genres { get; set; } = new();

    [JsonPropertyName("production_countries")]
    public List<TmdbCountry> ProductionCountries { get; set; } = new();

    [JsonPropertyName("production_companies")]
    public List<TmdbNamed> ProductionCompanies { get; set; } = new();

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("backdrop_path")]
    public string? BackdropPath { get; set; }

    /// <summary>MPAA certification (e.g. "PG-13") for the US region — parsed
    /// client-side from the appended release_dates block. Empty if none.</summary>
    public string Certification { get; set; } = string.Empty;

    /// <summary>Top-billed cast, parsed client-side from the appended credits
    /// block (sorted by billing order). Empty until details are fetched.</summary>
    public List<TmdbCastMember> Cast { get; set; } = new();

    /// <summary>Director names, parsed client-side from credits.crew.</summary>
    public List<string> Directors { get; set; } = new();

    /// <summary>Writer names, parsed client-side from credits.crew.</summary>
    public List<string> Writers { get; set; } = new();

    /// <summary>Release year derived from <see cref="ReleaseDate"/>, or 0.</summary>
    public int Year => !string.IsNullOrEmpty(ReleaseDate) && DateTime.TryParse(ReleaseDate, out var d)
        ? d.Year
        : 0;
}

public class TmdbCastMember
{
    public string Name { get; set; } = string.Empty;
    public string Character { get; set; } = string.Empty;
    public string? ProfilePath { get; set; }
    public int Order { get; set; }
}

public class TmdbNamed
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class TmdbCountry
{
    [JsonPropertyName("iso_3166_1")]
    public string IsoCode { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class TmdbSearchResult
{
    [JsonPropertyName("results")]
    public List<TmdbMovie> Results { get; set; } = new();

    [JsonPropertyName("total_results")]
    public int TotalResults { get; set; }
}
