using System.Xml;

namespace CineLibraryCS.Services;

public record ParsedActor(string Name, string? Role, string? Thumb, int Order);

public record ParsedMovie(
    string Title,
    string? OriginalTitle,
    string? SortTitle,
    int? Year,
    double? Rating,
    int? Votes,
    int? Runtime,
    string? Plot,
    string? Outline,
    string? Tagline,
    string? Mpaa,
    string? ImdbId,
    string? TmdbId,
    string? Premiered,
    string? Studio,
    string? Country,
    string? Trailer,
    List<string> Genres,
    List<string> Directors,
    List<ParsedActor> Actors,
    List<string> Sets
);

public static class NfoParser
{
    public static ParsedMovie? Parse(string nfoPath)
    {
        try
        {
            var doc = new XmlDocument();
            doc.Load(nfoPath);
            var root = doc.DocumentElement;
            if (root == null) return null;

            string? title = Get(root, "title");
            if (string.IsNullOrWhiteSpace(title)) return null;

            var genres = new List<string>();
            foreach (XmlElement g in root.SelectNodes("genre") ?? (XmlNodeList)new XmlDocument().CreateDocumentFragment().ChildNodes)
            {
                var t = g.InnerText.Trim();
                if (!string.IsNullOrEmpty(t)) genres.Add(t);
            }

            var directors = new List<string>();
            foreach (XmlElement d in root.SelectNodes("director") ?? (XmlNodeList)new XmlDocument().CreateDocumentFragment().ChildNodes)
            {
                var t = d.InnerText.Trim();
                if (!string.IsNullOrEmpty(t)) directors.Add(t);
            }

            var actors = new List<ParsedActor>();
            int order = 0;
            foreach (XmlElement a in root.SelectNodes("actor") ?? (XmlNodeList)new XmlDocument().CreateDocumentFragment().ChildNodes)
            {
                var name = GetChild(a, "name");
                if (string.IsNullOrEmpty(name)) continue;
                actors.Add(new ParsedActor(
                    Name: name,
                    Role: GetChild(a, "role"),
                    Thumb: GetChild(a, "thumb"),
                    Order: order++
                ));
            }

            var sets = new List<string>();
            foreach (XmlElement s in root.SelectNodes("set") ?? (XmlNodeList)new XmlDocument().CreateDocumentFragment().ChildNodes)
            {
                // MediaElch can store <set><name>...</name></set> or <set>...</set>
                var setName = GetChild(s, "name") ?? s.InnerText.Trim();
                if (!string.IsNullOrEmpty(setName)) sets.Add(setName);
            }

            return new ParsedMovie(
                Title: title,
                OriginalTitle: Get(root, "originaltitle"),
                SortTitle: Get(root, "sorttitle"),
                Year: TryInt(Get(root, "year")),
                Rating: TryDouble(Get(root, "rating") ?? Get(root, "ratings/rating/value")),
                Votes: TryInt(Get(root, "votes") ?? Get(root, "ratings/rating/votes")),
                Runtime: TryInt(Get(root, "runtime")),
                Plot: Get(root, "plot"),
                Outline: Get(root, "outline"),
                Tagline: Get(root, "tagline"),
                Mpaa: Get(root, "mpaa"),
                ImdbId: Get(root, "imdbid") ?? Get(root, "uniqueid[@type='imdb']") ?? Get(root, "id"),
                TmdbId: Get(root, "tmdbid") ?? Get(root, "uniqueid[@type='tmdb']"),
                Premiered: Get(root, "premiered") ?? Get(root, "releasedate"),
                Studio: Get(root, "studio"),
                Country: Get(root, "country"),
                Trailer: Get(root, "trailer"),
                Genres: genres,
                Directors: directors,
                Actors: actors,
                Sets: sets
            );
        }
        catch
        {
            return null;
        }
    }

    private static string? Get(XmlElement root, string xpath)
    {
        try
        {
            var node = root.SelectSingleNode(xpath);
            var text = node?.InnerText?.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch { return null; }
    }

    private static string? GetChild(XmlElement el, string tag)
    {
        var node = el.SelectSingleNode(tag);
        var text = node?.InnerText?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static int? TryInt(string? s)
    {
        if (s == null) return null;
        if (int.TryParse(s, out var v)) return v;
        if (double.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d))
            return (int)d;
        return null;
    }

    private static double? TryDouble(string? s)
    {
        if (s == null) return null;
        if (double.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v))
            return v;
        return null;
    }
}
