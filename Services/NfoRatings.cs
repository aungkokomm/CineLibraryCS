using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace CineLibraryCS.Services;

/// <summary>
/// v4.6.0: puts an IMDb rating into an existing .nfo, for Update IMDb ratings.
/// A port of CineLibrary Essentials' RatingsXml / NfoFile, so both apps write
/// the same Kodi &lt;ratings&gt; block:
///   &lt;ratings&gt;
///     &lt;rating name="imdb" default="true" max="10"&gt;&lt;value&gt;7.8&lt;/value&gt;&lt;votes&gt;512000&lt;/votes&gt;&lt;/rating&gt;
///     &lt;rating name="themoviedb" max="10"&gt;&lt;value&gt;7.4&lt;/value&gt;&lt;votes&gt;9120&lt;/votes&gt;&lt;/rating&gt;
///   &lt;/ratings&gt;
/// Only that block (and a missing IMDb id) changes; every other tag is left as it is.
/// </summary>
public static class NfoRatings
{
    public const string Imdb = "imdb";
    public const string Tmdb = "themoviedb";

    public record Entry(string Name, double Value, int Votes, bool IsDefault);

    /// <summary>
    /// Sets the IMDb entry, keeping the other sources. IMDb becomes the default
    /// (the rating players show first) when <paramref name="preferImdb"/>, or when
    /// there's no TMDb one. True if the .nfo changed.
    /// </summary>
    public static bool ApplyImdb(XElement root, double rating, int votes, bool preferImdb)
    {
        if (rating <= 0) return false;

        var existing = ReadAll(root);
        // An .nfo without a <ratings> block (CineLibrary Essentials before 1.1.10)
        // keeps TMDb's rating in the old single <rating>: carry it over as TMDb's.
        if (existing.Count == 0 && root.Element("ratings") == null
            && TryNumber(root.Element("rating")?.Value, out var legacy) && legacy > 0)
        {
            int.TryParse(root.Element("votes")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var legacyVotes);
            existing.Add(new Entry(Tmdb, legacy, legacyVotes, true));
        }

        var tmdb = existing.FirstOrDefault(e => e.Name is Tmdb or "tmdb");
        var old = existing.FirstOrDefault(e => e.Name == Imdb);
        bool imdbDefault = preferImdb || tmdb == null;
        if (old != null && Math.Abs(old.Value - Math.Round(rating, 1)) < 0.001 && old.Votes == votes
            && old.IsDefault == imdbDefault && root.Element("ratings") != null)
            return false;   // already current

        var block = new XElement("ratings");
        // The default first, so tools that read the first <rating> agree with players
        if (imdbDefault)
        {
            block.Add(Rating(Imdb, rating, votes, true));
            if (tmdb != null) block.Add(Rating(Tmdb, tmdb.Value, tmdb.Votes, false));
        }
        else
        {
            block.Add(Rating(Tmdb, tmdb!.Value, tmdb.Votes, true));
            block.Add(Rating(Imdb, rating, votes, false));
        }
        // Other sources (e.g. from other tools) after ours, kept as they are: one
        // can be out of another scale (Rotten Tomatoes' max="100")
        if (root.Element("ratings") is { } current)
        {
            foreach (var el in current.Elements("rating"))
            {
                var name = ((string?)el.Attribute("name") ?? "").Trim().ToLowerInvariant();
                if (name is Imdb or Tmdb or "tmdb") continue;
                var copy = new XElement(el);
                copy.Attribute("default")?.Remove();
                block.Add(copy);
            }
            current.ReplaceWith(block);
        }
        else
        {
            // Kodi puts <ratings> right after the titles
            var anchor = root.Element("originaltitle") ?? root.Element("title");
            if (anchor != null) anchor.AddAfterSelf(block); else root.AddFirst(block);
        }
        return true;
    }

    /// <summary>Adds &lt;uniqueid type="imdb"&gt; when the .nfo has no IMDb id. True if it changed.</summary>
    public static bool EnsureImdbId(XElement root, string imdbId)
    {
        bool has = root.Elements("uniqueid").Any(u =>
                       string.Equals((string?)u.Attribute("type"), "imdb", StringComparison.OrdinalIgnoreCase)
                       && !string.IsNullOrWhiteSpace(u.Value))
                   || !string.IsNullOrWhiteSpace(root.Element("imdbid")?.Value)
                   || (root.Element("id")?.Value.Trim().StartsWith("tt", StringComparison.OrdinalIgnoreCase) ?? false);
        if (has) return false;
        var id = new XElement("uniqueid", new XAttribute("type", "imdb"), imdbId);
        var after = root.Elements("uniqueid").LastOrDefault() ?? root.Element("tmdbid");
        if (after != null) after.AddAfterSelf(id);
        else if (root.Element("ratings") is { } ratings) ratings.AddAfterSelf(id);
        else root.Add(id);
        return true;
    }

    /// <summary>All entries in the &lt;ratings&gt; block (empty if none).</summary>
    public static List<Entry> ReadAll(XElement root)
    {
        var list = new List<Entry>();
        if (root.Element("ratings") is not { } ratings) return list;
        foreach (var r in ratings.Elements("rating"))
        {
            var name = ((string?)r.Attribute("name") ?? string.Empty).Trim().ToLowerInvariant();
            if (!TryNumber(r.Element("value")?.Value, out var value) || value <= 0) continue;
            int.TryParse(r.Element("votes")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var votes);
            var isDefault = string.Equals((string?)r.Attribute("default"), "true", StringComparison.OrdinalIgnoreCase);
            list.Add(new Entry(name, value, votes, isDefault));
        }
        return list;
    }

    /// <summary>
    /// Saves as UTF-8 (no BOM), indented, as Essentials writes them, through a
    /// temporary file so a full disk or a pulled drive can't leave half an .nfo.
    /// </summary>
    public static void Save(XDocument doc, string path)
    {
        var tmp = path + ".tmp";
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
        };
        try
        {
            using (var writer = XmlWriter.Create(tmp, settings))
                doc.Save(writer);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            throw;
        }
    }

    private static bool TryNumber(string? s, out double value)
        => double.TryParse(s?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static XElement Rating(string name, double value, int votes, bool isDefault)
    {
        var el = new XElement("rating", new XAttribute("name", name));
        if (isDefault) el.Add(new XAttribute("default", "true"));
        el.Add(new XAttribute("max", "10"),
               new XElement("value", value.ToString("F1", CultureInfo.InvariantCulture)),
               new XElement("votes", votes.ToString(CultureInfo.InvariantCulture)));
        return el;
    }
}
