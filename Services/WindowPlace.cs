using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace CineLibraryCS.Services;

/// <summary>
/// 5.4.4: where the main window was left, to open there next time: its size and place when it isn't
/// maximized (screen pixels), and whether it was maximized, on which screen. A place no screen shows
/// any more (a monitor unplugged) is left out, and it opens maximized as before.
/// </summary>
internal static class WindowPlace
{
    private const string Key = "ui_windowPlace";

    public static void Save(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd)) return;   // hidden while a movie's window has its place
        var p = new WINDOWPLACEMENT { length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(hwnd, ref p)) return;
        var maximized = p.showCmd == SwShowMaximized || p.showCmd == SwShowMinimized && (p.flags & WpfRestoreToMaximized) != 0;
        RECT r;
        if (p.showCmd == SwShowNormal && GetWindowRect(hwnd, out var now)) r = now;   // on screen, exactly
        else
        {
            // Maximized (or minimized): the size it goes back to, in the middle of the screen it's on
            int w = p.rcNormalPosition.Right - p.rcNormalPosition.Left, h = p.rcNormalPosition.Bottom - p.rcNormalPosition.Top;
            var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(MonitorFromWindow(hwnd, MonitorDefaultToNearest), ref info)) return;
            var work = info.rcWork;
            w = Math.Min(w, work.Right - work.Left);
            h = Math.Min(h, work.Bottom - work.Top);
            var left = work.Left + (work.Right - work.Left - w) / 2;
            var top = work.Top + (work.Bottom - work.Top - h) / 2;
            r = new RECT { Left = left, Top = top, Right = left + w, Bottom = top + h };
        }
        AppState.Instance.SetPref(Key, string.Join(",", r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, maximized ? 1 : 0));
    }

    /// <summary>Moves the window where it was left. True when it was maximized; null when nothing is kept, or
    /// no screen shows that place now.</summary>
    public static bool? Restore(AppWindow window)
    {
        var parts = AppState.Instance.GetPref(Key, "").Split(',');
        var v = new int[5];
        if (parts.Length != 5 || parts.Where((x, i) => !int.TryParse(x, out v[i])).Any()) return null;
        if (v[2] < 400 || v[3] < 300) return null;
        var rect = new RECT { Left = v[0], Top = v[1], Right = v[0] + v[2], Bottom = v[1] + v[3] };
        if (MonitorFromRect(ref rect, MonitorDefaultToNull) == IntPtr.Zero) return null;
        window.MoveAndResize(new RectInt32(v[0], v[1], v[2], v[3]));
        return v[4] == 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public uint length, flags, showCmd;
        public POINT ptMinPosition, ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
    }

    private const uint SwShowNormal = 1, SwShowMinimized = 2, SwShowMaximized = 3, WpfRestoreToMaximized = 2;
    private const uint MonitorDefaultToNull = 0, MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
}
