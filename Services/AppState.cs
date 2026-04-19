using CineLibraryCS.Models;

namespace CineLibraryCS.Services;

/// <summary>
/// Singleton app-wide state and service locator.
/// </summary>
public class AppState
{
    private static AppState? _instance;
    public static AppState Instance => _instance ??= new AppState();

    public DatabaseService Db { get; private set; } = null!;
    public ScannerService Scanner { get; private set; } = null!;
    public string DataDir { get; private set; } = "";

    public void Initialize()
    {
        DataDir = GetDataDir();
        Db = new DatabaseService(DataDir);
        Scanner = new ScannerService(Db);
    }

    private static string GetDataDir()
    {
        // Walk up from bin/x64/Debug/net8.0-windows... to project root during dev
        var exeDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(exeDir);
        // In VS, output is typically: ProjectDir\bin\x64\Debug\net8.0-windows...\
        // Walk up until we find a .csproj or we've gone up 5 levels
        for (int i = 0; i < 5; i++)
        {
            if (dir == null) break;
            if (dir.GetFiles("*.csproj").Length > 0)
            {
                // In dev: put data next to the .csproj
                var devData = Path.Combine(dir.FullName, "CineLibrary-Data");
                Directory.CreateDirectory(devData);
                return devData;
            }
            dir = dir.Parent;
        }
        // In production: put data next to the exe
        var prodData = Path.Combine(exeDir, "CineLibrary-Data");
        Directory.CreateDirectory(prodData);
        return prodData;
    }

    // ── Cached connected drives (refreshed on a timer) ───────────────────────

    private Dictionary<string, string> _connected = new();

    public Dictionary<string, string> Connected => _connected;

    public void RefreshConnected()
    {
        _connected = Db.GetConnectedDrives();
        foreach (var kv in _connected)
            Db.UpdateDriveLastSeen(kv.Key, kv.Value);
    }

    // ── Preferences ──────────────────────────────────────────────────────────

    public string GetPref(string key, string defaultValue = "")
        => Db.GetPref(key) ?? defaultValue;

    public void SetPref(string key, string value)
        => Db.SetPref(key, value);
}
