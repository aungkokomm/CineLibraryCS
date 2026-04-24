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
        });
        await RefreshSidebarAsync();
        StartPolling();
    }

    public async Task RefreshSidebarAsync()
    {
        var drives = await Task.Run(() => _state.Db.GetDrives());
        var collections = await Task.Run(() => _state.Db.GetCollections());
        var genres = await Task.Run(() => _state.Db.GetTopGenres(8));
        var stats = await Task.Run(() => _state.Db.GetStats());

        Drives.Clear();
        foreach (var d in drives) Drives.Add(d);
        Collections.Clear();
        foreach (var c in collections) Collections.Add(c);
        TopGenres.Clear();
        foreach (var g in genres) TopGenres.Add(g);
        Stats = stats;
    }

    // ── Drive polling ─────────────────────────────────────────────────────

    private System.Threading.Timer? _pollTimer;
    private Dictionary<string, bool> _prevConnected = new();

    private void StartPolling()
    {
        _pollTimer = new System.Threading.Timer(_ =>
        {
            if (_shuttingDown) return;
            _dispatcherQueue?.TryEnqueue(async () =>
            {
                if (_shuttingDown) return;
                await PollDrivesAsync();
            });
        }, null, 10_000, 10_000);
    }

    private async Task PollDrivesAsync()
    {
        if (_shuttingDown) return;
        var prev = _prevConnected;
        _state.RefreshConnected();
        var curr = _state.Connected;

        // Detect newly connected drives
        foreach (var drive in Drives)
        {
            var wasConnected = prev.TryGetValue(drive.VolumeSerial, out _);
            var nowConnected = curr.ContainsKey(drive.VolumeSerial);
            if (!wasConnected && nowConnected)
            {
                ShowToast($"Drive '{drive.Label}' connected");
            }
        }

        _prevConnected = curr.ToDictionary(kv => kv.Key, kv => true);
        await RefreshSidebarAsync();
    }

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
        try { _pollTimer?.Dispose(); } catch { }
        _pollTimer = null;
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
