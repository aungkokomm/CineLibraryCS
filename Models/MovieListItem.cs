using CommunityToolkit.Mvvm.ComponentModel;

namespace CineLibraryCS.Models;

/// <summary>
/// Row in the library grid/list. ObservableObject so card UI can react to
/// post-construction mutations (Watched / Favorite / Watchlist toggled
/// from anywhere) without us having to manually patch each control.
/// </summary>
/// <summary>v4.10.0: "1h 12m left", on Continue Watching and the pause screen.</summary>
public static class TimeLeft
{
    public static string Text(long ms)
    {
        var minutes = (long)Math.Ceiling(Math.Max(0, ms) / 60_000.0);
        return minutes >= 60 ? $"{minutes / 60}h {minutes % 60}m left" : minutes > 0 ? $"{minutes} min left" : "Almost done";
    }

    public static double Share(long ms, long length) => ms > 0 && length > 0 ? Math.Clamp(ms / (double)length, 0, 1) : 0;
}

public partial class MovieListItem : ObservableObject
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int? Year { get; set; }
    public double? Rating { get; set; }
    public int? Runtime { get; set; }
    public string? LocalPoster { get; set; }
    public bool IsMissing { get; set; }
    public string VolumeSerial { get; set; } = "";
    public string? DriveLabel { get; set; }
    public string? GenresCsv { get; set; }
    public bool IsOnline { get; set; }
    // v4.8.0: for the card's corner tags (New, 4K, HDR). Unix seconds, as stored.
    public long? DateAdded { get; set; }
    // 4.10.0: when it was last played (unix seconds, 0 = never), for Continue Watching's groups
    public long LastPlayed { get; set; }
    public int? VideoWidth { get; set; }
    public int? VideoHeight { get; set; }
    public string? HdrType { get; set; }
    // v4.8.0: the sort All movies listed it under ("runtime", "rating", ...), or
    // "recently_added" on Recently Added; the card follows it. Null elsewhere.
    public string? ListedBy { get; set; }

    [ObservableProperty] private bool _isFavorite;
    [ObservableProperty] private bool _isWatched;
    [ObservableProperty] private bool _isWatchlist;
    // v2.5 — multi-select state. The card draws a purple outline + ✓ corner
    // chip when this is true. LibraryPage owns the source-of-truth list of
    // selected items; this property is bound one-way for the card UI.
    [ObservableProperty] private bool _isSelected;
    // v4.10.0: where the built-in player stopped, 0 when it's not part-way
    [ObservableProperty] private long _resumeMs;

    /// <summary>How far it was watched, 0 to 1; 0 when its length isn't known.</summary>
    public double ResumeProgress => TimeLeft.Share(ResumeMs, Runtime is int r ? r * 60_000L : 0);

    public string ResumeText => ResumeProgress > 0 ? TimeLeft.Text(Runtime!.Value * 60_000L - ResumeMs) : "";

    public string YearRuntimeText =>
        $"{Year?.ToString() ?? "—"}{(Runtime.HasValue ? $" · {Runtime}m" : "")}";

    public string RatingText =>
        Rating.HasValue ? $"★ {Rating:F1}" : "";

    public string StatusBadge =>
        IsMissing ? "MISSING" : IsOnline ? "ONLINE" : "OFFLINE";
}
