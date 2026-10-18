namespace CineLibraryCS.Services;

/// <summary>
/// v4.7.0: what the sidebar shows. The tags pinned to it (Tags page, right-click)
/// and the items you hid from it (right-click → Hide from sidebar; Settings shows
/// them again). Kept in the prefs table, one name per line.
/// </summary>
public static class SidebarPrefs
{
    private const string KeyPinned = "sidebar_pinnedTags";
    private const string KeyHidden = "sidebar_hiddenItems";   // the items' x:Name

    public static List<string> PinnedTags => Read(KeyPinned);

    public static bool IsPinned(string tag) => PinnedTags.Contains(tag, StringComparer.OrdinalIgnoreCase);

    public static void SetPinned(string tag, bool pinned) => Write(KeyPinned, With(PinnedTags, tag, pinned));

    /// <summary>A pinned tag that was renamed stays pinned under its new name.</summary>
    public static void RenamePinned(string from, string to)
    {
        var list = PinnedTags;
        var i = list.FindIndex(t => t.Equals(from, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return;
        list[i] = to;
        Write(KeyPinned, list);
    }

    public static List<string> HiddenItems => Read(KeyHidden);

    public static void SetHidden(string item, bool hidden) => Write(KeyHidden, With(HiddenItems, item, hidden));

    public static void ShowAll() => Write(KeyHidden, new List<string>());

    private static List<string> Read(string key)
        => AppState.Instance.GetPref(key)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    private static void Write(string key, List<string> items) => AppState.Instance.SetPref(key, string.Join('\n', items));

    private static List<string> With(List<string> list, string item, bool on)
    {
        list.RemoveAll(x => x.Equals(item, StringComparison.OrdinalIgnoreCase));
        if (on) list.Add(item);
        return list;
    }
}
