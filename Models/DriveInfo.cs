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

    public string StatusText => IsConnected ? $"Connected ({CurrentLetter}:)" : "Offline";
    public string LetterDisplay => CurrentLetter != null ? $"({CurrentLetter}:)" : "";
}

public class DriveRoot
{
    public int Id { get; set; }
    public string RootPath { get; set; } = "";
}
