namespace CineLibraryCS.Services;

/// <summary>
/// Canonical-English genre names + alias folding. Used by ScannerService
/// when storing genres from a freshly-parsed .nfo, and by DatabaseService's
/// retro-fix migration which runs once per DB version bump.
///
/// Keep entries lowercase — lookups are case-insensitive.
/// Non-English keys cover the common locales TMDB hands MediaElch back
/// (Arabic, Hindi, Spanish, French, German, Portuguese) for users who
/// scraped in their native language.
/// </summary>
public static class GenreAliases
{
    public const string Version = "v2.1.1";   // bump → migration re-runs

    public static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // ── English variants ────────────────────────────────────────────────
        ["science fiction"] = "Sci-Fi",
        ["sciencefiction"]  = "Sci-Fi",
        ["sci fi"]          = "Sci-Fi",
        ["scifi"]           = "Sci-Fi",
        ["science-fiction"] = "Sci-Fi",
        ["action & adventure"] = "Action",
        ["tv movie"]           = "TV Movie",
        ["film-noir"]          = "Film Noir",
        ["film noir"]          = "Film Noir",
        ["children"]           = "Family",
        ["kids"]               = "Family",
        ["bio"]                = "Biography",
        ["docu"]               = "Documentary",
        ["documentaries"]      = "Documentary",

        // ── Arabic (التصنيفات بالعربية) ───────────────────────────────────────
        ["حركة"]               = "Action",
        ["مغامرة"]              = "Adventure",
        ["مغامرات"]             = "Adventure",
        ["رسوم متحركة"]         = "Animation",
        ["كوميديا"]             = "Comedy",
        ["جريمة"]               = "Crime",
        ["وثائقي"]              = "Documentary",
        ["دراما"]               = "Drama",
        ["عائلي"]               = "Family",
        ["فانتازيا"]            = "Fantasy",
        ["خيالي"]               = "Fantasy",
        ["تاريخي"]              = "History",
        ["رعب"]                 = "Horror",
        ["موسيقى"]              = "Music",
        ["موسيقي"]              = "Music",
        ["غموض"]                = "Mystery",
        ["رومانسية"]            = "Romance",
        ["رومنسية"]             = "Romance",
        ["خيال علمي"]           = "Sci-Fi",
        ["إثارة"]               = "Thriller",
        ["حرب"]                = "War",
        ["غربي"]                = "Western",
        ["سيرة ذاتية"]          = "Biography",
        ["رياضي"]               = "Sport",

        // ── Hindi (मूल हिन्दी श्रेणियाँ) ─────────────────────────────────────
        ["एक्शन"]               = "Action",
        ["एडवेंचर"]              = "Adventure",
        ["एनिमेशन"]             = "Animation",
        ["कॉमेडी"]               = "Comedy",
        ["क्राइम"]               = "Crime",
        ["डॉक्यूमेंट्री"]         = "Documentary",
        ["ड्रामा"]               = "Drama",
        ["पारिवारिक"]           = "Family",
        ["फैंटेसी"]              = "Fantasy",
        ["हॉरर"]                = "Horror",
        ["रोमांस"]              = "Romance",
        ["रोमांचक"]             = "Thriller",
        ["साइंस फिक्शन"]        = "Sci-Fi",
        ["थ्रिलर"]               = "Thriller",
        ["युद्ध"]               = "War",
        ["संगीतमय"]              = "Musical",
        ["रहस्य"]               = "Mystery",

        // ── Spanish ─────────────────────────────────────────────────────────
        ["acción"]              = "Action",
        ["accion"]              = "Action",
        ["aventura"]            = "Adventure",
        ["animación"]           = "Animation",
        ["animacion"]           = "Animation",
        ["comedia"]             = "Comedy",
        ["crimen"]              = "Crime",
        ["documental"]          = "Documentary",
        ["familia"]             = "Family",
        ["fantasía"]            = "Fantasy",
        ["fantasia"]            = "Fantasy",
        ["historia"]            = "History",
        ["terror"]              = "Horror",
        ["misterio"]            = "Mystery",
        ["romance"]             = "Romance",
        ["ciencia ficción"]     = "Sci-Fi",
        ["ciencia ficcion"]     = "Sci-Fi",
        ["suspense"]            = "Thriller",
        ["suspenso"]            = "Thriller",
        ["guerra"]              = "War",

        // ── French ──────────────────────────────────────────────────────────
        ["aventure"]            = "Adventure",
        ["animation"]           = "Animation",
        ["comédie"]             = "Comedy",
        ["comedie"]             = "Comedy",
        ["crime"]               = "Crime",
        ["documentaire"]        = "Documentary",
        ["drame"]                = "Drama",
        ["famille"]              = "Family",
        ["fantastique"]          = "Fantasy",
        ["histoire"]             = "History",
        ["horreur"]              = "Horror",
        ["mystère"]              = "Mystery",
        ["mystere"]              = "Mystery",
        ["science-fiction"]      = "Sci-Fi",
        ["thriller"]             = "Thriller",
        ["guerre"]               = "War",
        ["western"]              = "Western",

        // ── German ──────────────────────────────────────────────────────────
        ["abenteuer"]            = "Adventure",
        ["komödie"]              = "Comedy",
        ["komodie"]              = "Comedy",
        ["dokumentarfilm"]       = "Documentary",
        ["familie"]              = "Family",
        ["fantasie"]             = "Fantasy",
        ["geschichte"]           = "History",
        ["liebesfilm"]           = "Romance",
        ["liebe"]                = "Romance",
        ["mysterie"]             = "Mystery",
        ["krimi"]                = "Crime",
        ["kriminalfilm"]         = "Crime",
        ["sciencefiction"]       = "Sci-Fi",
        ["krieg"]                = "War",
    };

    /// <summary>Folds the given name to its canonical English form, or
    /// returns it unchanged if no alias is known.</summary>
    public static string Fold(string name)
        => Map.TryGetValue(name, out var canon) ? canon : name;
}
