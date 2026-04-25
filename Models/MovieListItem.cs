namespace CineLibraryCS.Models;

public class MovieListItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int? Year { get; set; }
    public double? Rating { get; set; }
    public int? Runtime { get; set; }
    public string? LocalPoster { get; set; }
    public bool IsMissing { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsWatched { get; set; }
    public bool IsWatchlist { get; set; }
    public string VolumeSerial { get; set; } = "";
    public string? DriveLabel { get; set; }
    public string? GenresCsv { get; set; }
    public bool IsOnline { get; set; }

    public string YearRuntimeText =>
        $"{Year?.ToString() ?? "—"}{(Runtime.HasValue ? $" · {Runtime}m" : "")}";

    public string RatingText =>
        Rating.HasValue ? $"★ {Rating:F1}" : "";

    public string StatusBadge =>
        IsMissing ? "MISSING" : IsOnline ? "ONLINE" : "OFFLINE";
}
