namespace CineLibraryCS.Models;

public class DriveInfo
{
    public string VolumeSerial { get; set; } = "";
    public string Label { get; set; } = "";
    public string? LastSeenLetter { get; set; }
    public bool IsConnected { get; set; }
    public string? CurrentLetter { get; set; }
    public int MovieCount { get; set; }
    public int MissingCount { get; set; }
    public string MovieRootRelative { get; set; } = "";
    public List<DriveRoot> Folders { get; set; } = new();

    public string StatusText => IsConnected ? $"Connected ({CurrentLetter}:)" : "Offline";
    public string NotConnectedText => IsConnected ? $"Connected as {CurrentLetter}:" : "Not connected";
    public string LetterDisplay => CurrentLetter != null ? $"({CurrentLetter}:)" : "";
    public bool HasFolders => Folders.Count > 0;
    public bool HasMissing => MissingCount > 0;
    public string MissingButtonText => MissingCount > 0 ? $"🧹 Clean up {MissingCount} missing" : "🧹 Clean up missing";
    public Microsoft.UI.Xaml.Visibility MissingVisibility =>
        MissingCount > 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
}

public class DriveRoot
{
    public int Id { get; set; }
    public string VolumeSerial { get; set; } = "";
    public string RootPath { get; set; } = ""; // relative to drive root, forward-slash
    public string DisplayName => string.IsNullOrEmpty(RootPath) ? "(drive root)" : RootPath;
}
