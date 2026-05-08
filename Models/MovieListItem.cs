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

    [ObservableProperty] private bool _isFavorite;
    [ObservableProperty] private bool _isWatched;
    [ObservableProperty] private bool _isWatchlist;

    public string YearRuntimeText =>
        $"{Year?.ToString() ?? "—"}{(Runtime.HasValue ? $" · {Runtime}m" : "")}";

    public string RatingText =>
        Rating.HasValue ? $"★ {Rating:F1}" : "";

    public string StatusBadge =>
        IsMissing ? "MISSING" : IsOnline ? "ONLINE" : "OFFLINE";
}
