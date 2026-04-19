using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    }

    // ── Add drive ─────────────────────────────────────────────────────────

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

        // Build a list of drives that aren't already added
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

        // Pick drive — use SelectedIndex to look up from newDrives (no Tag marshaling)
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

        // Prompt to scan
        var scanDialog = new ContentDialog
        {
            Title = "Scan now?",
            Content = $"Drive '{finalLabel}' added. Would you like to scan it now?",
            PrimaryButtonText = "Scan",
            CloseButtonText = "Later",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = CineLibraryCS.MainWindow.CurrentTheme,
        };
        if (await scanDialog.ShowAsync() == ContentDialogResult.Primary)
            await ScanDriveAsync(ser, let, finalLabel, null);
    }

    // ── Scan ─────────────────────────────────────────────────────────────

    private async void OnScanDrive(object sender, RoutedEventArgs e)
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
            var driveRoot = $"{letter}:\\";

            // Ask: full drive or specific folder?
            var scopePanel = new StackPanel { Spacing = 12 };
            var rbFull   = new RadioButton { Content = "Scan full drive",       IsChecked = true, GroupName = "ScanScope" };
            var rbFolder = new RadioButton { Content = "Scan specific folder…", IsChecked = false, GroupName = "ScanScope" };
            scopePanel.Children.Add(rbFull);
            scopePanel.Children.Add(rbFolder);

            var scopeDialog = new ContentDialog
            {
                Title = $"Scan {drive?.Label ?? serial}",
                Content = scopePanel,
                PrimaryButtonText = "Next",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
                RequestedTheme = CineLibraryCS.MainWindow.CurrentTheme,
            };

            if (await scopeDialog.ShowAsync() != ContentDialogResult.Primary) return;

            string? scanFolder = null;

            if (rbFolder.IsChecked == true)
            {
                // Open a folder picker restricted to the drive
                var picker = new Windows.Storage.Pickers.FolderPicker();
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
                picker.FileTypeFilter.Add("*");

                // Associate picker with the app window (required for unpackaged WinUI 3)
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var folder = await picker.PickSingleFolderAsync();
                if (folder == null) return;   // user cancelled

                // Validate the chosen folder is actually under this drive
                if (!folder.Path.StartsWith(driveRoot, StringComparison.OrdinalIgnoreCase))
                {
                    await ShowInfoDialog("Wrong drive",
                        $"Please choose a folder on drive {letter}: ({driveRoot}).");
                    return;
                }

                scanFolder = folder.Path;
            }

            await ScanDriveAsync(serial, letter, drive?.Label ?? serial, scanFolder);
        }
        catch (Exception ex) { await ShowInfoDialog("Error", ex.Message); }
    }

    private async Task ScanDriveAsync(string serial, string letter, string label, string? scanFolder = null)
    {
        var driveRoot = $"{letter}:\\";

        var scopeLabel = scanFolder != null
            ? $"…\\{Path.GetFileName(scanFolder)}"
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
            await Task.Delay(1500);
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

    // ── Browse ────────────────────────────────────────────────────────────

    private void OnBrowseDrive(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string serial)
            NavigateToLibrary?.Invoke(this, serial);
    }

    // ── Rename ────────────────────────────────────────────────────────────

    private async void OnRenameDrive(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button btn || btn.Tag is not string serial) return;
            var drive = _drives.FirstOrDefault(d => d.VolumeSerial == serial);
            if (drive == null) return;

            var box = new TextBox { Text = drive.Label, SelectionStart = 0, SelectionLength = drive.Label.Length };
            var dialog = new ContentDialog
            {
                Title = "Rename Drive",
                Content = box,
                PrimaryButtonText = "Rename",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
                RequestedTheme = CineLibraryCS.MainWindow.CurrentTheme,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            var newName = box.Text.Trim();
            if (string.IsNullOrEmpty(newName)) return;
            AppState.Instance.Db.RenameDrive(serial, newName);
            Refresh();
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { await ShowInfoDialog("Error", ex.Message); }
    }

    // ── Remove ────────────────────────────────────────────────────────────

    private async void OnRemoveDrive(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button btn || btn.Tag is not string serial) return;
            var drive = _drives.FirstOrDefault(d => d.VolumeSerial == serial);

            var dialog = new ContentDialog
            {
                Title = "Remove Drive?",
                Content = $"Remove '{drive?.Label}' and all its {drive?.MovieCount} movies from CineLibrary? The actual files are not deleted.",
                PrimaryButtonText = "Remove",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
                RequestedTheme = CineLibraryCS.MainWindow.CurrentTheme,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            AppState.Instance.Db.RemoveDrive(serial);
            Refresh();
            RefreshRequested?.Invoke(this, EventArgs.Empty);
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
