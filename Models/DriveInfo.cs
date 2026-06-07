namespace CineLibraryCS.Models;

public class DriveInfo
{
    public string VolumeSerial { get; set; } = "";
    public string Label { get; set; } = "";
    public string? LastSeenLetter { get; set; }
    public bool IsConnected { get; set; }
    public string? CurrentLetter { get; set; }
    public int MovieCount { get; set; }
    public int TvShowCount { get; set; }
    public int MissingCount { get; set; }
    public string MovieRootRelative { get; set; } = "";
    public List<DriveRoot> Folders { get; set; } = new();

    // v2.8 — TV-show pill on the drive card (only when the drive has shows).
    public Microsoft.UI.Xaml.Visibility TvShowVisibility =>
        TvShowCount > 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public string StatusText => IsConnected ? $"Connected ({CurrentLetter}:)" : "Offline";
    public string NotConnectedText => IsConnected ? $"Connected as {CurrentLetter}:" : "Not connected";
    public string LetterDisplay => CurrentLetter != null ? $"({CurrentLetter}:)" : "";
    public bool HasFolders => Folders.Count > 0;
    public bool HasMissing => MissingCount > 0;
    public string MissingButtonText => MissingCount > 0 ? $"Clean up {MissingCount}" : "Clean up";
    public Microsoft.UI.Xaml.Visibility MissingVisibility =>
        MissingCount > 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    // v3.2 — compact, sleek Drives page helpers.
    public string MovieCountText => $"{MovieCount} {(MovieCount == 1 ? "movie" : "movies")}";
    public string TvShowCountText => $"{TvShowCount} {(TvShowCount == 1 ? "show" : "shows")}";
    public string FolderCountText => $"{Folders.Count} {(Folders.Count == 1 ? "folder" : "folders")}";
    public Microsoft.UI.Xaml.Visibility FoldersVisibility =>
        Folders.Count > 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public string MissingInfoText =>
        $"{MissingCount} indexed movie(s) weren't found in the last scan — they may have been moved or deleted on the drive.";
}

public class DriveRoot
{
    public int Id { get; set; }
    public string VolumeSerial { get; set; } = "";
    public string RootPath { get; set; } = ""; // relative to drive root, forward-slash
    public string DisplayName => string.IsNullOrEmpty(RootPath) ? "(drive root)" : RootPath;
}
