using System.Diagnostics;
using LibVLCSharp.Shared;

namespace CineLibraryCS.Services.Playback;

/// <summary>
/// v4.10.0 test builds: what the player and VLC did during the last play, in
/// CineLibrary-Data\player.log (the play before in player-previous.log), to find what
/// goes wrong on a real screen. Each line is written at once, so a hang keeps it.
/// </summary>
internal static class PlayerLog
{
    private const int MaxLines = 50_000;   // a film that lags all the way stays a few MB
    private static readonly object Gate = new();
    private static readonly Stopwatch Clock = new();
    private static StreamWriter? _file;
    private static int _lines;

    /// <summary>A new log for a new play; the last one becomes player-previous.log.</summary>
    public static void Start(string what)
    {
        lock (Gate)
        {
            _file?.Dispose();
            _file = null;
            try
            {
                var dir = AppState.Instance.DataDir;
                var path = Path.Combine(dir, "player.log");
                if (File.Exists(path)) File.Copy(path, Path.Combine(dir, "player-previous.log"), overwrite: true);
                _file = new StreamWriter(path, append: false) { AutoFlush = true };
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return;
            }
            _lines = 0;
            Clock.Restart();
            _file.WriteLine($"CineLibrary {typeof(PlayerLog).Assembly.GetName().Version} player log, {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }
        Write(what);
    }

    public static void Write(string line)
    {
        lock (Gate)
        {
            if (_file is null || _lines > MaxLines) return;
            try
            {
                _file.WriteLine(++_lines > MaxLines ? "(log full)" : $"{Clock.Elapsed.TotalSeconds,9:0.000}  {line}");
            }
            catch (IOException) { }
        }
    }

    /// <summary>VLC's warnings and errors, and the modules it starts and stops (decoders, video output).</summary>
    public static void OnVlcLog(object? sender, LogEventArgs e)
    {
        if (_file is null) return;
        var keep = e.Level >= LogLevel.Warning ? !e.Message.StartsWith("option ") : e.Module == "main" && e.Message.Contains("module");
        if (keep) Write($"vlc {e.Level} {e.Module}: {e.Message}");
    }
}
