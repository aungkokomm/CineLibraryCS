using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CineLibraryCS.Models;

public partial class Actor : ObservableObject
{
    public string Name { get; set; } = "";
    public string? Role { get; set; }
    public string? Thumb { get; set; }
    public int SortOrder { get; set; }
    // Loaded lazily by the detail dialog if Thumb is a valid http(s) URL or
    // local path. Bound x:Bind OneWay so the avatar appears once decoded.
    [ObservableProperty] private BitmapImage? _thumbBitmap;
}

public class MovieDetail
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? OriginalTitle { get; set; }
    public int? Year { get; set; }
    public double? Rating { get; set; }
    public int? Runtime { get; set; }
    public string? Plot { get; set; }
    public string? Outline { get; set; }
    public string? Tagline { get; set; }
    public string? Mpaa { get; set; }
    public string? ImdbId { get; set; }
    public string? TmdbId { get; set; }
    public string? Premiered { get; set; }
    public string? Studio { get; set; }
    public string? Country { get; set; }
    public string? LocalPoster { get; set; }
    public string? LocalFanart { get; set; }
    public bool IsMissing { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsWatched { get; set; }
    public bool IsWatchlist { get; set; }
    public bool IsOnline { get; set; }
    public bool Playable { get; set; }
    public string? DriveLabel { get; set; }
    public string? CurrentLetter { get; set; }
    public string VolumeSerial { get; set; } = "";
    public string? FolderRelPath { get; set; }
    public string? VideoFileRelPath { get; set; }
    public string? Note { get; set; }
    // v2.2 — trailer + stream details + file info
    public string? Trailer { get; set; }
    public int? VideoWidth { get; set; }
    public int? VideoHeight { get; set; }
    public string? VideoCodec { get; set; }
    public string? VideoAspect { get; set; }
    public string? HdrType { get; set; }
    public string? AudioCodec { get; set; }
    public string? AudioChannels { get; set; }
    public string? AudioLanguages { get; set; }
    public string? SubtitleLanguages { get; set; }
    public int? DurationSeconds { get; set; }
    public string? ContainerExt { get; set; }
    public long? FileSizeBytes { get; set; }

    public List<string> Genres { get; set; } = new();
    public List<string> Directors { get; set; } = new();
    public List<string> Writers { get; set; } = new();
    public List<Actor> Actors { get; set; } = new();
    public List<string> Sets { get; set; } = new();
    public List<(string Source, double Value, int? Votes)> AllRatings { get; set; } = new();

    public string RatingText => Rating.HasValue ? $"★ {Rating:F1}" : "";
    public string RuntimeText => Runtime.HasValue ? $"{Runtime} min" : "";
    public string ImdbUrl => ImdbId != null ? $"https://www.imdb.com/title/{ImdbId}/" : "";
}
