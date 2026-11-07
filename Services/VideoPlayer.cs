using System.Diagnostics;
using System.Text.Json.Serialization;

namespace CineLibraryCS.Services;

/// <summary>v4.10.0: a movie or an episode, for the built-in player's resume and watched.</summary>
public enum PlaybackKind { Movie, Episode }

/// <summary>v4.10.0: what Play plays; the title shows in the player's title bar.</summary>
public sealed record PlaybackItem(PlaybackKind Kind, int Id, string Title);

/// <summary>
/// v4.10.0: which audio and subtitle tracks to use for an item: a language (VLC's name for
/// it) and which of the tracks in that language, chosen in the player or in Settings.
/// </summary>
public sealed record TrackChoice(
    [property: JsonPropertyName("audio")] string? Audio,
    [property: JsonPropertyName("audioIndex")] int AudioIndex,
    [property: JsonPropertyName("subtitles")] string? Subtitles,
    [property: JsonPropertyName("subtitleIndex")] int SubtitleIndex,
    [property: JsonPropertyName("subtitlesOff")] bool SubtitlesOff,
    [property: JsonPropertyName("subtitleDelayMs")] int SubtitleDelayMs);

/// <summary>v4.10.0: where Play opened the video.</summary>
public enum PlayedWith { Nothing, BuiltIn, External }

/// <summary>
/// v3.6.0: one place every Play button goes through. Opens the video in the
/// player chosen in Settings; with none chosen, or if that program has since
/// been removed, it falls back to whatever Windows opens video files with.
/// v4.10.0: CineLibrary's own player first (Settings > Play in CineLibrary).
/// </summary>
public static class VideoPlayer
{
    /// <summary>
    /// <paramref name="finished"/> runs on the UI thread each time the built-in player
    /// finishes an item (this one, or a next episode it went on to), with whether it
    /// newly marked it watched. An external player never reports back.
    /// </summary>
    public static async Task<PlayedWith> PlayAsync(string videoPath, PlaybackItem? item = null,
                                                   Action<PlaybackItem, bool>? finished = null)
    {
        WaitingPlay.Cancel();   // 5.3.0: something else plays, so a film waiting for its drive doesn't start over it
        // 5.3.0: played here, its saved place says whether it's in Continue Watching; played in another
        // player (also one a file goes to when VLC can't play it), it stays there
        var film = item is { Kind: PlaybackKind.Movie } m ? m : null;
        async Task<bool> ElsewhereAsync(string path)
        {
            if (!await PlayExternalAsync(path)) return false;
            if (film != null && AppState.Instance.Db.SetPlayedElsewhere(film.Id, true))
                AppState.Instance.SavePlaybackState(false, film.Id);   // into its state file
            return true;
        }
        if (UiSettings.UseBuiltInPlayer && film != null) AppState.Instance.Db.SetPlayedElsewhere(film.Id, false);
        if (UiSettings.UseBuiltInPlayer
            && await Playback.BuiltInPlayer.PlayAsync(videoPath, item, finished, ElsewhereAsync))
            return PlayedWith.BuiltIn;
        if (!await ElsewhereAsync(videoPath)) return PlayedWith.Nothing;
        // 5.3.0: that player never says how far it got; back in CineLibrary later, it's asked
        if (film != null && AppState.Instance.Db.GetMovieDetail(film.Id, AppState.Instance.Connected) is { IsWatched: false } movie)
            ExternalPlay.Started(film.Id, movie.Title, movie.Runtime ?? movie.DurationSeconds / 60);
        return PlayedWith.External;
    }

    private static async Task<bool> PlayExternalAsync(string videoPath)
    {
        var player = UiSettings.PlayerPath;
        if (player.Length > 0 && File.Exists(player))
        {
            try
            {
                var psi = new ProcessStartInfo(player) { UseShellExecute = false };
                psi.ArgumentList.Add(videoPath);
                Process.Start(psi)?.Dispose();
                return true;
            }
            catch { /* fall back to the Windows default below */ }
        }
        return await Windows.System.Launcher.LaunchUriAsync(new Uri(videoPath));
    }
}
