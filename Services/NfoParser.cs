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

            // Sets / collections — try every shape seen in the wild:
            //   <set>James Bond</set>
            //   <set><name>James Bond</name></set>
            //   <set><name>X</name><overview>...</overview></set>
            //   <set id="645"><name>James Bond</name></set>
            //   <sets><set>…</set></sets>                ← wrapper, some MediaElch versions
            //   <collection>James Bond</collection>      ← Plex / Emby style
            //   <collections><collection>…</collection></collections>
            //   <setname>James Bond Collection</setname> ← older Kodi root-level
            //
            // Defensive across all of them — Infuse handles more shapes than
            // our v1 parser did, which left ~half of real collections invisible.
            var sets = new List<string>();
            void CollectSetsFromXPath(string xpath)
            {
                XmlNodeList? nodes = null;
                try { nodes = root.SelectNodes(xpath); } catch { return; }
                if (nodes == null) return;
                foreach (XmlNode n in nodes)
                {
                    if (n is not XmlElement el) continue;
                    // Prefer <name> child; fall back to direct text content
                    var name = GetChild(el, "name");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        // If element has *only* text (no child elements), use its text
                        bool hasChildren = false;
                        foreach (XmlNode c in el.ChildNodes)
                            if (c is XmlElement) { hasChildren = true; break; }
                        if (!hasChildren) name = el.InnerText?.Trim();
                    }
                    if (!string.IsNullOrWhiteSpace(name)) sets.Add(name);
                }
            }
            CollectSetsFromXPath("set");
            CollectSetsFromXPath("sets/set");
            CollectSetsFromXPath("collection");
            CollectSetsFromXPath("collections/collection");
            // Root-level <setname>
            var setname = Get(root, "setname");
            if (!string.IsNullOrWhiteSpace(setname)) sets.Add(setname!);

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
