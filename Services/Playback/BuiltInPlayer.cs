using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;

namespace CineLibraryCS.Services.Playback;

/// <summary>
/// v4.10.0: Play in CineLibrary's own player. One video at a time, in a player window over
/// the window Play was pressed in. It starts where the item last stopped, saves the
/// position every 10 s, marks the item watched once 90% of it played (never because it
/// "ended": a drive unplugged mid-film ends it too) and offers the next episode.
/// </summary>
internal static class BuiltInPlayer
{
    private const long MinResumeMs = 60_000;   // less than a minute in: start from the beginning next time
    private static PlayerSession? _session;
    private static DispatcherQueueTimer? _saveTimer;

    /// <summary>An item stopped and where it stopped is saved (on the UI thread).</summary>
    public static event Action<PlaybackItem>? Stopped;

    /// <summary>
    /// False when the built-in player couldn't start (the caller then uses the other player).
    /// A file VLC turns out unable to play goes to <paramref name="fallback"/>.
    /// </summary>
    public static async Task<bool> PlayAsync(string path, PlaybackItem? item, Action<PlaybackItem, bool>? finished,
                                             Func<string, Task<bool>> fallback)
    {
        var owner = OwnerWindow();
        // The previous film closes on its own (and records where it stopped): a stop stuck
        // on a stalled drive doesn't hold up this one
        if (_session is { } previous)
        {
            if (owner == previous.Hwnd) owner = MainHwnd;   // never own the new one by a window that goes
            _session = null;
            _ = previous.CloseAsync();
        }
        if (owner == IntPtr.Zero) return false;

        var db = AppState.Instance.Db;
        GetWindowRect(owner, out var r);
        var startMs = item is null ? 0 : db.GetResumeMs(item.Kind == PlaybackKind.Episode, item.Id);
        var (session, _) = await PlayerSession.OpenAsync(owner, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, IsZoomed(owner),
            path, item?.Title ?? Path.GetFileNameWithoutExtension(path), item, startMs, UiSettings.PlayerVolume, choice: ChoiceFor(item));
        if (session is null) return false;
        _session = session;

        session.SetNeighbours(tag => tag is PlaybackItem { Kind: PlaybackKind.Episode } episode ? Adjacent(episode, next: false) : null,
                              tag => tag is PlaybackItem { Kind: PlaybackKind.Episode } episode ? Adjacent(episode, next: true) : null);
        session.FindPauseInfo = PauseInfoFor;
        session.FindSubtitleFilm = SubtitleFilmFor;
        session.ChoiceChanged += (tag, choice) => { if (tag is PlaybackItem chosen) db.SetTrackChoice(chosen.Kind == PlaybackKind.Episode, chosen.Id, choice); };
        session.ItemStarted += tag => { if (tag is PlaybackItem { Kind: PlaybackKind.Episode } episode) db.MarkEpisodePlayed(episode.Id); };
        session.ItemEnded += (tag, position, length) =>
        {
            if (tag is not PlaybackItem ended) return;
            var watched = Record(ended, position, length);
            try { finished?.Invoke(ended, watched); } catch { /* the page may be gone */ }
            Stopped?.Invoke(ended);
        };

        // Closing the window the player belongs to stops playback first, but a stop stuck on
        // a stalled drive doesn't keep it from closing
        var ownerWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(owner));
        Windows.Foundation.TypedEventHandler<AppWindow, AppWindowClosingEventArgs> ownerClosing = async (_, e) =>
        {
            if (_session != session) return;
            e.Cancel = true;
            _session = null;
            await Task.WhenAny(session.CloseAsync(), Task.Delay(3000));
            PostMessage(owner, WmClose, 0, 0);
        };
        if (ownerWindow != null) ownerWindow.Closing += ownerClosing;

        session.Closed += () =>
        {
            if (ownerWindow != null) ownerWindow.Closing -= ownerClosing;
            UiSettings.SetPlayerVolume(session.Volume);
            if (_session == session) _session = null;
            if (session.FailedToStart) _ = fallback(path);
        };
        StartSaving();
        return true;
    }

    /// <summary>The tracks for an item: as chosen for it before, else as set in Settings.</summary>
    private static TrackChoice ChoiceFor(PlaybackItem? item)
    {
        var saved = item is null ? null : AppState.Instance.Db.GetTrackChoice(item.Kind == PlaybackKind.Episode, item.Id);
        var subtitlesOff = UiSettings.PreferredSubtitles == "Off";
        return new TrackChoice(
            saved?.Audio ?? NullIfEmpty(UiSettings.PreferredAudio), saved?.AudioIndex ?? 0,
            saved?.Subtitles ?? (subtitlesOff ? null : NullIfEmpty(UiSettings.PreferredSubtitles)), saved?.SubtitleIndex ?? 0,
            saved?.SubtitlesOff == true || saved?.Subtitles is null && subtitlesOff,
            saved?.SubtitleDelayMs ?? 0);
    }

    private static string? NullIfEmpty(string text) => text.Length == 0 ? null : text;

    /// <summary>The pause screen's details, from the library.</summary>
    private static PauseInfo? PauseInfoFor(object? tag)
    {
        if (!UiSettings.PauseScreen || tag is not PlaybackItem item) return null;
        var db = AppState.Instance.Db;
        if (item.Kind == PlaybackKind.Movie)
        {
            if (db.GetMovieDetail(item.Id, AppState.Instance.Connected) is not { } movie) return null;
            return new PauseInfo(movie.Title,
                Facts(movie.Year?.ToString(), movie.Runtime ?? movie.DurationSeconds / 60, movie.Rating, movie.Genres),
                movie.Plot ?? movie.Outline ?? "",
                People(movie.Directors, movie.Actors),
                PosterPath(movie.LocalPoster));
        }
        if (db.GetEpisodeDetail(item.Id) is not { } episode) return null;
        var show = db.GetTvShowDetail(episode.ShowId);
        return new PauseInfo($"{episode.ShowTitle}: S{episode.Season:D2}E{episode.Episode:D2}",
            string.Join(" · ", new[] { episode.Title, Facts(null, episode.Runtime ?? episode.DurationSeconds / 60, episode.Rating, []) }.Where(s => s.Length > 0)),
            episode.Plot ?? show?.Plot ?? "",
            show is null ? "" : People([], show.Actors),
            PosterPath(show?.LocalPoster));
    }

    /// <summary>What to look for subtitles online by, from the library.</summary>
    private static SubtitleFilm? SubtitleFilmFor(object? tag)
    {
        if (tag is not PlaybackItem item) return null;
        var db = AppState.Instance.Db;
        if (item.Kind == PlaybackKind.Movie)
            return db.GetMovieDetail(item.Id, AppState.Instance.Connected) is { } movie ? new SubtitleFilm(movie.Title, movie.Year, movie.ImdbId, null, null) : null;
        return db.GetEpisodeDetail(item.Id) is { } episode
            ? new SubtitleFilm(episode.ShowTitle, null, db.GetTvShowDetail(episode.ShowId)?.ImdbId, episode.Season, episode.Episode)
            : null;
    }

    private static string Facts(string? year, int? minutes, double? rating, IEnumerable<string> genres)
    {
        var facts = new List<string>();
        if (year != null) facts.Add(year);
        if (minutes is > 0) facts.Add(minutes >= 60 ? $"{minutes / 60}h {minutes % 60}m" : $"{minutes} min");
        if (rating is > 0) facts.Add($"★ {rating:0.0}");
        facts.AddRange(genres.Take(3));
        return string.Join(" · ", facts);
    }

    private static string People(IEnumerable<string> directors, IEnumerable<Models.Actor> actors)
    {
        var lines = new List<string>();
        var by = string.Join(", ", directors.Take(2));
        if (by.Length > 0) lines.Add("Directed by " + by);
        var cast = string.Join(", ", actors.Take(5).Select(a => a.Name));
        if (cast.Length > 0) lines.Add("With " + cast);
        return string.Join(Environment.NewLine, lines);
    }

    private static string? PosterPath(string? relative) => relative is null ? null : AppState.Instance.Db.GetCachedImagePath(relative);

    /// <summary>Where an item stopped: watched from 90%, else where to start next time.
    /// True when it was newly marked watched (watching it again changes nothing).</summary>
    private static bool Record(PlaybackItem item, long position, long length)
    {
        var db = AppState.Instance.Db;
        var episode = item.Kind == PlaybackKind.Episode;
        if (length > 0 && position >= length * 0.9)
        {
            db.SetResumeMs(episode, item.Id, 0);
            if (!episode) return db.SetMovieWatched(item.Id);
            if (db.IsEpisodeWatched(item.Id)) return false;
            db.SetEpisodeWatched(item.Id, true);
            return true;
        }
        db.SetResumeMs(episode, item.Id, position >= MinResumeMs ? position : 0);
        AppState.Instance.SavePlaybackState(episode, item.Id);
        return false;
    }

    /// <summary>The next (or previous) episode of the show, when its drive is here.</summary>
    private static NextUp? Adjacent(PlaybackItem episode, bool next)
    {
        var db = AppState.Instance.Db;
        if (db.GetAdjacentEpisode(episode.Id, next) is not { } other
            || !AppState.Instance.Connected.TryGetValue(other.VolumeSerial, out var letter)) return null;
        var path = Path.Combine($"{letter}:\\", other.File.Replace('/', '\\'));
        if (!File.Exists(path)) return null;
        var title = other.Title.Length > 0 ? $"{other.Code} {other.Title}" : other.Code;
        var item = new PlaybackItem(PlaybackKind.Episode, other.Id, title);
        return new NextUp(path, title, other.Title.Length > 0 ? $"{other.Code} · {other.Title}" : other.Code, item, db.GetResumeMs(true, other.Id), ChoiceFor(item));
    }

    /// <summary>Every 10 s while playing, so a crash or a power cut loses little.</summary>
    private static void StartSaving()
    {
        if (_saveTimer != null) return;
        var ui = DispatcherQueue.GetForCurrentThread();
        _saveTimer = ui.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromSeconds(10);
        _saveTimer.Tick += (_, _) => ui.Post(() =>
        {
            if (_session is not { Tag: PlaybackItem item } s || !s.IsPlaying || s.Position < MinResumeMs) return;
            try { AppState.Instance.Db.SetResumeMs(item.Kind == PlaybackKind.Episode, item.Id, s.Position); }
            catch { /* best effort */ }
        });
        _saveTimer.Start();
    }

    /// <summary>The window Play was pressed in (the movie window covers the hidden main one), else the main window.</summary>
    private static IntPtr OwnerWindow()
    {
        var active = GetActiveWindow();
        if (active != IntPtr.Zero && GetAncestor(active, GaRoot) == active && IsWindowVisible(active)) return active;
        return MainHwnd;
    }

    private static IntPtr MainHwnd => App.MainWindow is { } main ? WinRT.Interop.WindowNative.GetWindowHandle(main) : IntPtr.Zero;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const uint WmClose = 0x0010;
    private const uint GaRoot = 2;

    [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, nint wParam, nint lParam);
}
