using System.Runtime.InteropServices;

namespace CineLibraryCS.Services;

/// <summary>
/// Zero-poll drive-connect/disconnect notifications via WM_DEVICECHANGE.
///
/// Windows already raises WM_DEVICECHANGE on the main window every time a
/// volume is added or removed — we just need to listen. This replaces the
/// old 10-second System.Threading.Timer in MainViewModel which:
///   • kept waking the disk even when idle
///   • hit the SQLite connection from a ThreadPool thread, which gave us
///     the v1.4.0 STATUS_STACK_BUFFER_OVERRUN on close
///   • had noticeable latency (up to 10s) when plugging in a USB drive
///
/// We subclass the HWND via SetWindowSubclass (comctl32) so we don't have
/// to replace the existing WndProc. Callback runs on the UI thread.
/// </summary>
public sealed class DeviceChangeWatcher : IDisposable
{
    // WM_DEVICECHANGE broadcasts any device event. We only care about
    // volume arrival/removal.
    private const int WM_DEVICECHANGE       = 0x0219;
    private const int DBT_DEVICEARRIVAL     = 0x8000;
    private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    private const int DBT_DEVTYP_VOLUME     = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct DEV_BROADCAST_HDR
    {
        public int dbch_size;
        public int dbch_devicetype;
        public int dbch_reserved;
    }

    private delegate IntPtr SUBCLASSPROC(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
        UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(
        IntPtr hWnd, SUBCLASSPROC pfnSubclass, UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(
        IntPtr hWnd, SUBCLASSPROC pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    // Keep the delegate alive — if it's GC'd while Windows still holds the
    // subclass, the next WM_DEVICECHANGE pops the app.
    private readonly SUBCLASSPROC _proc;
    private readonly IntPtr _hwnd;
    private readonly UIntPtr _id = (UIntPtr)0xC1E1C1E1; // arbitrary subclass id
    private readonly Action _onChange;
    private bool _disposed;

    public DeviceChangeWatcher(IntPtr hwnd, Action onDeviceChange)
    {
        _hwnd = hwnd;
        _onChange = onDeviceChange;
        _proc = SubclassProc;
        SetWindowSubclass(hwnd, _proc, _id, UIntPtr.Zero);
    }

    private IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
                                UIntPtr uIdSubclass, UIntPtr dwRefData)
    {
        if (uMsg == WM_DEVICECHANGE)
        {
            int evt = wParam.ToInt32();
            if ((evt == DBT_DEVICEARRIVAL || evt == DBT_DEVICEREMOVECOMPLETE)
                && lParam != IntPtr.Zero)
            {
                var hdr = Marshal.PtrToStructure<DEV_BROADCAST_HDR>(lParam);
                if (hdr.dbch_devicetype == DBT_DEVTYP_VOLUME)
                {
                    try { _onChange(); } catch { /* never let a notification crash WndProc */ }
                }
            }
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { RemoveWindowSubclass(_hwnd, _proc, _id); } catch { }
    }
}
