namespace CineLibraryCS.Models;

public class Actor
{
    public string Name { get; set; } = "";
    public string? Role { get; set; }
    public string? Thumb { get; set; }
    public int SortOrder { get; set; }
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
    public string? Premiered { get; set; }
    public string? Studio { get; set; }
    public string? Country { get; set; }
    public string? LocalPoster { get; set; }
    public string? LocalFanart { get; set; }
    public bool IsMissing { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsWatched { get; set; }
    public bool IsOnline { get; set; }
    public bool Playable { get; set; }
    public string? DriveLabel { get; set; }
    public string? CurrentLetter { get; set; }
    public string VolumeSerial { get; set; } = "";
    public string? FolderRelPath { get; set; }
    public string? VideoFileRelPath { get; set; }

    public List<string> Genres { get; set; } = new();
    public List<string> Directors { get; set; } = new();
    public List<Actor> Actors { get; set; } = new();
    public List<string> Sets { get; set; } = new();

    public string RatingText => Rating.HasValue ? $"★ {Rating:F1}" : "";
    public string RuntimeText => Runtime.HasValue ? $"{Runtime} min" : "";
    public string ImdbUrl => ImdbId != null ? $"https://www.imdb.com/title/{ImdbId}/" : "";
}
