using System.Globalization;
using System.Xml.Linq;

namespace CineLibraryCS.Services;

/// <summary>
/// v4.8.0: what Fetch missing info found, into the movie's own .nfo the way Kodi
/// keeps it, so Kodi, MediaElch and the next scan see it too. Fill-only: a tag
/// that has a value is never changed and nothing is taken out; a cast of 15 or
/// fewer gets the fetched people it doesn't have. Two repairs, for
/// what some tools write: an &lt;actor&gt; holding the whole cast (several
/// &lt;name&gt;s) becomes one &lt;actor&gt; per person, as Kodi reads only the first
/// name in each, and &lt;genre&gt;s in a &lt;genres&gt; wrapper move to where Kodi
/// reads them. Ratings stay with Update IMDb ratings (NfoRatings).
/// </summary>
public static class NfoFill
{
    /// <summary>
    /// Fills the blanks of a movie .nfo from <paramref name="meta"/>. True if it
    /// changed. <paramref name="actorThumb"/> gives a cast member's photo for
    /// &lt;thumb&gt; (".actors/First_Last.jpg"), or null for none.
    /// </summary>
    public static bool Apply(XElement root, MovieStateSidecar.Meta meta, Func<string, string?>? actorThumb = null)
    {
        bool packed = SplitPackedActors(root), changed = packed;
        changed |= UnwrapGenres(root);
        changed |= Text(root, "plot", meta.Plot);
        changed |= Text(root, "tagline", meta.Tagline);
        changed |= Text(root, "runtime", meta.Runtime is > 0 ? meta.Runtime.Value.ToString(CultureInfo.InvariantCulture) : null);
        changed |= Text(root, "mpaa", meta.Mpaa);
        changed |= Text(root, "premiered", meta.Premiered);
        changed |= Text(root, "year", meta.Year is > 0 ? meta.Year.Value.ToString(CultureInfo.InvariantCulture) : null);
        changed |= List(root, "genre", meta.Genres);
        changed |= List(root, "country", new List<string?> { meta.Country });
        changed |= List(root, "studio", new List<string?> { meta.Studio });
        changed |= List(root, "director", meta.Directors);
        // Kodi keeps writers in <credits>; some tools write <writer>
        if (!Has(root, "writer")) changed |= List(root, "credits", meta.Writers);
        changed |= TmdbId(root, meta.TmdbId);
        if (!string.IsNullOrWhiteSpace(meta.ImdbId)) changed |= NfoRatings.EnsureImdbId(root, meta.ImdbId.Trim());
        changed |= Cast(root, meta.Cast, actorThumb, packed);
        return changed;
    }

    /// <summary>One &lt;actor&gt; per &lt;name&gt; in a packed one, each with the
    /// tags that follow its name (role, order, thumb).</summary>
    private static bool SplitPackedActors(XElement root)
    {
        var packed = root.Elements("actor").Where(a => a.Elements("name").Count() > 1).ToList();
        foreach (var actor in packed)
        {
            var people = new List<XElement>();
            XElement? person = null;
            foreach (var child in actor.Elements())
            {
                if (person == null || (child.Name.LocalName == "name" && person.Element("name") != null))
                {
                    person = new XElement("actor");
                    people.Add(person);
                }
                person.Add(new XElement(child));
            }
            actor.ReplaceWith(people);
        }
        return packed.Count > 0;
    }

    /// <summary>The &lt;genre&gt;s of a &lt;genres&gt; wrapper straight under &lt;movie&gt;,
    /// unless there are some there already.</summary>
    private static bool UnwrapGenres(XElement root)
    {
        if (root.Elements("genre").Any(g => !Blank(g))) return false;
        var wrappers = root.Elements("genres").Where(w => w.Elements("genre").Any(g => !Blank(g))).ToList();
        foreach (var w in wrappers)
            w.ReplaceWith(w.Elements("genre").Where(g => !Blank(g)).Select(g => new XElement(g)).ToList());
        return wrappers.Count > 0;
    }

    /// <summary>A single-value tag: set when it's missing or empty (an empty one,
    /// as MediaElch leaves them, is filled where it is).</summary>
    private static bool Text(XElement root, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var el = root.Element(name);
        if (el != null && !Blank(el)) return false;
        if (el != null) el.Value = value.Trim();
        else Insert(root, new XElement(name, value.Trim()));
        return true;
    }

    /// <summary>A repeated tag (one per genre, director...): added only when the
    /// .nfo has none with a value, in place of the empty ones.</summary>
    private static bool List(XElement root, string name, IEnumerable<string?> values)
    {
        var items = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim())
                          .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (items.Count == 0 || Has(root, name)) return false;
        var fresh = items.Select(v => new XElement(name, v)).ToList();
        var empties = root.Elements(name).ToList();
        if (empties.Count > 0)
        {
            empties[0].AddBeforeSelf(fresh);
            foreach (var e in empties) e.Remove();
        }
        else Insert(root, fresh);
        return true;
    }

    private static bool TmdbId(XElement root, string? tmdbId)
    {
        if (string.IsNullOrWhiteSpace(tmdbId)) return false;
        bool has = root.Elements("uniqueid").Any(u =>
                       ((string?)u.Attribute("type"))?.Trim().ToLowerInvariant() is "tmdb" or "themoviedb"
                       && !string.IsNullOrWhiteSpace(u.Value))
                   || !string.IsNullOrWhiteSpace(root.Element("tmdbid")?.Value);
        if (has) return false;
        var id = new XElement("uniqueid", new XAttribute("type", "tmdb"), tmdbId.Trim());
        var after = root.Elements("uniqueid").LastOrDefault() ?? root.Element("id");
        if (after != null) after.AddAfterSelf(id); else Insert(root, id);
        return true;
    }

    /// <summary>The cast, when the .nfo names nobody: one &lt;actor&gt; each, in billing
    /// order. A short cast gets the people it doesn't have after it: one that was
    /// packed (a broken file, usually TMDb's first 10) or one of 15 or fewer (all
    /// Fetch took before 4.8.0). A longer cast is the .nfo's own and stays as it is.</summary>
    private static bool Cast(XElement root, List<MovieStateSidecar.CastEntry> cast, Func<string, string?>? actorThumb, bool packed)
    {
        var people = cast.Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToList();
        XElement Actor(MovieStateSidecar.CastEntry c, int order)
        {
            var actor = new XElement("actor",
                new XElement("name", c.Name.Trim()),
                new XElement("role", c.Role?.Trim() ?? ""),
                new XElement("order", order));
            if (actorThumb?.Invoke(c.Name.Trim()) is { } thumb) actor.Add(new XElement("thumb", thumb));
            return actor;
        }
        var named = root.Elements("actor").Where(a => !Blank(a.Element("name"))).ToList();
        if (named.Count > 0)
        {
            if (!packed && named.Count > 15) return false;
            var have = named.Select(a => NameKey(a.Element("name")!.Value)).ToHashSet();
            var more = people.Where(c => !have.Contains(NameKey(c.Name))).ToList();
            if (more.Count == 0) return false;
            named[^1].AddAfterSelf(more.Select((c, i) => Actor(c, named.Count + i)).ToList());
            return true;
        }
        if (people.Count == 0) return false;
        var fresh = people.Select((c, i) => Actor(c, i)).ToList();
        var empties = root.Elements("actor").ToList();
        if (empties.Count > 0)
        {
            empties[0].AddBeforeSelf(fresh);
            foreach (var e in empties) e.Remove();
        }
        // after the file info, as Kodi writes it
        else if (root.Elements().FirstOrDefault(e => e.Name.LocalName is "resume" or "dateadded" or "generator") is { } anchor)
            anchor.AddBeforeSelf(fresh);
        else root.Add(fresh);
        return true;
    }

    // A name by its letters and digits: "Saif Ali-Khan" = "saif ali khan"
    private static string NameKey(string? name)
        => new string((name ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool Blank(XElement? el) => el == null || string.IsNullOrWhiteSpace(el.Value) || el.Value.Trim() == "0";

    /// <summary>A tag with a value, where Kodi reads it (straight under &lt;movie&gt;).</summary>
    private static bool Has(XElement root, string name)
        => root.Elements(name).Any(e => !string.IsNullOrWhiteSpace(e.Value));

    /// <summary>A new tag goes before the file info, cast and the tags tools add at the end.</summary>
    private static void Insert(XElement root, object content)
    {
        var before = root.Elements().FirstOrDefault(e => e.Name.LocalName is "fileinfo" or "actor" or "resume" or "dateadded" or "generator");
        if (before != null) before.AddBeforeSelf(content); else root.Add(content);
    }
}
