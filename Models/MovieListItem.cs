using CommunityToolkit.Mvvm.ComponentModel;

namespace CineLibraryCS.Models;

/// <summary>
/// Row in the library grid/list. ObservableObject so card UI can react to
/// post-construction mutations (Watched / Favorite / Watchlist toggled
/// from anywhere) without us having to manually patch each control.
/// </summary>
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

    public string YearRuntimeText =>
        $"{Year?.ToString() ?? "—"}{(Runtime.HasValue ? $" · {Runtime}m" : "")}";

    public string RatingText =>
        Rating.HasValue ? $"★ {Rating:F1}" : "";

    public string StatusBadge =>
        IsMissing ? "MISSING" : IsOnline ? "ONLINE" : "OFFLINE";
}
