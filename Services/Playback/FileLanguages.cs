using LibVLCSharp.Shared;

namespace CineLibraryCS.Services.Playback;

/// <summary>
/// 5.3.0: the languages of the sound and subtitle tracks inside a video, as codes the way the .nfo
/// has them ("hin,eng"). Most .nfo files don't say (two films in three here), so the film's page
/// asks VLC once for each file, and the player saves what it finds when it plays one.
/// </summary>
internal static class FileLanguages
{
    /// <summary>VLC opens the file only to list its tracks. Null when it couldn't in time (a drive
    /// waking up, a file it can't open); then the page asks again next time.</summary>
    public static async Task<(string? Audio, string? Subtitles)?> ReadAsync(string path)
    {
        var (vlc, _) = await VideoEngine.GetAsync();
        if (vlc is null) return null;
        try
        {
            using var media = new Media(vlc, path, FromType.FromPath);
            var status = await media.Parse(MediaParseOptions.ParseLocal, timeout: 15000);
            // Called off the UI thread, this would go on in VLC's own event, and releasing the media
            // there (the using) crashes VLC. From another thread VLC waits until it's done with it.
            await Task.Yield();
            return status == MediaParsedStatus.Done ? FromTracks(media.Tracks) : null;
        }
        catch (Exception e) when (e is VLCException or InvalidOperationException or ObjectDisposedException)
        {
            PlayerLog.Write($"languages of {Path.GetFileName(path)}: {e.Message}");
            return null;
        }
    }

    public static (string? Audio, string? Subtitles) FromTracks(MediaTrack[]? tracks) =>
        (Codes(tracks, TrackType.Audio), Codes(tracks, TrackType.Text));

    // Tracks that don't say, or say "undetermined" or "several", tell nothing
    private static string? Codes(MediaTrack[]? tracks, TrackType type)
    {
        var codes = (tracks ?? []).Where(t => t.TrackType == type)
            .Select(t => t.Language?.Trim().ToLowerInvariant())
            .Where(c => !string.IsNullOrEmpty(c) && c is not ("und" or "mul" or "zxx" or "mis"))
            .Distinct().ToList();
        return codes.Count > 0 ? string.Join(",", codes) : null;
    }
}
