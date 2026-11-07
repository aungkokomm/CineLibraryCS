namespace CineLibraryCS.Services;

/// <summary>
/// 5.3.0: a film played in another player, which never says how far it got. Back in CineLibrary once
/// most of its length has gone by, the window you come back to asks once whether it was finished.
/// </summary>
internal static class ExternalPlay
{
    internal static Func<DateTime> Now = () => DateTime.UtcNow;   // the tests move it on
    private static (int Id, string Title, DateTime Started, TimeSpan Length)? _pending;

    public static void Started(int movieId, string title, int? minutes) =>
        _pending = minutes is > 0 ? (movieId, title, Now(), TimeSpan.FromMinutes(minutes.Value)) : null;

    /// <summary>The film to ask about now, taken so it's asked once. Null when there's none, when less
    /// than 70% of its length has gone by, or when it was played over 12 hours ago.</summary>
    public static (int Id, string Title)? TakeDue()
    {
        if (_pending is not { } p) return null;
        var gone = Now() - p.Started;
        if (gone > TimeSpan.FromHours(12)) { _pending = null; return null; }
        if (gone < p.Length * 0.7) return null;
        _pending = null;
        return (p.Id, p.Title);
    }

    /// <summary>It was: watched, and one more play.</summary>
    public static void MarkFinished(int movieId)
    {
        var db = AppState.Instance.Db;
        db.AddPlay(movieId);
        if (!db.SetMovieWatched(movieId)) AppState.Instance.SavePlaybackState(false, movieId);   // the count, for its state file
    }
}
