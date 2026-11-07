using CineLibraryCS.Services.Playback;

namespace CineLibraryCS.Services;

/// <summary>
/// 5.3.0: reads the sound and subtitle languages inside the films whose languages aren't known (the
/// .nfo doesn't say, never read), so the library's Audio filter finds them. Nothing goes online: VLC
/// lists the file's tracks. Once for each film, one file at a time, and never while the player has a
/// video open. Runs a while after the app opens and again when a drive is connected.
/// </summary>
internal static class LanguageSweep
{
    internal static bool Disabled;   // the tests switch it off: it would compete with what they measure
    private static int _running, _wanted;
    private static readonly HashSet<int> Tried = new();   // VLC couldn't read it in time: not again this session

    public static void Start(TimeSpan delay)
    {
        if (Disabled) return;
        Interlocked.Increment(ref _wanted);
        if (Interlocked.Exchange(ref _running, 1) == 1) return;   // the one running goes round again
        _ = Task.Run(async () =>
        {
            var seen = 0;
            try
            {
                await Task.Delay(delay);
                do { seen = Volatile.Read(ref _wanted); await RunAsync(); }
                while (seen != Volatile.Read(ref _wanted));
            }
            catch (Exception e) { PlayerLog.Write("languages: " + e.Message); }
            finally
            {
                Volatile.Write(ref _running, 0);
                if (seen != Volatile.Read(ref _wanted)) Start(TimeSpan.Zero);   // asked for while it was finishing
            }
        });
    }

    private static async Task RunAsync()
    {
        var state = AppState.Instance;
        var films = state.Db.GetFilmsWithoutLanguages(new Dictionary<string, string>(state.Connected));
        var read = 0;
        foreach (var (id, path) in films)
        {
            while (BuiltInPlayer.IsOpen) await Task.Delay(5000);
            lock (Tried) if (Tried.Contains(id)) continue;
            try
            {
                if (!File.Exists(path)) continue;   // its drive went
                var size = new FileInfo(path).Length;
                if (await FileLanguages.ReadAsync(path) is not { } languages)
                {
                    lock (Tried) Tried.Add(id);
                    continue;
                }
                state.Db.SetFileLanguages(id, languages.Audio, languages.Subtitles, size);
                read++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException) { }
            await Task.Delay(200);   // gently, for the drive
        }
        if (films.Count > 0) PlayerLog.Write($"languages: read {read} of {films.Count} films");
    }
}
