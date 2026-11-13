using Microsoft.UI.Xaml.Media.Imaging;

namespace CineLibraryCS.Models;

/// <summary>
/// 5.4.0: an actor or director as TMDb knows them, shown above their films in the
/// library (see Services/People). Kept as JSON in the people_tmdb table.
/// </summary>
public sealed class PersonInfo
{
    public int TmdbId { get; set; }
    public string Name { get; set; } = "";
    public string Biography { get; set; } = "";
    public string? Birthday { get; set; }       // yyyy-MM-dd
    public string? Deathday { get; set; }
    public string? PlaceOfBirth { get; set; }
    public string? ProfilePath { get; set; }
    public string? ImdbId { get; set; }
    /// <summary>Their films as an actor, the most voted first (up to 60).</summary>
    public List<PersonFilm> Acted { get; set; } = new();
    /// <summary>Every film they directed.</summary>
    public List<PersonFilm> Directed { get; set; } = new();
}

/// <summary>5.4.0: one of a person's films on TMDb.</summary>
public sealed class PersonFilm
{
    public int TmdbId { get; set; }
    public string Title { get; set; } = "";
    public string? OriginalTitle { get; set; }
    public string? ReleaseDate { get; set; }    // yyyy-MM-dd, or null when TMDb has none yet
    public string? PosterPath { get; set; }
    public int Votes { get; set; }
}

/// <summary>5.4.1: someone from your films born on this day, on On This Day.</summary>
public sealed class BirthdayPerson
{
    public BirthdayPerson(string name, string line, BitmapImage? photo)
    {
        Name = name;
        Line = line;
        Photo = photo;
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Initials = parts.Length == 0 ? "?" : parts.Length == 1 ? parts[0][..1].ToUpperInvariant()
                 : (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
    }

    public string Name { get; }
    public string Line { get; }
    public BitmapImage? Photo { get; }
    public string Initials { get; }
}

/// <summary>
/// 5.4.0: a film that isn't in the library, as a card (TmdbFilmCard): on an actor's
/// or director's page, and on the Wishlist.
/// </summary>
public sealed partial class TmdbFilmItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public TmdbFilmItem(PersonFilm film, string subtitle, BitmapImage? poster, string? from, bool wished, bool owned = false)
    {
        Film = film;
        Subtitle = subtitle;
        Poster = poster;
        From = from;
        _wished = wished;
        Owned = owned;
    }

    public PersonFilm Film { get; }
    /// <summary>Under the title: the year, and on the Wishlist whose page it came from.</summary>
    public string Subtitle { get; }
    public BitmapImage? Poster { get; }
    /// <summary>The actor or director whose page it's on (kept with it on the Wishlist).</summary>
    public string? From { get; }
    /// <summary>On the Wishlist and in the library now.</summary>
    public bool Owned { get; }
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _wished;
}
