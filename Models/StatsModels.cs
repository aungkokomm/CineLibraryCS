namespace CineLibraryCS.Models;

/// <summary>
/// v4.8.0: everything the Statistics page shows, read in one go. Movie counts
/// are the library's (not Watched &amp; Gone); the breakdowns leave out missing
/// movies, as the Browse pages do, so a click shows the movies counted.
/// </summary>
public class DashboardStats
{
    public int Movies { get; set; }
    public int Missing { get; set; }
    public int Archived { get; set; }
    public int Watched { get; set; }
    public int Watchlist { get; set; }
    public long RuntimeMinutes { get; set; }
    public int Shows { get; set; }
    public int Episodes { get; set; }
    public int EpisodesWatched { get; set; }

    public List<DriveUse> Drives { get; set; } = new();
    public List<StatCount> Quality { get; set; } = new();        // 4K, 1080p, 720p, SD, Unknown
    public List<StatCount> VideoFormats { get; set; } = new();
    public List<StatCount> Channels { get; set; } = new();
    public List<StatCount> AudioFormats { get; set; } = new();

    public List<DecadeCount> Decades { get; set; } = new();
    public List<StatCount> RatingBands { get; set; } = new();    // Key: the Browse page's band key
    public List<StatCount> Lengths { get; set; } = new();
    public int AverageLength { get; set; }                       // minutes
    public List<StatCount> Genres { get; set; } = new();
    public List<StatCount> Countries { get; set; } = new();
    public List<StatCount> Certifications { get; set; } = new();
    public int Certified { get; set; }
    public List<StatCount> Collections { get; set; } = new();    // Key: the collection's id
    public int CollectionCount { get; set; }
    public int MoviesInCollections { get; set; }

    // v4.8.0 (#25): the TV shows' own section
    public long TvRuntimeMinutes { get; set; }
    public long TvBytes { get; set; }
    public List<StatCount> TvProgress { get; set; } = new();     // Finished, In progress, Not started
    public List<StatCount> TvStatus { get; set; } = new();
    public List<StatCount> TvGenres { get; set; } = new();
    public List<StatCount> TvStudios { get; set; } = new();
    public List<ShowCount> TvBiggest { get; set; } = new();
    public List<StatCount> TvQuality { get; set; } = new();      // 4K, 1080p, 720p, SD, Unknown

    public List<MonthCount> Months { get; set; } = new();
    public List<PersonCount> Actors { get; set; } = new();
    public List<PersonCount> Directors { get; set; } = new();
    public List<MovieListItem> RecentlyWatched { get; set; } = new();
    public List<MovieListItem> WorthWatching { get; set; } = new();

    public long Bytes => Drives.Sum(d => d.Bytes);
    public int DrivesUsed => Drives.Count(d => d.Movies + d.Shows > 0);
}

public record StatCount(string Label, int Count, string? Key = null);
public record DriveUse(string Serial, string Label, int Movies, int Shows, long Bytes);
public record DecadeCount(int Decade, int Count, double? AverageRating);
public record MonthCount(DateTime Month, int Movies, int Episodes);
public record PersonCount(string Name, string? Thumb, int Movies, int Watched);
public record ShowCount(int Id, string Title, int Episodes, int Watched);
