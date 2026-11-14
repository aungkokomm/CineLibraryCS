using System.Runtime.InteropServices;
using Windows.Media;
using Windows.Storage;
using Windows.Storage.Streams;

namespace CineLibraryCS.Services.Playback;

/// <summary>
/// 5.4.3: Windows' media controls for the player window. The keyboard's media keys, a remote's
/// buttons and those of Bluetooth headphones reach the player even when another window is in
/// front or CineLibrary is minimized, and Windows' media panel (by the volume) shows what's
/// playing, with its poster. Windows sends the keys to whatever played last.
/// </summary>
internal sealed class MediaKeys
{
    private readonly SystemMediaTransportControls _controls;

    /// <summary>A key or a button, on one of Windows' threads.</summary>
    public event Action<SystemMediaTransportControlsButton>? Pressed;

    private MediaKeys(SystemMediaTransportControls controls)
    {
        _controls = controls;
        controls.IsEnabled = true;
        controls.IsPlayEnabled = controls.IsPauseEnabled = controls.IsStopEnabled = true;
        controls.IsFastForwardEnabled = controls.IsRewindEnabled = true;
        controls.IsNextEnabled = controls.IsPreviousEnabled = false;
        controls.ButtonPressed += OnButtonPressed;
    }

    /// <summary>The media controls of a window, or null when Windows doesn't give them (the player keeps its own keys).</summary>
    public static MediaKeys? TryCreate(IntPtr hwnd)
    {
        try { return new MediaKeys(SystemMediaTransportControlsInterop.GetForWindow(hwnd)); }
        catch (Exception e) when (e is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            PlayerLog.Write("no media keys: " + e.Message);
            return null;
        }
    }

    private void OnButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args) =>
        Pressed?.Invoke(args.Button);

    /// <summary>Windows picks Play or Pause for the play/pause key from this.</summary>
    public void SetPlaying(bool playing)
    {
        try { _controls.PlaybackStatus = playing ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused; }
        catch (COMException) { }
    }

    /// <summary>Next and previous only where there's an episode or a chapter to go to.</summary>
    public void SetNextPrevious(bool next, bool previous)
    {
        try
        {
            _controls.IsNextEnabled = next;
            _controls.IsPreviousEnabled = previous;
        }
        catch (COMException) { }
    }

    /// <summary>What Windows' media panel shows: the title, a line under it and the poster.</summary>
    public async void SetInfo(string title, string? line, string? poster)
    {
        try
        {
            var display = _controls.DisplayUpdater;
            display.Type = MediaPlaybackType.Video;
            display.VideoProperties.Title = title;
            display.VideoProperties.Subtitle = line ?? "";
            display.Thumbnail = poster is not null && File.Exists(poster)
                ? RandomAccessStreamReference.CreateFromFile(await StorageFile.GetFileFromPathAsync(poster))
                : null;
            display.Update();
        }
        catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            PlayerLog.Write("media panel: " + e.Message);   // the keys still work
        }
    }

    /// <summary>Gone from Windows' media panel, and the keys go back to whatever had them.</summary>
    public void Close()
    {
        _controls.ButtonPressed -= OnButtonPressed;
        try
        {
            _controls.PlaybackStatus = MediaPlaybackStatus.Closed;
            _controls.DisplayUpdater.ClearAll();
            _controls.DisplayUpdater.Update();
            _controls.IsEnabled = false;
        }
        catch (COMException) { }
    }
}
