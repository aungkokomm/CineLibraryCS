using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;

namespace CineLibraryCS.Services.Playback;

/// <summary>
/// v4.10.0: the player's controls, a see-through bar in a borderless window of its own, kept
/// over the bottom of the video by the session. It has to be a window: WinUI can't draw over
/// the video window. Owned by the player window, so it stays above it; it never takes
/// the focus from it (clicks don't activate it, and it isn't in Alt+Tab).
/// A row above the bar opens for the audio and subtitle choices and for short
/// messages (where playback resumed, the next episode).
/// </summary>
internal sealed class PlayerControls : Window
{
    private const double BarHeight = 56, RowHeight = 48, ThumbWidth = 18;   // DIPs
    private const string PlayGlyph = "\uE768", PauseGlyph = "\uE769", VolumeGlyph = "\uE767", MuteGlyph = "\uE74F",
                         FullScreenGlyph = "\uE740", BackToWindowGlyph = "\uE73F";

    private readonly FontIcon _play = Icon(PlayGlyph), _mute = Icon(VolumeGlyph), _fullScreen = Icon(FullScreenGlyph);
    private readonly TextBlock _time = Label(), _length = Label(), _rowLabel = Label();
    private readonly Slider _seek = new() { Minimum = 0, Maximum = 1, IsThumbToolTipEnabled = false };
    private readonly Slider _volume = new() { Minimum = 0, Maximum = 100, Width = 96, IsThumbToolTipEnabled = false };
    private readonly Grid _row = new() { Height = RowHeight, Padding = new Thickness(16, 0, 16, 0), Visibility = Visibility.Collapsed };
    private readonly StackPanel _rowItems = new() { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
    private readonly Canvas _ticks = new() { IsHitTestVisible = false };
    private readonly Button _previous, _next;
    private bool _updating, _dragging;
    private int _wheel;
    private int _timeMode = UiSettings.PlayerTimeMode;
    private IReadOnlyList<double> _chapters = [];

    public IntPtr Hwnd { get; }
    /// <summary>
    /// The mouse is on the controls. Windows is asked each time: the enter and leave events of
    /// WinUI also come for each part of the bar, and don't come when the bar moves away from
    /// under the mouse (into full screen), which left them on for good or off too early.
    /// </summary>
    public bool PointerOver => AppWindow.IsVisible && GetCursorPos(out var mouse) && Covers(mouse.X, mouse.Y);

    /// <summary>The window covers this point of the screen (physical pixels).</summary>
    internal bool Covers(int x, int y)
    {
        var at = AppWindow.Position;
        var size = AppWindow.Size;
        return x >= at.X && x < at.X + size.Width && y >= at.Y && y < at.Y + size.Height;
    }

    public bool Shown => AppWindow.IsVisible;
    /// <summary>The controls' top on the screen (physical pixels).</summary>
    public int Top => AppWindow.Position.Y;

    /// <summary>What the row above the bar shows ("message"); empty when closed.</summary>
    public string Row { get; private set; } = "";

    public event Action? PlayPause, Back, Forward, Mute, FullScreen, CloseClicked, Audio, Subtitles, Previous, Next;
    /// <summary>The seek bar was moved: the new position in seconds.</summary>
    public event Action<double>? Seek;
    public event Action<int>? Volume;
    /// <summary>The wheel turned over the seek bar or the volume: notches, up is positive.</summary>
    public event Action<int>? SeekWheel, VolumeWheel;
    /// <summary>The mouse over the seek bar: the time under it (seconds) and where it is on
    /// the screen (physical pixels); null when it left.</summary>
    public event Action<double?, int>? SeekHover;
    /// <summary>A click on the controls ended (the keys should go back to the player).</summary>
    public event Action? Released;
    /// <summary>Windows moved or resized the controls (for example for a new monitor's
    /// scale), or the row above the bar opened or closed.</summary>
    public event Action? Moved;

    public PlayerControls(IntPtr owner)
    {
        var root = new Grid
        {
            // Dark enough to keep the white controls readable over a white picture
            Background = new SolidColorBrush(ColorHelper.FromArgb(150, 0, 0, 0)),
            RequestedTheme = ElementTheme.Dark,
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(BarHeight) });

        _row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _rowLabel.Margin = new Thickness(0, 0, 12, 0);
        Add(_row, 0, _rowLabel);
        Add(_row, 1, new ScrollViewer
        {
            Content = _rowItems,
            HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalAlignment = VerticalAlignment.Center,
        });
        root.Children.Add(_row);

        var bar = new Grid { Padding = new Thickness(8, 0, 8, 0) };
        Grid.SetRow(bar, 1);
        root.Children.Add(bar);
        foreach (var width in new[] { 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0 })
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = width == 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

        // Previous and next (episode, or chapter): hidden when there's none
        _previous = IconButton(Icon("\uE892"), "Previous (P)", () => Previous?.Invoke());
        _next = IconButton(Icon("\uE893"), "Next (N)", () => Next?.Invoke());
        _previous.Visibility = _next.Visibility = Visibility.Collapsed;
        Add(bar, 0, _previous);
        Add(bar, 1, IconButton(_play, "Play or pause (Space)", () => PlayPause?.Invoke()));
        Add(bar, 2, _next);
        Add(bar, 3, IconButton(Icon("\uEB9E"), "Back 10 seconds (Left)", () => Back?.Invoke()));
        Add(bar, 4, IconButton(Icon("\uEB9D"), "Forward 10 seconds (Right)", () => Forward?.Invoke()));
        Add(bar, 5, _time);
        _seek.Margin = new Thickness(12, 0, 12, 0);
        var seekArea = WheelArea(_seek, notches => SeekWheel?.Invoke(notches));
        _ticks.Margin = _seek.Margin;
        seekArea.Children.Insert(0, _ticks);
        seekArea.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnSeekPointer), true);
        seekArea.PointerExited += (_, _) => SeekHover?.Invoke(null, 0);
        _seek.SizeChanged += (_, _) => DrawTicks();
        _ticks.SizeChanged += (_, _) => DrawTicks();
        Add(bar, 6, seekArea);
        // The length, the time left or when it ends: a click goes to the next one
        var lengthButton = new Button
        {
            Content = _length,
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 2, 0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            AllowFocusOnInteraction = false,
            IsTabStop = false,
        };
        ToolTipService.SetToolTip(lengthButton, "Length, time left or end time");
        lengthButton.Click += (_, _) =>
        {
            _timeMode = (_timeMode + 1) % 3;
            UiSettings.SetPlayerTimeMode(_timeMode);
        };
        Add(bar, 7, lengthButton);
        Add(bar, 8, IconButton(Icon("\uE8D6"), "Audio", () => Audio?.Invoke()));
        Add(bar, 9, IconButton(Icon("\uED1E"), "Subtitles", () => Subtitles?.Invoke()));
        Add(bar, 10, IconButton(_mute, "Mute (M)", () => Mute?.Invoke()));
        _volume.Margin = new Thickness(4, 0, 12, 0);
        Add(bar, 11, WheelArea(_volume, notches => VolumeWheel?.Invoke(notches)));
        Add(bar, 12, IconButton(_fullScreen, "Full screen (F, double-click)", () => FullScreen?.Invoke()));
        Add(bar, 13, IconButton(Icon("\uE711"), "Close (Esc)", () => CloseClicked?.Invoke()));

        foreach (var slider in new[] { _seek, _volume })
        {
            slider.VerticalAlignment = VerticalAlignment.Center;
            slider.AllowFocusOnInteraction = false;
            slider.IsTabStop = false;
        }
        _seek.ValueChanged += (_, e) => { if (!_updating) Seek?.Invoke(e.NewValue); };
        _volume.ValueChanged += (_, e) => { if (!_updating) Volume?.Invoke((int)e.NewValue); };
        // While the seek bar is held, the playing position doesn't move it
        _seek.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => _dragging = true), true);
        _seek.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => _dragging = false), true);
        _seek.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((_, _) => _dragging = false), true);

        root.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => Released?.Invoke()), true);
        Content = root;
        Title = "CineLibrary player controls";
        SystemBackdrop = new SeeThroughBackdrop();

        Hwnd = MakeOverlay(this, owner, menu: false);
        // Posted: an exception escaping AppWindow.Changed ends the process, with "Unknown Hard Error"
        AppWindow.Changed += (_, e) => { if (e.DidPositionChange || e.DidSizeChange) DispatcherQueue.Post(() => Moved?.Invoke()); };
    }

    /// <summary>
    /// A WinUI window over the video: borderless, owned by the player window (so it stays
    /// above it), never activated by a click and not in Alt+Tab. A menu keeps Windows 11's
    /// rounded corners and border; the bar has neither.
    /// </summary>
    internal static IntPtr MakeOverlay(Window window, IntPtr owner, bool menu)
    {
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        window.AppWindow.SetPresenter(presenter);
        window.AppWindow.IsShownInSwitchers = false;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        SetWindowLongPtr(hwnd, GwlExStyle, GetWindowLongPtr(hwnd, GwlExStyle) | WsExNoActivate | WsExToolWindow);
        // The presenter still gives a window without border and title bar a dialog frame (a 3 px
        // edge), and puts it back when the window is shown: the whole window is made client area
        SetWindowSubclass(hwnd, NoFrameProc, 1, 0);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        SetWindowLongPtr(hwnd, GwlpHwndParent, owner);
        var corners = menu ? DwmWcpRound : DwmWcpDoNotRound;
        DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corners, sizeof(int));
        if (!menu)
        {
            var none = DwmwaColorNone;
            DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref none, sizeof(int));
            // Windows then uses the window's transparency: asked for as a blur of a region that is nowhere
            var nowhere = CreateRectRgn(-2, -2, -1, -1);
            var behind = new DwmBlurBehind { Flags = DwmBbEnable | DwmBbBlurRegion, Enable = true, Region = nowhere };
            DwmEnableBlurBehindWindow(hwnd, ref behind);
            DeleteObject(nowhere);
        }
        return hwnd;
    }

    /// <summary>Puts the controls along the bottom of the given screen rectangle (physical pixels).</summary>
    public void Place(int left, int bottom, int width, double scale)
    {
        var height = (int)Math.Round((BarHeight + (Row.Length > 0 ? RowHeight : 0)) * scale);
        var position = AppWindow.Position;
        var size = AppWindow.Size;
        if (position.X == left && position.Y == bottom - height && size.Width == width && size.Height == height) return;
        AppWindow.MoveAndResize(new RectInt32(left, bottom - height, width, height));
    }

    public void SetShown(bool shown)
    {
        if (shown == AppWindow.IsVisible) return;
        if (shown) AppWindow.Show(false);
        else AppWindow.Hide();
    }

    /// <summary>Opens the row above the bar: a label and buttons (an accent one is the chosen
    /// track or the main action).</summary>
    public void ShowRow(string row, string label, IEnumerable<(string Text, bool Accent, Action Click)> buttons)
    {
        Row = row;
        _rowLabel.Text = label;
        _rowItems.Children.Clear();
        foreach (var (text, accent, click) in buttons)
        {
            var b = new Button
            {
                Content = text,
                Padding = new Thickness(12, 4, 12, 4),
                VerticalAlignment = VerticalAlignment.Center,
                AllowFocusOnInteraction = false,
                IsTabStop = false,
            };
            if (accent && Application.Current.Resources.TryGetValue("AccentButtonStyle", out var style)) b.Style = (Style)style;
            b.Click += (_, _) => click();
            _rowItems.Children.Add(b);
        }
        _row.Visibility = Visibility.Visible;
        Moved?.Invoke();
    }

    public void SetRowLabel(string label) => _rowLabel.Text = label;

    /// <summary>Shows Previous and Next with what they go to (null hides one).</summary>
    public void SetSkip(string? previous, string? next)
    {
        _previous.Visibility = previous is null ? Visibility.Collapsed : Visibility.Visible;
        _next.Visibility = next is null ? Visibility.Collapsed : Visibility.Visible;
        ToolTipService.SetToolTip(_previous, previous);
        ToolTipService.SetToolTip(_next, next);
    }

    /// <summary>The Subtitles button's right edge sits this far (DIPs) from the window's right:
    /// padding, Close, Full screen, volume, Mute. The menus open with their right edge there.</summary>
    public const double MenuAnchorFromRight = 8 + 40 + 40 + 112 + 40;

    public void HideRow()
    {
        if (Row.Length == 0) return;
        Row = "";
        _row.Visibility = Visibility.Collapsed;
        _rowItems.Children.Clear();
        Moved?.Invoke();
    }

    public void Update(long timeMs, long lengthMs, bool playing, bool muted, int volume, bool fullScreen)
    {
        _updating = true;
        _seek.Maximum = Math.Max(1, lengthMs / 1000.0);
        if (!_dragging) _seek.Value = Math.Clamp(timeMs / 1000.0, 0, _seek.Maximum);
        _volume.Value = Math.Clamp(volume, 0, 100);
        _updating = false;
        _time.Text = Format(timeMs);
        var left = Math.Max(0, lengthMs - timeMs);
        _length.Text = _timeMode switch
        {
            1 => "-" + Format(left),
            2 => "Ends " + DateTime.Now.AddMilliseconds(left).ToString("t"),
            _ => Format(lengthMs),
        };
        _play.Glyph = playing ? PauseGlyph : PlayGlyph;
        _mute.Glyph = muted ? MuteGlyph : VolumeGlyph;
        _fullScreen.Glyph = fullScreen ? BackToWindowGlyph : FullScreenGlyph;
    }

    public static string Format(long ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    /// <summary>Marks the chapters on the seek bar (their starts, in seconds).</summary>
    public void SetChapters(IReadOnlyList<double> seconds)
    {
        _chapters = seconds;
        DrawTicks();
    }

    private void DrawTicks()
    {
        _ticks.Children.Clear();
        var track = _seek.ActualWidth - ThumbWidth;
        if (track <= 0 || _seek.Maximum <= 1) return;
        foreach (var seconds in _chapters)
        {
            if (seconds <= 0) continue;
            var tick = new Rectangle { Width = 2, Height = 10, Fill = new SolidColorBrush(ColorHelper.FromArgb(200, 255, 255, 255)) };
            Canvas.SetLeft(tick, ThumbWidth / 2 + seconds / _seek.Maximum * track - 1);
            Canvas.SetTop(tick, (_ticks.ActualHeight - tick.Height) / 2);
            _ticks.Children.Add(tick);
        }
    }

    private void OnSeekPointer(object sender, PointerRoutedEventArgs e)
    {
        // The thumb's middle travels the track, so its ends are half a thumb in
        var x = e.GetCurrentPoint(_seek).Position.X;
        var share = Math.Clamp((x - ThumbWidth / 2) / Math.Max(1, _seek.ActualWidth - ThumbWidth), 0, 1);
        var scale = Content.XamlRoot.RasterizationScale;
        SeekHover?.Invoke(share * _seek.Maximum, AppWindow.Position.X + (int)(e.GetCurrentPoint(null).Position.X * scale));
    }

    private static FontIcon Icon(string glyph) => new() { Glyph = glyph, FontSize = 16 };

    private static TextBlock Label() => new()
    {
        Text = "0:00",
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 220, 220, 220)),
        FontSize = 13,
    };

    private static Button IconButton(FontIcon icon, string tip, Action click)
    {
        // Never focused: a click must not take the keys away from the player
        var b = new Button
        {
            Content = icon,
            Width = 40,
            Height = 40,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            AllowFocusOnInteraction = false,
            IsTabStop = false,
        };
        ToolTipService.SetToolTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>The slider in a cell that takes the wheel over the whole height of the bar.</summary>
    private Grid WheelArea(Slider slider, Action<int> turned)
    {
        var area = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
        area.Children.Add(slider);
        area.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler((_, e) =>
        {
            // A notch is 120; a touchpad sends it in small pieces
            _wheel += e.GetCurrentPoint(area).Properties.MouseWheelDelta;
            var notches = _wheel / 120;
            _wheel -= notches * 120;
            if (notches != 0) turned(notches);
            e.Handled = true;
        }), true);
        return area;
    }

    private static void Add(Grid grid, int column, FrameworkElement element)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private static readonly SubclassProc NoFrameProc = (hwnd, msg, wParam, lParam, _, _) =>   // kept alive: Windows calls it
    {
        if (msg == WmNcCalcSize && wParam != 0) return 0;
        if (msg != WmEraseBkgnd) return DefSubclassProc(hwnd, msg, wParam, lParam);
        // Under the content only black is nothing: any other colour would show through the bar
        GetClientRect(hwnd, out var client);
        FillRect(wParam, ref client, GetStockObject(BlackBrush));
        return 1;
    };

    private delegate nint SubclassProc(IntPtr hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint data);

    private const uint WmNcCalcSize = 0x0083, WmEraseBkgnd = 0x0014;
    private const int GwlExStyle = -20;
    private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010, SwpFrameChanged = 0x0020;
    private const int GwlpHwndParent = -8;
    private const nint WsExNoActivate = 0x08000000;
    private const nint WsExToolWindow = 0x00000080;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmWcpDoNotRound = 1;
    private const int DwmWcpRound = 2;
    private const int DwmwaColorNone = unchecked((int)0xFFFFFFFE);
    private const uint DwmBbEnable = 1, DwmBbBlurRegion = 2;
    private const int BlackBrush = 4;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(IntPtr hwnd, int index, nint value);

    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, nuint id, nuint data);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(IntPtr hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmBlurBehind
    {
        public uint Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool Enable;
        public IntPtr Region;
        [MarshalAs(UnmanagedType.Bool)] public bool TransitionOnMaximized;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DwmBlurBehind behind);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int index);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out PointInt32 point);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hdc, ref Rect rect, IntPtr brush);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
}
