using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using CineLibraryCS.Models;
using CineLibraryCS.Services;
using DriveInfo = CineLibraryCS.Models.DriveInfo;
using SysDriveInfo = System.IO.DriveInfo;

namespace CineLibraryCS.Views;

public sealed partial class DrivesPage : Page
{
    public event EventHandler? RefreshRequested;
    public event EventHandler<string>? NavigateToLibrary;

    private CancellationTokenSource? _scanCts;
    private List<DriveInfo> _drives = new();

    public DrivesPage()
    {
        InitializeComponent();
        Refresh();
    }

    public void Refresh()
    {
        AppState.Instance.RefreshConnected();
        _drives = AppState.Instance.Db.GetDrives();
        DrivesRepeater.ItemsSource = _drives;
        EmptyState.Visibility = _drives.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Add drive (no scan — just register it) ────────────────────────────

    private async void OnAddDrive(object sender, RoutedEventArgs e)
    {
        try { await DoAddDriveAsync(); }
        catch (Exception ex) { await ShowInfoDialog("Error", ex.Message); }
    }

    private async Task DoAddDriveAsync()
    {
        var connected = AppState.Instance.Connected;
        if (!connected.Any())
        {
            await ShowInfoDialog("No drives found",
                "No external drives are currently connected. Connect a drive and try again.");
            return;
        }

        var existing = AppState.Instance.Db.GetDrives().Select(d => d.VolumeSerial).ToHashSet();
        var newDrives = new List<(string Serial, string Letter, string VolumeLabel)>();

        foreach (var kv in connected)
        {
            if (!existing.Contains(kv.Key))
            {
                var sdi = SysDriveInfo.GetDrives()
                    .FirstOrDefault(d => d.Name.StartsWith(kv.Value, StringComparison.OrdinalIgnoreCase));
                var label = sdi?.VolumeLabel is { Length: > 0 } vl ? vl : $"Drive ({kv.Value}:)";
                newDrives.Add((kv.Key, kv.Value, label));
            }
        }

        if (!newDrives.Any())
        {
            await ShowInfoDialog("All drives added", "All connected drives are already in your library.");
            return;
        }

        var combo = new ComboBox { MinWidth = 300 };
        foreach (var (_, letter, label) in newDrives)
            combo.Items.Add($"{label} ({letter}:)");
        combo.SelectedIndex = 0;

        var nameBox = new TextBox
        {
            Text = newDrives[0].VolumeLabel,
            PlaceholderText = "Drive label (e.g. Seagate Red 5TB)"
        };

        combo.SelectionChanged += (_, _) =>
        {
            var idx = combo.SelectedIndex;
            if (idx >= 0 && idx < newDrives.Count)
                nameBox.Text = newDrives[idx].VolumeLabel;
        };

        var muted = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Windows.UI.Color.FromArgb(0xFF, 0x90, 0x90, 0xA0));
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Select drive:", FontSize = 13, Foreground = muted });
        panel.Children.Add(combo);
        panel.Children.Add(new TextBlock { Text = "Drive label:", FontSize = 13, Foreground = muted });
        panel.Children.Add(nameBox);
        panel.Children.Add(new TextBlock
        {
            Text = "After adding, use “+ Add Folder” on the drive card to pick which folders to index.",
            FontSize = 12, Foreground = muted, TextWrapping = TextWrapping.Wrap,
        });

        var dialog = new ContentDialog
        {
            Title = "Add Drive",
            Content = panel,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = CineLibraryCS.MainWindow.CurrentTheme,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var selIdx = combo.SelectedIndex;
        if (selIdx < 0 || selIdx >= newDrives.Count) return;
        var (ser, let, _) = newDrives[selIdx];

        var finalLabel = string.IsNullOrWhiteSpace(nameBox.Text) ? $"Drive ({let}:)" : nameBox.Text.Trim();
        AppState.Instance.Db.AddDrive(ser, finalLabel, let);
        Refresh();
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    // ── Add folder to a drive ─────────────────────────────────────────────

    private async void OnAddFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button btn || btn.Tag is not string serial) return;
            var connected = AppState.Instance.Connected;
            if (!connected.TryGetValue(serial, out var letter))
            {
                await ShowInfoDialog("Drive offline",
                    "Connect this drive to add or scan folders on it.");
                return;
            }
            var driveRoot = $"{letter}:\\";
            var drive = _drives.FirstOrDefault(d => d.VolumeSerial == serial);

            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add("*");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder == null) return;

            if (!folder.Path.StartsWith(driveRoot, StringComparison.OrdinalIgnoreCase))
            {
                await ShowInfoDialog("Wrong drive",
                    $"Please choose a folder on drive {letter}: ({driveRoot}).");
                return;
            }

            // Relative path, forward-slash, no trailing slash. Empty string means drive root itself.
            var relPath = Path.GetRelativePath(driveRoot, folder.Path).Replace('\\', '/').TrimEnd('/');
            if (relPath == ".") relPath = "";

            // Prevent duplicates / nested-within-existing
            var existingRoots = AppState.Instance.Db.GetDriveRoots(serial);
            if (existingRoots.Any(x => x.RootPath == relPath))
            {
                await ShowInfoDialog("Already added", $"'{(relPath == "" ? "(drive root)" : relPath)}' is already tracked on this drive.");
                return;
            }

            AppState.Instance.Db.AddDriveRoot(serial, relPath);

            // Now scan just that folder
            await ScanDriveAsync(serial, letter, drive?.Label ?? serial, folder.Path);
        }
        catch (Exception ex) { await ShowInfoDialog("Error", ex.Message); }
    }

    // ── Remove folder ─────────────────────────────────────────────────────

    private async void OnRemoveFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button btn || btn.Tag is not DriveRoot dr) return;
            var drive = _drives.FirstOrDefault(d => d.VolumeSerial == dr.VolumeSerial);

            var dialog = new ContentDialog
            {
                Title = "Remove folder?",
                Content = $"Remove '{dr.DisplayName}' from '{drive?.Label}'? Movies indexed under this folder will be deleted from the library. The actual files on the drive are untouched.",
                PrimaryButtonText = "Remove",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
                RequestedTheme = CineLibraryCS.MainWindow.CurrentTheme,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            AppState.Instance.Db.RemoveDriveRoot(dr.VolumeSerial, dr.RootPath);
            Refresh();
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { await ShowInfoDialog("Error", ex.Message); }
    }

    // ── Update Database (rescan all tracked folders on this drive) ────────

    private async void OnUpdateDatabase(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button btn || btn.Tag is not string serial) return;
            var connected = AppState.Instance.Connected;
            if (!connected.TryGetValue(serial, out var letter))
            {
                await ShowInfoDialog("Drive offline", "This drive is not connected.");
                return;
            }
            var drive = _drives.FirstOrDefault(d => d.VolumeSerial == serial);
            var roots = AppState.Instance.Db.GetDriveRoots(serial);
            if (roots.Count == 0)
            {
                await ShowInfoDialog("No folders", "Add at least one folder to this drive before updating.");
                return;
            }

            var driveRoot = $"{letter}:\\";
            foreach (var r in roots)
            {
                var abs = string.IsNullOrEmpty(r.RootPath)
                    ? driveRoot
                    : Path.Combine(driveRoot, r.RootPath.Replace('/', '\\'));
                await ScanDriveAsync(serial, letter, drive?.Label ?? serial, abs);
            }
        }
        catch (Exception ex) { await ShowInfoDialog("Error", ex.Message); }
    }

    // ── Scan (shared) ─────────────────────────────────────────────────────

    private async Task ScanDriveAsync(string serial, string letter, string label, string? scanFolder = null)
    {
        var driveRoot = $"{letter}:\\";

        var scopeLabel = scanFolder != null && !string.Equals(scanFolder.TrimEnd('\\'), driveRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            ? $"…\\{Path.GetFileName(scanFolder.TrimEnd('\\'))}"
            : label;

        ScanOverlay.Visibility = Visibility.Visible;
        ScanStatusText.Text = $"Scanning {scopeLabel}…";
        ScanDetailText.Text = "Preparing…";

        _scanCts = new CancellationTokenSource();

        var progress = new Progress<ScanProgress>(p =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ScanStatusText.Text = p.Done
                    ? $"Done — {p.Inserted} new, {p.Updated} updated, {p.Skipped} skipped"
                    : $"Scanning {scopeLabel}… ({p.Found} found)";
                ScanDetailText.Text = p.Done ? "" : Path.GetFileName(p.CurrentFolder);
            });
        });

        try
        {
            await AppState.Instance.Scanner.ScanAsync(serial, driveRoot, progress, _scanCts.Token, scanFolder);
            await Task.Delay(1200);
        }
        catch (OperationCanceledException)
        {
            ScanStatusText.Text = "Scan cancelled";
            await Task.Delay(1000);
        }
        catch (Exception ex)
        {
            await ShowInfoDialog("Scan error", ex.Message);
        }
        finally
        {
            ScanOverlay.Visibility = Visibility.Collapsed;
            Refresh();
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnCancelScan(object sender, RoutedEventArgs e)
        => _scanCts?.Cancel();

    /// <summary>
    /// Incremental rescan of every online drive's *configured drive-roots*
    /// (NOT the whole drive — that was the v1 bug that pulled TV episode
    /// nfos in as fake movies). For each user-added folder, walks just that
    /// folder; the scanner's mtime check skips nfos unchanged since last scan.
    /// Also runs a cleanup pass to remove any stray rows that the v1 bug
    /// might have inserted on a previous run.
    /// </summary>
    private async void OnRefreshChanges(object sender, RoutedEventArgs e)
    {
        var connected = AppState.Instance.Connected;
        var drives = AppState.Instance.Db.GetDrives()
                          .Where(d => connected.ContainsKey(d.VolumeSerial))
                          .ToList();
        if (drives.Count == 0)
        {
            await ShowInfoDialog("No online drives", "Plug in at least one drive that has movies on it, then try again.");
            return;
        }

        // Build the per-drive-root work list — these are the only paths we
        // should touch. A drive without any drive-root entries is skipped.
        var work = new List<(string Serial, string Letter, string Label, string DriveRoot, string ScanFolder)>();
        foreach (var d in drives)
        {
            var letter = connected[d.VolumeSerial];
            var driveRoot = $"{letter}:\\";
            foreach (var r in d.Folders)
            {
                if (string.IsNullOrWhiteSpace(r.RootPath)) continue;
                var abs = Path.Combine(driveRoot, r.RootPath.Replace('/', '\\'));
                if (!Directory.Exists(abs)) continue;
                work.Add((d.VolumeSerial, letter, d.Label, driveRoot, abs));
            }
        }
        if (work.Count == 0)
        {
            await ShowInfoDialog("Nothing to refresh",
                "None of the online drives have any folders configured to scan. " +
                "Add a folder on a drive card first.");
            return;
        }

        ScanOverlay.Visibility = Visibility.Visible;
        ScanStatusText.Text = $"Refreshing {work.Count} folder(s)…";
        ScanDetailText.Text = "";
        _scanCts = new CancellationTokenSource();

        int strayCleaned = 0;
        try
        {
            for (int i = 0; i < work.Count; i++)
            {
                if (_scanCts.IsCancellationRequested) break;
                var (serial, _, label, driveRoot, scanFolder) = work[i];

                int prog = i + 1;
                var progress = new Progress<ScanProgress>(p =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        ScanStatusText.Text = $"{prog}/{work.Count} — {label}: {p.Found} checked";
                        ScanDetailText.Text = p.Done ? "" : Path.GetFileName(p.CurrentFolder);
                    });
                });

                await AppState.Instance.Scanner.ScanAsync(
                    serial, driveRoot, progress, _scanCts.Token,
                    scanFolder: scanFolder, incremental: true);
            }

            // Cleanup: remove any movies whose folder_rel_path isn't under
            // a configured drive_root. This silently undoes the v1 bug
            // for users who already ran the broken refresh.
            foreach (var d in drives)
                strayCleaned += AppState.Instance.Db.RemoveMoviesOutsideDriveRoots(d.VolumeSerial);

            ScanStatusText.Text = strayCleaned > 0
                ? $"Refreshed — removed {strayCleaned} stray entries"
                : "Refreshed — sidebar counts updated";
            await Task.Delay(1500);
        }
        catch (OperationCanceledException)
        {
            ScanStatusText.Text = "Refresh cancelled";
            await Task.Delay(1000);
        }
        catch (Exception ex)
        {
            await ShowInfoDialog("Refresh error", ex.Message);
        }
        finally
        {
            ScanOverlay.Visibility = Visibility.Collapsed;
            Refresh();
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    // ── Browse ────────────────────────────────────────────────────────────

    private void OnBrowseDrive(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string serial)
            NavigateToLibrary?.Invoke(this, serial);
    }

    // ── Folder row hover-reveal (delete button) ───────────────────────────

    private static void SetFolderDeleteOpacity(object sender, double opacity)
    {
        if (sender is Grid g)
            foreach (var child in g.Children)
                if (child is Button b) { b.Opacity = opacity; break; }
    }

    private void OnFolderPointerEntered(object sender, PointerRoutedEventArgs e)
        => SetFolderDeleteOpacity(sender, 1);

    private void OnFolderPointerExited(object sender, PointerRoutedEventArgs e)
        => SetFolderDeleteOpacity(sender, 0);

    // ── Drive card hover lift ─────────────────────────────────────────────

    // Themed surface overlays (must match CardBrush / CardHoverBrush in
    // Styles/Colors.xaml). Resolved from the card's ActualTheme because a
    // plain Application.Resources lookup of a ThemeDictionary key returns the
    // wrong theme variant (which painted dark cards white on hover).
    private static Microsoft.UI.Xaml.Media.SolidColorBrush CardRestBrush(FrameworkElement el) =>
        new(el.ActualTheme == ElementTheme.Light
            ? Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)   // #FFFFFF
            : Windows.UI.Color.FromArgb(0xFF, 0x14, 0x14, 0x1F)); // #14141F

    private static Microsoft.UI.Xaml.Media.SolidColorBrush CardHoverBrush(FrameworkElement el) =>
        new(el.ActualTheme == ElementTheme.Light
            ? Windows.UI.Color.FromArgb(0xFF, 0xED, 0xED, 0xFB)   // #EDEDFB
            : Windows.UI.Color.FromArgb(0xFF, 0x1E, 0x1E, 0x2E)); // #1E1E2E

    private void OnCardPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border b) b.Background = CardHoverBrush(b);
    }

    private void OnCardPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border b) b.Background = CardRestBrush(b);
    }

    // ── Clean up missing movies ───────────────────────────────────────────

    private async void OnCleanupMissing(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button btn || btn.Tag is not string serial) return;
            var drive = _drives.FirstOrDefault(d => d.VolumeSerial == serial);
            if (drive == null || drive.MissingCount == 0) return;

            var dialog = new ContentDialog
            {
                Title = $"Remove {drive.MissingCount} missing movies?",
                Content =
                    $"The last scan of '{drive.Label}' didn't find {drive.MissingCount} movie(s) that were previously indexed. " +
                    "Their folders may have been renamed or deleted on the drive.\n\n" +
                    "Removing them will delete their entries (and cached posters/fanart) from the CineLibrary database. " +
                    "The actual files on the drive are not touched.\n\n" +
                    "Tip: if the drive was only partially connected or you scanned the wrong folder, " +
                    "cancel this and rescan first — otherwise you'll re-scrape these next time.",
                PrimaryButtonText = $"Delete {drive.MissingCount} entries",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
                RequestedTheme = CineLibraryCS.MainWindow.CurrentTheme,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            var deleted = AppState.Instance.Db.CleanupMissingMovies(serial);
            Refresh();
            RefreshRequested?.Invoke(this, EventArgs.Empty);

            await ShowInfoDialog("Cleaned up", $"Removed {deleted} missing movie entries from '{drive.Label}'.");
        }
        catch (Exception ex) { await ShowInfoDialog("Error", ex.Message); }
    }

    // ── Rename drive ──────────────────────────────────────────────────────

    private async void OnRenameDrive(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement btn || btn.Tag is not string serial) return;
            var drive = _drives.FirstOrDefault(d => d.VolumeSerial == serial);
            if (drive == null) return;

            var box = new TextBox
            {
                Text = drive.Label,
                SelectionStart = 0,
                SelectionLength = drive.Label.Length,
                PlaceholderText = "e.g. Seagate Red 5TB - Movies",
                MinWidth = 320,
            };
            var muted = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0xFF, 0x90, 0x90, 0xA0));
            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(new TextBlock
            {
                Text = "Give this drive a unique name so you can tell it apart from other drives with the same model.",
                FontSize = 12, Foreground = muted, TextWrapping = TextWrapping.Wrap,
            });
            panel.Children.Add(box);

            var dialog = new ContentDialog
            {
                Title = "Rename Drive",
                Content = panel,
                PrimaryButtonText = "Rename",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
                RequestedTheme = CineLibraryCS.MainWindow.CurrentTheme,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            var newName = box.Text.Trim();
            if (string.IsNullOrEmpty(newName)) return;
            if (newName == drive.Label) return;

            AppState.Instance.Db.RenameDrive(serial, newName);
            Refresh();
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { await ShowInfoDialog("Error", ex.Message); }
    }

    // ── Remove drive ──────────────────────────────────────────────────────

    private async void OnRemoveDrive(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement btn || btn.Tag is not string serial) return;
            var drive = _drives.FirstOrDefault(d => d.VolumeSerial == serial);

            // v2.7 — count how many movies on this drive carry personal state
            // (watched / favorite / watchlist / last_played / notes / list
            // membership). If there's any, offer to mirror it to disk first
            // so the user can re-add the drive later and get everything back.
            var stateful = AppState.Instance.Db.GetMoviesWithPersonalState(serial).Count;
            var canSync = drive?.IsConnected == true && stateful > 0;

            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(new TextBlock
            {
                Text = $"Remove '{drive?.Label}' and all its {drive?.MovieCount} movies from CineLibrary? The actual files on the drive are not deleted.",
                TextWrapping = TextWrapping.Wrap,
            });
            CheckBox? syncBox = null;
            if (stateful > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"{stateful} movie(s) on this drive have watched / favorite / list / notes data.",
                    FontSize = 12,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["MutedBrush"],
                    TextWrapping = TextWrapping.Wrap,
                });
                if (canSync)
                {
                    syncBox = new CheckBox
                    {
                        Content = "Save that state to the drive first (so it comes back if you re-add this drive later)",
                        IsChecked = true,
                    };
                    panel.Children.Add(syncBox);
                }
                else
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = "⚠ Drive is offline — that state will be lost unless you cancel and connect the drive first.",
                        FontSize = 12,
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentRedBrush"],
                        TextWrapping = TextWrapping.Wrap,
                    });
                }
            }

            var dialog = new ContentDialog
            {
                Title = "Remove Drive?",
                Content = panel,
                PrimaryButtonText = "Remove",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
                RequestedTheme = CineLibraryCS.MainWindow.CurrentTheme,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            if (syncBox?.IsChecked == true)
            {
                var (written, skipped) = await Task.Run(() =>
                    AppState.Instance.SweepStateSidecars(serial));
                if (App.MainWindow is CineLibraryCS.MainWindow mw)
                    mw.ShowToast($"Synced state to {written} movie(s){(skipped > 0 ? $" — {skipped} skipped" : "")}");
            }

            AppState.Instance.Db.RemoveDrive(serial);
            Refresh();
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { await ShowInfoDialog("Error", ex.Message); }
    }

    private async void OnSyncStateToDrive(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement btn || btn.Tag is not string serial) return;
            var (written, skipped) = await Task.Run(() =>
                AppState.Instance.SweepStateSidecars(serial));
            if (App.MainWindow is CineLibraryCS.MainWindow mw)
                mw.ShowToast(written == 0
                    ? "Nothing to sync — no movies on this drive carry personal state yet."
                    : $"Synced state to {written} movie(s){(skipped > 0 ? $" — {skipped} skipped" : "")}");
        }
        catch (Exception ex) { await ShowInfoDialog("Error", ex.Message); }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private async Task ShowInfoDialog(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title, Content = message, CloseButtonText = "OK",
            XamlRoot = XamlRoot,
            RequestedTheme = CineLibraryCS.MainWindow.CurrentTheme,
        };
        await dialog.ShowAsync();
    }
}
