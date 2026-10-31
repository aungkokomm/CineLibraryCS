using System.Runtime.CompilerServices;
using LibVLCSharp.Shared;

namespace CineLibraryCS.Services.Playback;

/// <summary>
/// v4.10.0: VLC's engine for the built-in player, started once, on a background
/// thread, at the first Play. It always loads the copy in libvlc\win-x64 next to
/// CineLibrary, never a VLC installed on the computer.
/// </summary>
public static class VideoEngine
{
    private static Task<(LibVLC? Vlc, string? Error)>? _start;

    public static string LibVlcDir => Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64");
    private static string PluginsCache => Path.Combine(LibVlcDir, "plugins", "plugins.dat");

    /// <summary>The engine, or null with the reason it didn't start.</summary>
    public static Task<(LibVLC? Vlc, string? Error)> GetAsync() => _start ??= Task.Run(Start);

    private static (LibVLC?, string?) Start()
    {
        try
        {
            // A VLC_PLUGIN_PATH set for another VLC would load its modules into ours
            Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", null);
            Core.Initialize(LibVlcDir);
            // Sound goes through libsamplerate, VLC's first choice, added beside the package.
            // Never ask for Speex: VLC's build rebuilds a full table for every block while it
            // nudges the sound back in step after a seek, 120 ms for 43 ms of sound, so it
            // never catches up and the sound stays chopped.
            // Two decoder threads: with hardware decoding VLC keeps a picture per
            // thread, so 4K needs 32 instead of 46 (about 350 MB less) and starts and
            // seeks faster; software decoding of 1080p still has room to spare.
            string[] options = ["--no-video-title-show", "--no-osd", "--no-snapshot-preview", "--avcodec-threads=2"];
            // Without its module cache (made by the installer build) VLC opens every module at each start
            if (!File.Exists(PluginsCache))
                options = [.. options, "--reset-plugins-cache"];
            var vlc = new LibVLC(options);
            vlc.Log += PlayerLog.OnVlcLog;
            return (vlc, null);
        }
        catch (Exception e) when (e is VLCException or DllNotFoundException or TypeInitializationException or BadImageFormatException or InvalidOperationException)
        {
            return (null, e.Message);
        }
    }

    /// <summary>
    /// Writes VLC's module cache next to the modules, for the installer build
    /// (CineLibrary.exe --vlc-plugins-cache). True when the cache exists afterwards.
    /// </summary>
    public static bool WritePluginsCache()
    {
        try
        {
            Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", null);
            Core.Initialize(LibVlcDir);
            using (new LibVLC("--reset-plugins-cache")) { }
            return File.Exists(PluginsCache);
        }
        catch (Exception e) when (e is VLCException or DllNotFoundException or TypeInitializationException or IOException)
        {
            return false;
        }
    }
}

/// <summary>Runs before anything else, so the installer build's switch never opens a window.</summary>
internal static class VideoEngineCommandLine
{
    [ModuleInitializer]
    internal static void Init()
    {
        if (Environment.GetCommandLineArgs().Skip(1).FirstOrDefault() != "--vlc-plugins-cache") return;
        var ok = false;
        try { ok = VideoEngine.WritePluginsCache(); }
        finally { Environment.Exit(ok ? 0 : 1); }
    }
}
