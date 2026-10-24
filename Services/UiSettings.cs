namespace CineLibraryCS.Services;

/// <summary>
/// v3.0.0 — user-facing UI preferences exposed through the Settings dialog.
///
/// Backed by the existing prefs table (AppState.GetPref/SetPref) so they
/// persist across sessions. <see cref="Changed"/> fires whenever a value is
/// updated, so live UI (movie cards) can react without a restart or reload.
///
/// Both visual extras default to OFF, so the out-of-the-box experience is the
/// fast, flat one — performance-minded users keep it, and anyone who wants the
/// richer look opts in.
/// </summary>
public static class UiSettings
{
    private const string KeyCardShadows = "ui_cardShadows";
    private const string KeyReduceMotion = "ui_reduceMotion";
    private const string KeyCardBorders = "ui_cardBorders";
    private const string KeyNewTag = "ui_newTag";                   // v4.8.0
    private const string KeyQualityTags = "ui_qualityTags";         // v4.8.0
    private const string KeyStandInPosters = "ui_standInPosters";   // v4.8.0
    private const string KeyMica = "ui_mica";              // legacy bool
    private const string KeyMicaLevel = "ui_micaLevel";   // v3.3.2 — off|subtle|strong
    private const string KeyPlayerPath = "ui_playerPath"; // v3.6.0: "" = Windows default
    private const string KeyMissingEpisodes = "tv_checkMissingEpisodes"; // v4.6.0
    private const string KeySaveFetched = "drive_saveFetched";            // v4.6.0

    /// <summary>How much Windows Mica material shows behind the window.
    /// Off = flat solid (also best on weak GPUs); Subtle / Strong control how
    /// translucent the floating sidebar is over the wallpaper-tinted backdrop.</summary>
    public enum MicaLevel { Off, Subtle, Strong }

    /// <summary>Selected Mica intensity. Default Subtle.</summary>
    public static MicaLevel Mica { get; private set; } = MicaLevel.Subtle;

    /// <summary>True whenever Mica is on (either intensity).</summary>
    public static bool MicaEnabled => Mica != MicaLevel.Off;

    /// <summary>Thin outline around movie cards. Default on.</summary>
    public static bool CardBorders { get; private set; } = true;

    /// <summary>Faint resting drop shadow on movie cards. Default off.</summary>
    public static bool CardShadows { get; private set; }

    /// <summary>Disable the card hover zoom/lift animation. Default off.</summary>
    public static bool ReduceMotion { get; private set; }

    /// <summary>v4.8.0: a NEW tag on movies added in the last two weeks. Default on.</summary>
    public static bool NewTag { get; private set; } = true;

    /// <summary>v4.8.0: 4K, HDR or DV on the poster. Default on.</summary>
    public static bool QualityTags { get; private set; } = true;

    /// <summary>v4.8.0: the title on a coloured card when there's no poster. Default on.</summary>
    public static bool StandInPosters { get; private set; } = true;

    /// <summary>v3.6.0: the program Play opens videos with (VLC, MPC-HC, ...).
    /// Empty = whatever Windows opens video files with. See VideoPlayer.</summary>
    public static string PlayerPath { get; private set; } = "";

    /// <summary>v4.6.0: a show page asks TMDb which aired episodes aren't on
    /// disk (the one thing CineLibrary goes online for by itself). Default on.</summary>
    public static bool CheckMissingEpisodes { get; private set; } = true;

    /// <summary>v4.6.0: copy what Fetch missing info gets into the title's folder,
    /// now or when its drive is next connected (AppState.SyncFetched). Default on.</summary>
    public static bool SaveFetchedToDrives { get; private set; } = true;

    /// <summary>Raised after any setting changes so live UI can re-apply it.</summary>
    public static event Action? Changed;

    /// <summary>Load persisted values. Call once at startup, after AppState.Initialize().</summary>
    public static void Load()
    {
        CardBorders  = AppState.Instance.GetPref(KeyCardBorders,  "true")  == "true";
        CardShadows  = AppState.Instance.GetPref(KeyCardShadows,  "false") == "true";
        ReduceMotion = AppState.Instance.GetPref(KeyReduceMotion, "false") == "true";
        NewTag         = AppState.Instance.GetPref(KeyNewTag,         "true") == "true";
        QualityTags    = AppState.Instance.GetPref(KeyQualityTags,    "true") == "true";
        StandInPosters = AppState.Instance.GetPref(KeyStandInPosters, "true") == "true";
        PlayerPath   = AppState.Instance.GetPref(KeyPlayerPath,   "");
        CheckMissingEpisodes = AppState.Instance.GetPref(KeyMissingEpisodes, "true") == "true";
        SaveFetchedToDrives  = AppState.Instance.GetPref(KeySaveFetched, "true") == "true";

        var lvl = AppState.Instance.GetPref(KeyMicaLevel, "");
        Mica = lvl switch
        {
            "off"    => MicaLevel.Off,
            "subtle" => MicaLevel.Subtle,
            "strong" => MicaLevel.Strong,
            // Migrate the old on/off pref for existing users.
            _ => AppState.Instance.GetPref(KeyMica, "true") == "true" ? MicaLevel.Subtle : MicaLevel.Off,
        };
    }

    public static void SetMica(MicaLevel value)
    {
        if (Mica == value) return;
        Mica = value;
        AppState.Instance.SetPref(KeyMicaLevel,
            value switch { MicaLevel.Off => "off", MicaLevel.Strong => "strong", _ => "subtle" });
        Changed?.Invoke();
    }

    public static void SetCardBorders(bool value)
    {
        if (CardBorders == value) return;
        CardBorders = value;
        AppState.Instance.SetPref(KeyCardBorders, value ? "true" : "false");
        Changed?.Invoke();
    }

    public static void SetCardShadows(bool value)
    {
        if (CardShadows == value) return;
        CardShadows = value;
        AppState.Instance.SetPref(KeyCardShadows, value ? "true" : "false");
        Changed?.Invoke();
    }

    public static void SetNewTag(bool value)
    {
        if (NewTag == value) return;
        NewTag = value;
        AppState.Instance.SetPref(KeyNewTag, value ? "true" : "false");
        Changed?.Invoke();
    }

    public static void SetQualityTags(bool value)
    {
        if (QualityTags == value) return;
        QualityTags = value;
        AppState.Instance.SetPref(KeyQualityTags, value ? "true" : "false");
        Changed?.Invoke();
    }

    public static void SetStandInPosters(bool value)
    {
        if (StandInPosters == value) return;
        StandInPosters = value;
        AppState.Instance.SetPref(KeyStandInPosters, value ? "true" : "false");
        Changed?.Invoke();
    }

    /// <summary>No Changed event: the player choice doesn't affect any visible UI.</summary>
    public static void SetPlayerPath(string value)
    {
        PlayerPath = value;
        AppState.Instance.SetPref(KeyPlayerPath, value);
    }

    /// <summary>No Changed event: it applies the next time a show page opens.</summary>
    public static void SetCheckMissingEpisodes(bool value)
    {
        CheckMissingEpisodes = value;
        AppState.Instance.SetPref(KeyMissingEpisodes, value ? "true" : "false");
    }

    /// <summary>No Changed event; turning it on catches up in the background.</summary>
    public static void SetSaveFetchedToDrives(bool value)
    {
        SaveFetchedToDrives = value;
        AppState.Instance.SetPref(KeySaveFetched, value ? "true" : "false");
        if (value) _ = Task.Run(() => { try { AppState.Instance.SyncFetched(); } catch { } });
    }

    public static void SetReduceMotion(bool value)
    {
        if (ReduceMotion == value) return;
        ReduceMotion = value;
        AppState.Instance.SetPref(KeyReduceMotion, value ? "true" : "false");
        Changed?.Invoke();
    }
}
