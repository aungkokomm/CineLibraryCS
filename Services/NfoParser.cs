using System.Xml;

namespace CineLibraryCS.Services;

public record ParsedActor(string Name, string? Role, string? Thumb, int Order);

/// <summary>
/// Stream-info from `<fileinfo>/<streamdetails>` — width/height, codec,
/// channels, languages, HDR. Fields may be null on older or sparse nfos.
/// </summary>
public record ParsedStreamDetails(
    int? VideoWidth,
    int? VideoHeight,
    string? VideoCodec,
    string? VideoAspect,
    string? HdrType,
    string? AudioCodec,
    string? AudioChannels,
    string? AudioLanguages,
    string? SubtitleLanguages,
    int? DurationSeconds
);

public record ParsedRating(string Source, double Value, int? Votes);

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
    List<string> Writers,
    List<ParsedActor> Actors,
    List<string> Sets,
    List<ParsedRating> Ratings,
    ParsedStreamDetails? Stream
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

            var genres = ReadList(root, "genre");
            var directors = ReadList(root, "director");
            // <credits> and <writer> both exist in the wild — merge dedup'd
            var writers = MergeLists(ReadList(root, "credits"), ReadList(root, "writer"));

            var actors = new List<ParsedActor>();
            int order = 0;
            foreach (XmlElement a in SelectElements(root, "actor"))
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

            // Sets / collections — every shape seen in the wild
            var sets = new List<string>();
            void CollectSetsFromXPath(string xpath)
            {
                XmlNodeList? nodes = null;
                try { nodes = root.SelectNodes(xpath); } catch { return; }
                if (nodes == null) return;
                foreach (XmlNode n in nodes)
                {
                    if (n is not XmlElement el) continue;
                    var name = GetChild(el, "name");
                    if (string.IsNullOrWhiteSpace(name))
                    {
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
            var setname = Get(root, "setname");
            if (!string.IsNullOrWhiteSpace(setname)) sets.Add(setname!);

            // Multiple ratings: <ratings><rating name="imdb" default="true">value/votes</rating>…
            var ratings = new List<ParsedRating>();
            foreach (XmlElement rt in SelectElements(root, "ratings/rating"))
            {
                var src = rt.GetAttribute("name");
                if (string.IsNullOrEmpty(src)) src = rt.GetAttribute("moviedb");
                if (string.IsNullOrEmpty(src)) src = "unknown";
                var v = TryDouble(GetChild(rt, "value") ?? rt.InnerText);
                if (v == null) continue;
                var votes = TryInt(GetChild(rt, "votes"));
                ratings.Add(new ParsedRating(src, v.Value, votes));
            }

            var streamDetails = ParseStreamDetails(root);

            // Primary rating: first from <ratings> if present, else <rating>.
            double? primaryRating = ratings.Count > 0
                ? ratings[0].Value
                : TryDouble(Get(root, "rating"));
            int? primaryVotes = ratings.Count > 0
                ? ratings[0].Votes
                : TryInt(Get(root, "votes"));

            return new ParsedMovie(
                Title: title,
                OriginalTitle: Get(root, "originaltitle"),
                SortTitle: Get(root, "sorttitle"),
                Year: TryInt(Get(root, "year")),
                Rating: primaryRating,
                Votes: primaryVotes,
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
                Writers: writers,
                Actors: actors,
                Sets: sets,
                Ratings: ratings,
                Stream: streamDetails
            );
        }
        catch
        {
            return null;
        }
    }

    private static ParsedStreamDetails? ParseStreamDetails(XmlElement root)
    {
        var sd = root.SelectSingleNode("fileinfo/streamdetails")
               ?? root.SelectSingleNode("streamdetails");
        if (sd == null) return null;

        int? vw = null, vh = null, dur = null;
        string? vcodec = null, vaspect = null, hdr = null;
        string? acodec = null, achan = null;
        var audioLangs = new List<string>();
        var subLangs = new List<string>();

        if (sd.SelectSingleNode("video") is XmlElement video)
        {
            vw = TryInt(GetChild(video, "width"));
            vh = TryInt(GetChild(video, "height"));
            vcodec = GetChild(video, "codec");
            vaspect = GetChild(video, "aspect");
            hdr = GetChild(video, "hdrtype");
            dur = TryInt(GetChild(video, "durationinseconds"));
            if (dur == null)
            {
                var mins = TryInt(GetChild(video, "duration"));
                if (mins != null) dur = mins * 60;
            }
        }

        var audioNodes = sd.SelectNodes("audio");
        if (audioNodes != null)
        {
            foreach (XmlElement au in audioNodes)
            {
                if (acodec == null) acodec = GetChild(au, "codec");
                if (achan == null) achan = GetChild(au, "channels");
                var lang = GetChild(au, "language");
                if (!string.IsNullOrEmpty(lang)) audioLangs.Add(lang);
            }
        }

        var subNodes = sd.SelectNodes("subtitle");
        if (subNodes != null)
        {
            foreach (XmlElement sub in subNodes)
            {
                var lang = GetChild(sub, "language");
                if (!string.IsNullOrEmpty(lang)) subLangs.Add(lang);
            }
        }

        return new ParsedStreamDetails(
            VideoWidth: vw,
            VideoHeight: vh,
            VideoCodec: vcodec,
            VideoAspect: vaspect,
            HdrType: hdr,
            AudioCodec: acodec,
            AudioChannels: achan,
            AudioLanguages: audioLangs.Count > 0 ? string.Join(",", audioLangs.Distinct()) : null,
            SubtitleLanguages: subLangs.Count > 0 ? string.Join(",", subLangs.Distinct()) : null,
            DurationSeconds: dur
        );
    }

    private static List<string> ReadList(XmlElement root, string xpath)
    {
        var list = new List<string>();
        foreach (XmlElement el in SelectElements(root, xpath))
        {
            var t = el.InnerText.Trim();
            if (!string.IsNullOrEmpty(t)) list.Add(t);
        }
        return list;
    }

    private static List<string> MergeLists(params List<string>[] lists)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var l in lists)
            foreach (var s in l)
                if (seen.Add(s)) result.Add(s);
        return result;
    }

    private static IEnumerable<XmlElement> SelectElements(XmlElement root, string xpath)
    {
        XmlNodeList? nodes = null;
        try { nodes = root.SelectNodes(xpath); } catch { yield break; }
        if (nodes == null) yield break;
        foreach (var n in nodes) if (n is XmlElement el) yield return el;
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
