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

    /// <summary>Faint resting drop shadow on movie cards. Default off.</summary>
    public static bool CardShadows { get; private set; }

    /// <summary>Disable the card hover zoom/lift animation. Default off.</summary>
    public static bool ReduceMotion { get; private set; }

    /// <summary>Raised after any setting changes so live UI can re-apply it.</summary>
    public static event Action? Changed;

    /// <summary>Load persisted values. Call once at startup, after AppState.Initialize().</summary>
    public static void Load()
    {
        CardShadows  = AppState.Instance.GetPref(KeyCardShadows,  "false") == "true";
        ReduceMotion = AppState.Instance.GetPref(KeyReduceMotion, "false") == "true";
    }

    public static void SetCardShadows(bool value)
    {
        if (CardShadows == value) return;
        CardShadows = value;
        AppState.Instance.SetPref(KeyCardShadows, value ? "true" : "false");
        Changed?.Invoke();
    }

    public static void SetReduceMotion(bool value)
    {
        if (ReduceMotion == value) return;
        ReduceMotion = value;
        AppState.Instance.SetPref(KeyReduceMotion, value ? "true" : "false");
        Changed?.Invoke();
    }
}
