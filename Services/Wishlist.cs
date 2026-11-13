using CineLibraryCS.Models;
using CineLibraryCS.Services.Tmdb;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CineLibraryCS.Services;

/// <summary>
/// 5.4.0: films you want and don't have yet, added with + on an actor's or
/// director's page (the wishlist table). Each poster is kept in
/// CineLibrary-Data\wishlist, so the Wishlist page shows offline too.
/// </summary>
public static class Wishlist
{
    /// <summary>Raised on the UI thread after a film is added or taken off.</summary>
    public static event Action? Changed;

    public static void Add(PersonFilm film, string? from)
    {
        AppState.Instance.Db.AddToWishlist(film, from);
        Changed?.Invoke();
        _ = SavePosterAsync(film);
    }

    public static void Remove(IReadOnlyCollection<int> tmdbIds)
    {
        AppState.Instance.Db.RemoveFromWishlist(tmdbIds);
        foreach (var id in tmdbIds)
            try { File.Delete(PosterFile(id)); } catch { }
        Changed?.Invoke();
    }

    /// <summary>"Inception (2010)", for copying.</summary>
    public static string Line(PersonFilm film)
        => film.ReleaseDate is { Length: >= 4 } d ? $"{film.Title} ({d[..4]})" : film.Title;

    private static string PosterFile(int tmdbId) => Path.Combine(AppState.Instance.DataDir, "wishlist", $"{tmdbId}.jpg");

    /// <summary>The film's poster: the kept copy when there is one, else TMDb's. Call on the UI thread.</summary>
    public static BitmapImage? Poster(PersonFilm film)
    {
        var kept = PosterFile(film.TmdbId);
        var uri = File.Exists(kept) ? new Uri(kept)
                : string.IsNullOrEmpty(film.PosterPath) ? null
                : new Uri($"https://image.tmdb.org/t/p/w342{film.PosterPath}");
        return uri == null ? null : new BitmapImage(uri) { DecodePixelWidth = 300 };
    }

    private static async Task SavePosterAsync(PersonFilm film)
    {
        if (string.IsNullOrEmpty(film.PosterPath) || File.Exists(PosterFile(film.TmdbId))) return;
        using var client = new TmdbClient();
        await client.DownloadImageAsync(client.GetImageUrl(film.PosterPath, "w342"), PosterFile(film.TmdbId));
    }
}
