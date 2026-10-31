using System.Runtime.InteropServices;

namespace CineLibraryCS.Services.Playback;

/// <summary>
/// v4.10.0: the window VLC draws the video in. A plain Win32 window, not a WinUI one:
/// WinUI draws its content through composition, which paints over a child window,
/// so the video gets a top-level window of its own, owned by the window Play was
/// pressed in (it stays above it and goes with it when minimized). VLC's own
/// Direct3D 11 output then works as in VLC itself, HDR included.
/// VLC's windows inside it are disabled (mouse and keys are off in LibVLC), so the
/// mouse and the keys come here.
/// </summary>
internal sealed class PlayerWindow
{
    private const string ClassName = "CineLibraryPlayerWindow";
    private static readonly WndProc Proc = WindowProc;   // kept alive: Windows calls it
    private static readonly Dictionary<IntPtr, PlayerWindow> Windows = new();
    private static bool _registered;
    private WINDOWPLACEMENT _beforeFullScreen;
    private int _lastX = -1000, _lastY = -1000;

    public IntPtr Hwnd { get; }
    public bool IsFullScreen { get; private set; }

    private bool _cursorHidden;

    /// <summary>A key went down while the player window has the focus (virtual-key code, Ctrl held).</summary>
    public event Action<int, bool>? KeyDown;
    /// <summary>The mouse moved over the video by more than a pixel or two: where to (client pixels).</summary>
    public event Action<int, int>? MouseMoved;
    public event Action? Clicked;
    /// <summary>A right-click over the video: where (client pixels).</summary>
    public event Action<int, int>? RightClicked;
    public event Action? DoubleClicked;
    /// <summary>The wheel turned over the video: +1 up, -1 down.</summary>
    public event Action<int>? Wheel;
    /// <summary>Moved, resized, minimized, restored, full screen or back.</summary>
    public event Action? PlacementChanged;

    /// <summary>The window's close button, Alt+F4 or a close from the taskbar; the window stays until <see cref="Destroy"/>.</summary>
    public event Action? CloseRequested;

    public PlayerWindow(IntPtr owner, int x, int y, int width, int height, string title)
    {
        var instance = GetModuleHandle(null);
        if (!_registered)
        {
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                style = CsDblClks,
                lpfnWndProc = Proc,
                hInstance = instance,
                hIcon = LoadIcon(instance, AppIconId),
                hCursor = LoadCursor(IntPtr.Zero, IdcArrow),
                hbrBackground = GetStockObject(BlackBrush),
                lpszClassName = ClassName,
            };
            if (RegisterClassEx(ref wc) == 0) throw new InvalidOperationException($"RegisterClassEx failed ({Marshal.GetLastWin32Error()})");
            _registered = true;
        }
        Hwnd = CreateWindowEx(0, ClassName, title, WsPopup | FrameStyle | WsClipChildren,
                              x, y, width, height, owner, IntPtr.Zero, instance, IntPtr.Zero);
        if (Hwnd == IntPtr.Zero) throw new InvalidOperationException($"CreateWindowEx failed ({Marshal.GetLastWin32Error()})");
        Windows[Hwnd] = this;
        // A dark title bar over a film, whatever the Windows theme
        var dark = 1;
        DwmSetWindowAttribute(Hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
    }

    /// <summary>For off-screen tests: show without taking the focus from the user's window.</summary>
    internal static bool NoActivate { get; set; }

    /// <summary>Maximized covers a maximized owner exactly.</summary>
    public void Show(bool maximized) => ShowWindow(Hwnd, NoActivate ? SwShowNoActivate : maximized ? SwShowMaximized : SwShow);

    public void SetTitle(string title) => SetWindowText(Hwnd, title);

    public void Hide() => ShowWindow(Hwnd, SwHide);

    public void Destroy()
    {
        Windows.Remove(Hwnd);
        DestroyWindow(Hwnd);
    }

    /// <summary>Keys come here again, after a click on the controls.</summary>
    public void TakeFocus()
    {
        SetForegroundWindow(Hwnd);
        SetFocus(Hwnd);
    }

    /// <summary>Hides the cursor while it is over the video, or shows it again.</summary>
    public void SetCursorHidden(bool hidden)
    {
        if (_cursorHidden == hidden) return;
        _cursorHidden = hidden;
        if (GetCursorPos(out var pt) && GetAncestor(WindowFromPoint(pt), GaRoot) == Hwnd)
            SetCursor(hidden ? IntPtr.Zero : LoadCursor(IntPtr.Zero, IdcArrow));
    }

    public static int DoubleClickTime => (int)GetDoubleClickTime();

    /// <summary>The video area on the screen, in physical pixels.</summary>
    public RECT ClientOnScreen()
    {
        GetClientRect(Hwnd, out var r);
        var topLeft = new POINT { X = 0, Y = 0 };
        ClientToScreen(Hwnd, ref topLeft);
        return new RECT { Left = topLeft.X, Top = topLeft.Y, Right = topLeft.X + r.Right, Bottom = topLeft.Y + r.Bottom };
    }

    public double Scale => GetDpiForWindow(Hwnd) / 96.0;
    public bool IsMinimized => IsIconic(Hwnd);
    public bool IsVisible => IsWindowVisible(Hwnd);

    public int ClientHeight
    {
        get { GetClientRect(Hwnd, out var r); return r.Bottom; }
    }

    /// <summary>
    /// Full screen on the monitor the window is on, the classic way: no frame, the whole
    /// monitor. Back restores the exact place and size it had.
    /// </summary>
    public void ToggleFullScreen()
    {
        var style = (long)GetWindowLongPtr(Hwnd, GwlStyle);
        if (!IsFullScreen)
        {
            _beforeFullScreen.length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>();
            GetWindowPlacement(Hwnd, ref _beforeFullScreen);
            var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(MonitorFromWindow(Hwnd, MonitorDefaultToNearest), ref info);
            IsFullScreen = true;
            SetWindowLongPtr(Hwnd, GwlStyle, (IntPtr)(style & ~(long)FrameStyle));
            var m = info.rcMonitor;
            SetWindowPos(Hwnd, HwndTop, m.Left, m.Top, m.Right - m.Left, m.Bottom - m.Top, SwpNoOwnerZOrder | SwpFrameChanged);
        }
        else
        {
            IsFullScreen = false;
            SetWindowLongPtr(Hwnd, GwlStyle, (IntPtr)(style | FrameStyle));
            SetWindowPlacement(Hwnd, ref _beforeFullScreen);
            SetWindowPos(Hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoOwnerZOrder | SwpFrameChanged);
        }
        PlacementChanged?.Invoke();
    }

    /// <summary>For the player log: the video area's size and VLC's windows in it (size, shown or hidden).</summary>
    public string VlcWindowsState()
    {
        GetClientRect(Hwnd, out var client);
        var parts = new List<string>();
        EnumChildWindows(Hwnd, (child, _) =>
        {
            GetWindowRect(child, out var r);
            parts.Add($"{r.Right - r.Left}x{r.Bottom - r.Top}{(IsWindowVisible(child) ? "" : " hidden")}");
            return true;
        }, IntPtr.Zero);
        return $"area {client.Right}x{client.Bottom}, VLC {(parts.Count == 0 ? "none" : string.Join(", ", parts))}";
    }

    /// <summary>For the player log: which window has the focus, this one, another of CineLibrary's or another app's.</summary>
    public string Foreground()
    {
        var fg = GetForegroundWindow();
        if (fg == Hwnd) return "player";
        GetWindowThreadProcessId(fg, out var pid);
        return pid == (uint)Environment.ProcessId ? "CineLibrary" : "another app";
    }

    /// <summary>VLC's windows inside this one, and whether each is disabled (for tests).</summary>
    public List<(IntPtr Hwnd, bool Disabled)> VlcWindows()
    {
        var list = new List<(IntPtr, bool)>();
        EnumChildWindows(Hwnd, (child, _) =>
        {
            list.Add((child, ((long)GetWindowLongPtr(child, GwlStyle) & WsDisabled) != 0));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    // Windows calls this: an exception must never get through to it, or it ends all of
    // CineLibrary. A failing handler costs that one message, nothing more.
    private static IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try { return HandleMessage(hwnd, msg, wParam, lParam); }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"Player window message 0x{msg:X}: {e}");
            return DefWindowProc(hwnd, msg, wParam, lParam);
        }
    }

    private static IntPtr HandleMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        Windows.TryGetValue(hwnd, out var w);
        switch (msg)
        {
            case WmKeyDown:
                w?.KeyDown?.Invoke((int)wParam, (GetKeyState(VkControl) & 0x8000) != 0);
                return IntPtr.Zero;
            case WmSysKeyDown when (int)wParam == VkReturn:   // Alt+Enter
                w?.KeyDown?.Invoke(VkF11, false);
                return IntPtr.Zero;
            case WmMouseMove:
                var x = (short)((long)lParam & 0xFFFF);
                var y = (short)(((long)lParam >> 16) & 0xFFFF);
                if (w is not null && Math.Abs(x - w._lastX) + Math.Abs(y - w._lastY) > 3)
                {
                    w._lastX = x; w._lastY = y;
                    w.MouseMoved?.Invoke(x, y);
                }
                return IntPtr.Zero;
            case WmLButtonDown:
                w?.Clicked?.Invoke();
                return IntPtr.Zero;
            case WmLButtonDblClk:
                w?.DoubleClicked?.Invoke();
                return IntPtr.Zero;
            case WmRButtonUp:
                w?.RightClicked?.Invoke((short)((long)lParam & 0xFFFF), (short)(((long)lParam >> 16) & 0xFFFF));
                return IntPtr.Zero;
            case WmMouseWheel:
                w?.Wheel?.Invoke((short)(((long)wParam >> 16) & 0xFFFF) > 0 ? 1 : -1);
                return IntPtr.Zero;
            case WmSetCursor when w is { _cursorHidden: true } && ((long)lParam & 0xFFFF) == HtClient:
                SetCursor(IntPtr.Zero);
                return (IntPtr)1;
            case WmWindowPosChanged:
                w?.PlacementChanged?.Invoke();
                break;   // DefWindowProc still sends WM_SIZE and WM_MOVE
            case WmClose:
                w?.CloseRequested?.Invoke();
                return IntPtr.Zero;
            case WmDpiChanged:
                // Windows suggests the size and place for the new monitor's scale
                var r = Marshal.PtrToStructure<RECT>(lParam);
                SetWindowPos(hwnd, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, SwpNoZOrder | SwpNoActivate);
                return IntPtr.Zero;
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    // ---- Win32 ----

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    private delegate bool EnumProc(IntPtr hwnd, IntPtr param);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        [MarshalAs(UnmanagedType.FunctionPtr)] public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }

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

    private const uint CsDblClks = 0x0008;
    // A popup with a frame, like a dialog: Windows hides it with the library window when
    // that one is minimized. No minimize button of its own (it has no taskbar button).
    private const uint WsPopup = 0x80000000;
    private const uint FrameStyle = 0x00CD0000;   // caption, system menu, sizing border, maximize button
    private const uint WsClipChildren = 0x02000000;
    private const long WsDisabled = 0x08000000;
    private const int GwlStyle = -16;
    private const int SwHide = 0;
    private const int SwShowMaximized = 3;
    private const int SwShow = 5;
    private const int SwShowNoActivate = 4;
    private const int BlackBrush = 4;
    private const int IdcArrow = 32512;
    private const int AppIconId = 32512;   // the .exe's own icon
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int HtClient = 1;
    private const uint GaRoot = 2;
    private const int VkReturn = 0x0D;
    private const int VkControl = 0x11;
    internal const int VkF11 = 0x7A;
    private const uint WmClose = 0x0010;
    private const uint WmSetCursor = 0x0020;
    private const uint WmWindowPosChanged = 0x0047;
    private const uint WmKeyDown = 0x0100;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmMouseMove = 0x0200;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmMouseWheel = 0x020A;
    private const uint WmDpiChanged = 0x02E0;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint MonitorDefaultToNearest = 2;
    private static readonly IntPtr HwndTop = IntPtr.Zero;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int cmd);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowText(IntPtr hwnd, string text);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr param);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int key);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr cursor);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr instance, int name);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr instance, int name);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int obj);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}
