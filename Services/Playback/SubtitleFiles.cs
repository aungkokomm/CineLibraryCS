using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace CineLibraryCS.Services.Playback;

/// <summary>
/// 5.1.0 (#25): subtitle files kept in a Subs or Subtitles folder beside the video. VLC only
/// finds those named after the video, and many are called "2_English.srt". A folder several
/// videos share (a season) gives each one only what's named after it or in a folder of its name.
/// </summary>
internal static partial class SubtitleFiles
{
    public static readonly string[] Extensions = [".srt", ".ass", ".ssa", ".vtt", ".sub", ".idx"];
    private static readonly string[] Folders = ["Subs", "Subtitles", "Sub"];   // v5.2.0: Sub too
    private static readonly string[] Videos = [".mkv", ".mp4", ".avi", ".m4v", ".mov", ".wmv", ".ts", ".m2ts", ".mpg", ".mpeg", ".webm"];

    public static List<string> Near(string video)
    {
        var found = new List<string>();
        if (Path.GetDirectoryName(video) is not { } folder) return found;
        var name = Path.GetFileNameWithoutExtension(video);
        try
        {
            var shared = Directory.EnumerateFiles(folder).Count(f => Has(f, Videos)) > 1;
            foreach (var subs in Directory.EnumerateDirectories(folder).Where(d => Folders.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)))
            {
                found.AddRange(Directory.EnumerateFiles(subs).Where(IsSubtitles)
                    .Where(f => !shared || Path.GetFileName(f).StartsWith(name, StringComparison.OrdinalIgnoreCase)));
                var own = Path.Combine(subs, name);
                if (Directory.Exists(own)) found.AddRange(Directory.EnumerateFiles(own).Where(IsSubtitles));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            PlayerLog.Write($"subtitle files: {e.Message}");
        }
        found.Sort(StringComparer.OrdinalIgnoreCase);
        return found;
    }

    /// <summary>The one named after the video, which VLC would have shown by itself.</summary>
    public static string? NamedAfter(IEnumerable<string> files, string video) => files.Where(f =>
        Path.GetFileName(f).StartsWith(Path.GetFileNameWithoutExtension(video), StringComparison.OrdinalIgnoreCase)).MinBy(f => f.Length);

    /// <summary>A menu name, in the form VLC gives a track: "2_English - [English]", or the file's name
    /// when it doesn't say.</summary>
    public static string Label(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        return LanguageOf(name) is { } language && !string.Equals(language, name, StringComparison.OrdinalIgnoreCase)
            ? $"{name} - [{language}]" : name;
    }

    private static bool Has(string file, string[] extensions) =>
        extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);

    // A .sub beside its .idx is a picture track, and the .idx opens it
    private static bool IsSubtitles(string file) => Has(file, Extensions)
        && !(Path.GetExtension(file).Equals(".sub", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.ChangeExtension(file, ".idx")));

    private static readonly Lazy<Dictionary<string, string>> Languages = new(() =>
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.NeutralCultures).Where(c => c.Name.Length > 0))
        {
            var name = culture.EnglishName.Split(' ')[0];
            map.TryAdd(name, name);
            map.TryAdd(culture.ThreeLetterISOLanguageName, name);
            map.TryAdd(culture.TwoLetterISOLanguageName, name);
        }
        return map;
    });

    // A language's whole name anywhere, or a short code at the very end ("Film.en")
    private static string? LanguageOf(string name)
    {
        var words = Words().Matches(name).Select(m => m.Value).ToList();
        foreach (var word in Enumerable.Reverse(words))
            if (word.Length > 3 && Languages.Value.TryGetValue(word, out var language)) return language;
        return words.Count > 0 && Languages.Value.TryGetValue(words[^1], out var coded) ? coded : null;
    }

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex Words();
}
