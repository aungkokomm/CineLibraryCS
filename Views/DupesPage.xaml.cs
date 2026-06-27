using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using CineLibraryCS.Models;
using CineLibraryCS.Services;

namespace CineLibraryCS.Views;

/// <summary>
/// v3.4.4 — Tools → Dupes. A review tool for movies the library holds more than
/// once. Groups are matched by TMDb/IMDb id (else title + year) and classified
/// conservatively, so deliberately-kept variants (dubs, editions, quality) are
/// shown as kept-on-purpose. For real duplicates it recommends a keeper, shows
/// reclaimable space, and offers per-copy actions. The app never deletes files.
/// </summary>
public sealed partial class DupesPage : Page
{
    public event EventHandler? SidebarRefreshRequested;

    private List<DupeGroup> _all = new();
    private int _posterToken;
    private bool _isLoaded;

    public DupesPage()
    {
        InitializeComponent();
        Loaded += (_, _) => _isLoaded = true;
        Unloaded += (_, _) => _isLoaded = false;
        // When the user comes back from Explorer (e.g. after deleting a copy),
        // re-check files on disk so a now-resolved set drops off on its own —
        // no manual Rescan needed.
        if (App.MainWindow is Window w) w.Activated += OnWindowActivated;
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState != WindowActivationState.Deactivated && _isLoaded)
            Refresh();
    }

    public async void Refresh()
    {
        var connected = AppState.Instance.Connected;
        _all = await System.Threading.Tasks.Task.Run(
            () => AppState.Instance.Db.GetDuplicateGroups(connected));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        bool onlyDupes = OnlyDupesCheck.IsChecked == true;
        bool showIgnored = ShowIgnoredCheck.IsChecked == true;

        var shown = new List<DupeGroup>();
        int possible = 0, ignored = 0;
        long reclaimable = 0;
        foreach (var g in _all)
        {
            if (g.IsIgnored) ignored++;
            else if (g.PossibleDuplicate) { possible++; reclaimable += g.ReclaimableBytes; }

            if (!showIgnored && g.IsIgnored) continue;
            if (onlyDupes && !g.PossibleDuplicate) continue;
            shown.Add(g);
        }

        GroupsRepeater.ItemsSource = shown;
        CountText.Text = _all.Count == 0
            ? ""
            : $"{possible} possible duplicate{(possible == 1 ? "" : "s")} · {_all.Count} group{(_all.Count == 1 ? "" : "s")}"
              + (ignored > 0 ? $" · {ignored} ignored" : "");
        ReclaimHeadline.Text = reclaimable > 0 ? $"~{DupeCopy.HumanSize(reclaimable)} reclaimable" : "";
        EmptyState.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        _ = LoadPostersAsync(shown, ++_posterToken);
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e) => ApplyFilter();

    /// <summary>Re-check files on disk and rebuild — used after the user deletes
    /// a copy. Deleted copies drop out, so a now-single-copy set disappears.</summary>
    private void OnRescan(object sender, RoutedEventArgs e) => Refresh();

    /// <summary>Trickle-load the keeper poster for each visible group (small set).</summary>
    private async System.Threading.Tasks.Task LoadPostersAsync(List<DupeGroup> groups, int token)
    {
        foreach (var g in groups)
        {
            if (token != _posterToken) return;
            if (g.Poster != null || g.Copies.Count == 0) continue;
            var rel = g.Copies[0].LocalPoster;
            if (string.IsNullOrEmpty(rel)) continue;
            var full = AppState.Instance.Db.GetCachedImagePath(rel);
            if (full == null) continue;
            try
            {
                var bytes = await System.Threading.Tasks.Task.Run(() => System.IO.File.ReadAllBytes(full));
                if (token != _posterToken) return;
                var bmp = new BitmapImage { DecodePixelWidth = 110 };
                using var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                await ms.WriteAsync(bytes.AsBuffer());
                ms.Seek(0);
                await bmp.SetSourceAsync(ms);
                if (token != _posterToken) return;
                g.Poster = bmp;
            }
            catch { /* unreadable poster → initial letter stays */ }
        }
    }

    private async void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DupeCopy copy) return;
        if (!copy.IsOnline || copy.CurrentLetter == null || copy.FolderRelPath == null)
        {
            if (App.MainWindow is MainWindow mw) mw.ShowToast("That copy's drive is offline.");
            return;
        }
        var folder = System.IO.Path.Combine($"{copy.CurrentLetter}:\\",
            copy.FolderRelPath.Replace('/', '\\'));
        try { await Windows.System.Launcher.LaunchFolderPathAsync(folder); }
        catch
        {
            if (App.MainWindow is MainWindow mw2) mw2.ShowToast("Couldn't open that folder.");
        }
    }

    private void OnSendToWg(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DupeCopy copy) return;
        AppState.Instance.Db.ArchiveMovies(new[] { copy.Id });
        if (App.MainWindow is MainWindow mw) mw.ShowToast("Sent to Watched & Gone");
        SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
        Refresh();
    }

    private void OnToggleIgnore(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DupeGroup g) return;
        AppState.Instance.Db.SetDupeIgnored(g.Key, !g.IsIgnored);
        g.IsIgnored = !g.IsIgnored;
        ApplyFilter();
    }
}
