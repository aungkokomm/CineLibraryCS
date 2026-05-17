using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CineLibraryCS.Models;
using CineLibraryCS.Services;
using DriveInfo = CineLibraryCS.Models.DriveInfo;
using System.Collections.ObjectModel;

namespace CineLibraryCS.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AppState _state;

    [ObservableProperty] private ObservableCollection<DriveInfo> _drives = new();
    [ObservableProperty] private ObservableCollection<Collection> _collections = new();
    [ObservableProperty] private ObservableCollection<GenreFacet> _topGenres = new();
    [ObservableProperty] private LibraryStats? _stats;
    [ObservableProperty] private string? _toastMessage;
    [ObservableProperty] private bool _toastVisible;

    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;

    public MainViewModel()
    {
        _state = AppState.Instance;
        _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    }

    public async Task InitializeAsync()
    {
        await Task.Run(() =>
        {
            _state.RefreshConnected();
            _prevConnected = _state.Connected.ToDictionary(kv => kv.Key, _ => true);
        });
        await RefreshSidebarAsync();
        // Drive change detection is now event-driven via WM_DEVICECHANGE —
        // see DeviceChangeWatcher wired in MainWindow.xaml.cs. No polling.
    }

    public async Task RefreshSidebarAsync()
    {
        // Fetch all sidebar data in a single Task.Run — the DatabaseService
        // methods are [MethodImpl(Synchronized)] so parallel Task.Runs would
        // just serialize anyway, and one background hop is cheaper than four.
        var data = await Task.Run(() => (
            Drives: _state.Db.GetDrives(),
            Collections: _state.Db.GetCollections(),
            Genres: _state.Db.GetTopGenres(8),
            Stats: _state.Db.GetStats()
        ));

        var drives = data.Drives;
        var collections = data.Collections;
        var genres = data.Genres;
        var stats = data.Stats;

        Drives.Clear();
        foreach (var d in drives) Drives.Add(d);
        Collections.Clear();
        foreach (var c in collections) Collections.Add(c);
        TopGenres.Clear();
        foreach (var g in genres) TopGenres.Add(g);
        Stats = stats;
    }

    // ── Drive change (WM_DEVICECHANGE driven) ─────────────────────────────

    private Dictionary<string, bool> _prevConnected = new();

    /// <summary>
    /// Called from DeviceChangeWatcher on the UI thread when Windows reports
    /// a volume arrival/removal. Refreshes the connected set, raises a toast
    /// on new drives, and triggers a sidebar refresh.
    /// </summary>
    public async Task OnDeviceChangeAsync()
    {
        if (_shuttingDown) return;
        // Debounce — USB hubs fire 3–5 WM_DEVICECHANGE messages within ms
        // when a stick is plugged in. Coalesce into one refresh.
        var myCall = ++_deviceChangeCallId;
        await Task.Delay(150);
        if (_shuttingDown) return;
        if (myCall != _deviceChangeCallId) return;

        var prev = _prevConnected;
        await Task.Run(() => _state.RefreshConnected());
        if (_shuttingDown) return;
        var curr = _state.Connected;

        foreach (var drive in Drives)
        {
            // Read the previous CONNECTED state, not "ever seen". Without
            // this, a drive that disconnects then reconnects in the same
            // session never produces a toast because TryGetValue always
            // succeeds for any drive that was once present.
            prev.TryGetValue(drive.VolumeSerial, out var wasConnected);
            var nowConnected = curr.ContainsKey(drive.VolumeSerial);
            if (!wasConnected && nowConnected)
                ShowToast($"Drive '{drive.Label}' connected");
        }

        // Track every known drive's CONNECTED state (true / false), not just
        // presence — so disconnect→reconnect cycles produce a toast.
        var next = new Dictionary<string, bool>(prev);
        foreach (var drive in Drives)
            next[drive.VolumeSerial] = curr.ContainsKey(drive.VolumeSerial);
        _prevConnected = next;

        await RefreshSidebarAsync();
    }

    private int _deviceChangeCallId;

    // ── Toast ─────────────────────────────────────────────────────────────

    private System.Threading.Timer? _toastTimer;

    public void ShowToast(string message)
    {
        ToastMessage = message;
        ToastVisible = true;
        _toastTimer?.Dispose();
        _toastTimer = new System.Threading.Timer(_ =>
        {
            ToastVisible = false;
        }, null, 6000, Timeout.Infinite);
    }

    public void DismissToast() => ToastVisible = false;

    // ── Shutdown ──────────────────────────────────────────────────────────
    // Timers fire on ThreadPool threads. Without explicit disposal they can
    // tick during Close() and race with SqliteConnection teardown, producing
    // STATUS_STACK_BUFFER_OVERRUN (0xC0000409). Dispose() is called from
    // MainWindow.Closed BEFORE the native stack frame unwinds.

    private volatile bool _shuttingDown;
    public bool IsShuttingDown => _shuttingDown;

    public void Shutdown()
    {
        _shuttingDown = true;
        try { _toastTimer?.Dispose(); } catch { }
        _toastTimer = null;
    }

    // ── Export ────────────────────────────────────────────────────────────

    public async Task ExportCsvAsync(IEnumerable<MovieListItem> movies, string filePath)
    {
        await Task.Run(() =>
        {
            using var sw = new System.IO.StreamWriter(filePath, false, System.Text.Encoding.UTF8);
            sw.WriteLine("Title,Year,Rating,Runtime,Genres,Drive,Watched,Favorite,IMDB");
            foreach (var m in movies)
            {
                var detail = _state.Db.GetMovieDetail(m.Id, _state.Connected);
                sw.WriteLine(
                    $"\"{Esc(m.Title)}\",{m.Year},{m.Rating:F1},{m.Runtime}," +
                    $"\"{Esc(m.GenresCsv)}\",\"{Esc(m.DriveLabel)}\"," +
                    $"{(m.IsWatched ? "Yes" : "No")},{(m.IsFavorite ? "Yes" : "No")}," +
                    $"{detail?.ImdbUrl}");
            }
        });
    }

    public async Task ExportHtmlAsync(IEnumerable<MovieListItem> movies, string filePath)
    {
        await Task.Run(() =>
        {
            var rows = new System.Text.StringBuilder();
            int i = 0;
            foreach (var m in movies)
            {
                var detail = _state.Db.GetMovieDetail(m.Id, _state.Connected);
                var imdb = detail?.ImdbId != null ? $"<a href='https://www.imdb.com/title/{detail.ImdbId}/' style='color:#f5c518'>{detail.ImdbId}</a>" : "";
                rows.AppendLine($"<tr><td>{++i}</td><td>{System.Net.WebUtility.HtmlEncode(m.Title)}</td><td>{m.Year}</td><td>{m.Rating:F1}</td><td>{m.Runtime}</td><td>{System.Net.WebUtility.HtmlEncode(m.GenresCsv ?? "")}</td><td>{System.Net.WebUtility.HtmlEncode(m.DriveLabel ?? "")}</td><td>{(m.IsWatched ? "✓" : "")}</td><td>{imdb}</td></tr>");
            }
            var html = $@"<!DOCTYPE html><html><head><meta charset='utf-8'/><title>CineLibrary Export</title>
<style>body{{background:#0a0a0c;color:#e0e0e0;font-family:system-ui;padding:20px}}
table{{border-collapse:collapse;width:100%}}th,td{{border:1px solid #333;padding:8px;text-align:left}}
th{{background:#1a1a2e;color:#a78bfa}}tr:nth-child(even){{background:#111122}}
tr:hover{{background:#1e1e3a}}</style></head>
<body><h1 style='color:#a78bfa'>CineLibrary Export — {DateTime.Now:yyyy-MM-dd}</h1>
<table><thead><tr><th>#</th><th>Title</th><th>Year</th><th>Rating</th><th>Runtime</th><th>Genres</th><th>Drive</th><th>Watched</th><th>IMDb</th></tr></thead>
<tbody>{rows}</tbody></table></body></html>";
            File.WriteAllText(filePath, html, System.Text.Encoding.UTF8);
        });
    }

    private static string Esc(string? s) => s?.Replace("\"", "\"\"") ?? "";
}
