using CineLibraryCS.Services.Playback;

namespace CineLibraryCS.Services;

/// <summary>
/// 5.3.0: Play on a film or an episode whose drive isn't connected, and it plays the moment that drive
/// is connected. One waits at a time (the latest asked for), for 20 minutes: a drive plugged in hours
/// later doesn't start anything. Playing anything else ends the wait.
/// </summary>
internal static class WaitingPlay
{
    private static readonly TimeSpan For = TimeSpan.FromMinutes(20);
    private static (PlaybackItem Item, string Serial, string Name, string Drive, DateTime Until)? _waiting;

    /// <param name="name">what it's called in the note: the film, or the show and the episode</param>
    public static void Wait(PlaybackItem item, string serial, string? driveLabel, string name)
    {
        var drive = string.IsNullOrWhiteSpace(driveLabel) ? "its drive" : $"\"{driveLabel}\"";
        _waiting = (item, serial, name, drive, DateTime.UtcNow + For);
        if (App.MainWindow is MainWindow mw)
            mw.ShowToastWithAction($"{name} plays when {drive} is connected", "Cancel", Cancel, ms: 8000);
    }

    public static void Cancel() => _waiting = null;

    /// <summary>A drive came back (on the UI thread): what's waiting for it plays.</summary>
    public static async Task DriveConnectedAsync(string serial)
    {
        if (_waiting is not { } w || w.Serial != serial) return;
        _waiting = null;
        if (DateTime.UtcNow > w.Until) return;
        // A drive just connected may take a moment before its files open
        var path = await Task.Run(async () =>
        {
            for (var i = 0; i < 10; i++)
            {
                try
                {
                    if (FileOf(w.Item) is { } file && File.Exists(file)) return file;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException) { }
                await Task.Delay(500);
            }
            return null;
        });
        var mw = App.MainWindow as MainWindow;
        if (path is null)
        {
            mw?.ShowToast($"Couldn't find {w.Name} on {w.Drive}");
            return;
        }
        var db = AppState.Instance.Db;
        var episode = w.Item.Kind == PlaybackKind.Episode;
        try
        {
            if (!episode) db.MarkPlayed(w.Item.Id);
            var how = await VideoPlayer.PlayAsync(path, w.Item, (_, _) => mw?.RefreshSidebar());
            if (how == PlayedWith.Nothing)
            {
                mw?.ShowToast("Couldn't launch the video player");
                return;
            }
            if (episode)
            {
                // As the show pages do: another player never says how far it got
                db.MarkEpisodePlayed(w.Item.Id);
                if (how == PlayedWith.External) db.SetEpisodeWatched(w.Item.Id, true);
            }
            mw?.RefreshSidebar();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            mw?.ShowToast("Couldn't launch the video player");
        }
    }

    /// <summary>Where a film's or an episode's file is now, on its drive's letter; null while the drive is away.</summary>
    private static string? FileOf(PlaybackItem item)
    {
        var state = AppState.Instance;
        string? file = null, serial = null;
        if (item.Kind == PlaybackKind.Episode)
        {
            if (state.Db.GetEpisodeFile(item.Id) is { } e) (file, serial) = (e.File, e.VolumeSerial);
        }
        else if (state.Db.GetMovieDetail(item.Id, state.Connected) is { VideoFileRelPath: { } f } m) (file, serial) = (f, m.VolumeSerial);
        return file != null && serial != null && state.Connected.TryGetValue(serial, out var letter)
            ? Path.Combine($"{letter}:\\", file.Replace('/', '\\')) : null;
    }
}
