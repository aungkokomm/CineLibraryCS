using CommunityToolkit.Mvvm.ComponentModel;

namespace CineLibraryCS.Models;

/// <summary>
/// Card in the "All TV Shows" grid. Mirrors MovieListItem; watched
/// progress is a roll-up across all episodes.
/// </summary>
public partial class TvShowListItem : ObservableObject
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int? Year { get; set; }
    public double? Rating { get; set; }
    public string? LocalPoster { get; set; }
    public bool IsMissing { get; set; }
    public string VolumeSerial { get; set; } = "";
    public string? DriveLabel { get; set; }
    public bool IsOnline { get; set; }
    public int EpisodeCount { get; set; }
    public int WatchedCount { get; set; }
    public string? GenresCsv { get; set; }

    [ObservableProperty] private bool _isFavorite;
    [ObservableProperty] private bool _isWatchlist;
    [ObservableProperty] private bool _isSelected;

    public string YearText => Year?.ToString() ?? "—";
    public string RatingText => Rating.HasValue ? $"★ {Rating:F1}" : "";
    public string ProgressText => $"{WatchedCount}/{EpisodeCount}";
    public double ProgressFraction =>
        EpisodeCount > 0 ? (double)WatchedCount / EpisodeCount : 0;
    public bool FullyWatched => EpisodeCount > 0 && WatchedCount >= EpisodeCount;
    public string StatusBadge => IsMissing ? "MISSING" : IsOnline ? "ONLINE" : "OFFLINE";
}

/// <summary>A season tile inside a show — derived from tv_episodes.season.</summary>
public partial class TvSeason : ObservableObject
{
    public int ShowId { get; set; }
    public int Season { get; set; }
    public int EpisodeCount { get; set; }
    public int WatchedCount { get; set; }
    public string? PosterPath { get; set; } // show poster fallback

    public string SeasonLabel => Season == 0 ? "Specials" : $"Season {Season}";
    public string ProgressText => $"{WatchedCount}/{EpisodeCount}";
    public double ProgressFraction =>
        EpisodeCount > 0 ? (double)WatchedCount / EpisodeCount : 0;
}

/// <summary>One episode row inside a season.</summary>
public partial class TvEpisodeItem : ObservableObject
{
    public int Id { get; set; }
    public int ShowId { get; set; }
    public int Season { get; set; }
    public int Episode { get; set; }
    public string Title { get; set; } = "";
    public string? Plot { get; set; }
    public string? Aired { get; set; }
    public int? Runtime { get; set; }
    public double? Rating { get; set; }
    public string? LocalThumb { get; set; }
    public string? VideoFileRelPath { get; set; }
    public string VolumeSerial { get; set; } = "";
    public bool IsOnline { get; set; }

    [ObservableProperty] private bool _isWatched;

    public string Code => $"S{Season:D2}E{Episode:D2}";
    public string Header => $"{Code} · {Title}";
    public string RuntimeText => Runtime.HasValue ? $"{Runtime} min" : "";
    public string RatingText => Rating.HasValue ? $"★ {Rating:F1}" : "";
}

/// <summary>Full episode detail incl. stream/file info — the episode dialog.</summary>
public class TvEpisodeDetail
{
    public int Id { get; set; }
    public int ShowId { get; set; }
    public int Season { get; set; }
    public int Episode { get; set; }
    public string Title { get; set; } = "";
    public string ShowTitle { get; set; } = "";
    public string? Plot { get; set; }
    public string? Aired { get; set; }
    public double? Rating { get; set; }
    public int? Runtime { get; set; }
    public string? VideoFileRelPath { get; set; }
    public string? LocalThumb { get; set; }
    public string? SubtitleLanguages { get; set; }
    public int? VideoWidth { get; set; }
    public int? VideoHeight { get; set; }
    public string? VideoCodec { get; set; }
    public string? HdrType { get; set; }
    public string? AudioCodec { get; set; }
    public string? AudioChannels { get; set; }
    public string? AudioLanguages { get; set; }
    public int? DurationSeconds { get; set; }
    public string? ContainerExt { get; set; }
    public long? FileSizeBytes { get; set; }
    public bool IsWatched { get; set; }
    public string VolumeSerial { get; set; } = "";

    public string Code => $"S{Season:D2}E{Episode:D2}";
    public string AiredText => string.IsNullOrEmpty(Aired) ? "" : Aired;
    public string RuntimeText => Runtime.HasValue ? $"{Runtime} min" : "";
    public string RatingText => Rating.HasValue ? $"★ {Rating:F1}" : "";

    /// <summary>e.g. "1080p", "2160p (4K)", "720p" from the height.</summary>
    public string? Resolution
    {
        get
        {
            if (!VideoHeight.HasValue) return null;
            int h = VideoHeight.Value;
            return h >= 2000 ? "2160p (4K)" : h >= 1060 ? "1080p" : h >= 700 ? "720p"
                 : h >= 570 ? "576p" : h >= 470 ? "480p" : $"{h}p";
        }
    }
    public string FileSizeText
    {
        get
        {
            if (!FileSizeBytes.HasValue) return "";
            double b = FileSizeBytes.Value;
            return b >= 1L<<30 ? $"{b/(1L<<30):F2} GB" : b >= 1L<<20 ? $"{b/(1L<<20):F0} MB" : $"{b/(1L<<10):F0} KB";
        }
    }
    public string DurationText
    {
        get
        {
            if (!DurationSeconds.HasValue || DurationSeconds.Value <= 0) return "";
            var t = TimeSpan.FromSeconds(DurationSeconds.Value);
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:D2}m" : $"{t.Minutes}m {t.Seconds:D2}s";
        }
    }
}

/// <summary>Full show detail for the show header (poster, plot, cast).</summary>
public class TvShowDetail
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int? Year { get; set; }
    public double? Rating { get; set; }
    public string? Plot { get; set; }
    public string? Mpaa { get; set; }
    public string? Studio { get; set; }
    public string? Status { get; set; }
    public string? Premiered { get; set; }
    public string? ImdbId { get; set; }
    public string? TmdbId { get; set; }
    public string? LocalPoster { get; set; }
    public string? LocalFanart { get; set; }
    public string VolumeSerial { get; set; } = "";
    public string? FolderRelPath { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsWatchlist { get; set; }
    public string? Note { get; set; }
    public List<string> Genres { get; set; } = new();
    public List<Actor> Actors { get; set; } = new();
    public int EpisodeCount { get; set; }
    public int WatchedCount { get; set; }
}
