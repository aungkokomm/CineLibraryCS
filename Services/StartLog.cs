using System.Diagnostics;

namespace CineLibraryCS.Services;

/// <summary>
/// v4.10.0 test builds: the first 30 seconds of each start, then every fault until it ends, in
/// CineLibrary-Data\start.log. A fatal WinUI error ends the process without reaching any handler,
/// so each stage and each exception is written at the moment it happens.
/// </summary>
internal static class StartLog
{
    private const int WatchMs = 30_000, MaxBytes = 1_000_000, FaultsPerFile = 200;
    private static readonly object Gate = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static StreamWriter? _file;
    private static string _path = "";
    private static bool _started;
    private static int _faults;
    [ThreadStatic] private static bool _writing;

    public static void Begin()
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "CineLibrary-Data");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "start.log");
            var keep = File.Exists(_path) && new FileInfo(_path).Length < MaxBytes;
            _file = new StreamWriter(_path, append: keep) { AutoFlush = true };
            _file.WriteLine();
            _file.WriteLine($"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} pid {Environment.ProcessId}, built {File.GetLastWriteTime(Environment.ProcessPath!):yyyy-MM-dd HH:mm}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }
        AppDomain.CurrentDomain.FirstChanceException += OnException;
        _ = Task.Delay(WatchMs).ContinueWith(_ => End());
    }

    public static void Mark(string stage)
    {
        lock (Gate)
        {
            try { _file?.WriteLine($"{Clock.Elapsed.TotalSeconds,7:0.000}  {stage}"); }
            catch (IOException) { }
        }
    }

    private static void OnException(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
    {
        if (_writing) return;
        // Past the start, each keystroke in a search cancels a wait: not a fault, and a long
        // session must not push the last fault before a crash out of the log
        if (_started && e.Exception is OperationCanceledException) return;
        _writing = true;
        if (_started && ++_faults % FaultsPerFile == 0) Rotate();
        Mark($"{e.Exception.GetType().Name} on thread {Environment.CurrentManagedThreadId}: {e.Exception.Message}\n{Environment.StackTrace}");
        _writing = false;
    }

    private static void End()
    {
        _started = true;
        Mark("still running after 30 s, faults only from here");
    }

    private static void Rotate()
    {
        lock (Gate)
        {
            try
            {
                _file?.Dispose();
                File.Move(_path, Path.ChangeExtension(_path, ".previous.log"), overwrite: true);
                _file = new StreamWriter(_path, append: false) { AutoFlush = true };
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _file = null;
            }
        }
    }
}
