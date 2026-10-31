using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace CineLibraryCS.Services.Playback;

/// <summary>
/// v4.10.0: nothing behind a window's content, so what is under the window shows through
/// wherever the content is transparent. WinUI has no such backdrop of its own.
/// </summary>
internal sealed class SeeThroughBackdrop : SystemBackdrop
{
    private static Windows.UI.Composition.Compositor? _compositor;   // kept alive: the brush lives on it

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        base.OnTargetConnected(target, root);
        // The system compositor, the one this brush has to come from, needs the system's dispatcher queue
        if (Windows.System.DispatcherQueue.GetForCurrentThread() is null)
            CreateDispatcherQueueController(new DispatcherQueueOptions { Size = 12, ThreadType = 2, ApartmentType = 2 }, out _);
        _compositor ??= new Windows.UI.Composition.Compositor();
        target.SystemBackdrop = _compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        target.SystemBackdrop = null;
        base.OnTargetDisconnected(target);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions { public int Size, ThreadType, ApartmentType; }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr controller);
}
