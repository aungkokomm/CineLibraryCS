using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
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
    private const double FadeHeight = 20, SeekHeight = 36, ButtonsHeight = 54, RowHeight = 48, ThumbWidth = 14, PlaySize = 44;   // DIPs
    // DIPs: under these the end time and file go, then the volume slider, then the 10 and 30 second skips,
    // and last the capsule moves to the left, so it never covers Close
    private const double InfoFrom = 760, VolumeFrom = 600, SkipsFrom = 500, CentredFrom = 440;
    private const string VolumeGlyph = "\uE767", MuteGlyph = "\uE74F", FullScreenGlyph = "\uE740", BackToWindowGlyph = "\uE73F";
    // 5.3.0: shapes in a 100 x 100 box, stretched to the size they're given: Play, Pause and the glass cap of a ball
    private const string PlayPath = "M 30,28 C 30,23 35,20.5 39,23 L 75,45.5 C 79,48 79,52 75,54.5 L 39,77 C 35,79.5 30,77 30,72 Z";
    private const string PausePath = "M 34,24 H 40 A 4,4 0 0 1 44,28 V 72 A 4,4 0 0 1 40,76 H 34 A 4,4 0 0 1 30,72 V 28 A 4,4 0 0 1 34,24 Z "
                                   + "M 60,24 H 66 A 4,4 0 0 1 70,28 V 72 A 4,4 0 0 1 66,76 H 60 A 4,4 0 0 1 56,72 V 28 A 4,4 0 0 1 60,24 Z";
    private const string CapPath = "M 9,47 C 11,22 30,9 50,9 C 70,9 89,22 91,47 C 91,51 88,52 84,50 C 74,45 63,43 50,43 C 37,43 26,45 16,50 C 12,52 9,51 9,47 Z";

    private readonly FontIcon _mute = Icon(VolumeGlyph), _fullScreen = Icon(FullScreenGlyph);
    // 5.3.0: Play and Pause drawn glass-white with a soft shadow, on a glass ball that follows the button's state
    private readonly Grid _playShape = Glyph(PlayPath, 16, 18, new Thickness(3, 0, 0, 0)), _pauseShape = Glyph(PausePath, 14, 16, new Thickness(0));
    private Button? _playButton;
    private Ellipse? _ballOutline, _ballEdge, _ballBody;
    private Microsoft.UI.Xaml.Shapes.Path? _ballCap;
    private readonly TextBlock _time = Label(), _length = Label(), _rowLabel = Label(), _ends = Label(), _file = Label();
    private readonly Slider _seek = new() { Minimum = 0, Maximum = 1, IsThumbToolTipEnabled = false };
    private readonly Slider _volume = new() { Minimum = 0, Maximum = 100, Width = 96, IsThumbToolTipEnabled = false };
    private readonly Grid _row = new() { Height = RowHeight, Padding = new Thickness(16, 0, 16, 0), Visibility = Visibility.Collapsed };
    private readonly StackPanel _rowItems = new() { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
    private readonly Canvas _ticks = new() { IsHitTestVisible = false };
    private readonly Button _previous, _next, _back, _forward, _subtitles;
    private readonly Grid _volumeArea;
    private readonly StackPanel _info = new() { Margin = new Thickness(8, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly ColumnDefinition _leftColumn = new() { Width = new GridLength(1, GridUnitType.Star) };
    private readonly RowDefinition _fadeRow = new() { Height = new GridLength(FadeHeight) };
    private readonly Grid _root = new() { RequestedTheme = ElementTheme.Dark, Opacity = 0 };
    private readonly DispatcherQueueTimer _fadeOut;
    private readonly Storyboard _pulse = new();
    private Ellipse? _halo;
    private Rectangle? _track, _done;
    private bool _pulsing, _closed;
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
        var root = _root;
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(_fadeRow);
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(SeekHeight) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(ButtonsHeight) });
        // 5.1.0 (#25): it fades in and out
        if (!UiSettings.ReduceMotion) root.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(150) };
        _fadeOut = DispatcherQueue.CreateTimer();
        _fadeOut.Interval = TimeSpan.FromMilliseconds(170);
        _fadeOut.IsRepeating = false;
        _fadeOut.Tick += (_, _) => { if (!_closed && _root.Opacity == 0) AppWindow.Hide(); };
        Closed += (_, _) => { _closed = true; _fadeOut.Stop(); _pulse.Stop(); };
        // Dark enough to keep the white controls readable over a white picture
        _row.Background = new SolidColorBrush(ColorHelper.FromArgb(150, 0, 0, 0));

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

        // The picture fades into the bar (closed while the row above is open), then dark glass
        var fade = new Border { Background = Glass((0, 0, 0, 0, 0), (150, 0, 0, 0, 1)) };
        Grid.SetRow(fade, 1);
        root.Children.Add(fade);
        var glass = new Border { Background = Glass((150, 0, 0, 0, 0), (205, 0, 0, 0, 1)) };
        Grid.SetRow(glass, 2);
        Grid.SetRowSpan(glass, 2);
        root.Children.Add(glass);

        // Two lines, the seek bar with its times and under it the buttons
        var seekRow = new Grid { Padding = new Thickness(16, 0, 8, 0) };
        Grid.SetRow(seekRow, 2);
        root.Children.Add(seekRow);
        foreach (var width in new[] { 0, 1, 0 })
            seekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = width == 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        Add(seekRow, 0, _time);
        _seek.Margin = new Thickness(12, 0, 12, 0);
        var seekArea = WheelArea(_seek, notches => SeekWheel?.Invoke(notches));
        _ticks.Margin = _seek.Margin;
        seekArea.Children.Insert(0, _ticks);
        seekArea.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnSeekPointer), true);
        seekArea.PointerExited += (_, _) => { SeekHover?.Invoke(null, 0); SetTrack(false); };
        _seek.Loaded += (_, _) =>
        {
            _track = Part<Rectangle>(_seek, "HorizontalTrackRect");
            _done = Part<Rectangle>(_seek, "HorizontalDecreaseRect");
        };
        _seek.SizeChanged += (_, _) => DrawTicks();
        _ticks.SizeChanged += (_, _) => DrawTicks();
        Add(seekRow, 1, seekArea);
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
        Add(seekRow, 2, lengthButton);

        var bar = new Grid { Padding = new Thickness(8, 0, 8, 0) };
        Grid.SetRow(bar, 3);
        root.Children.Add(bar);
        bar.ColumnDefinitions.Add(_leftColumn);
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // When it ends and what's playing
        _ends.Text = _file.Text = "";
        _file.FontSize = 11;
        _file.Opacity = 0.7;
        _file.TextTrimming = TextTrimming.CharacterEllipsis;
        _info.Children.Add(_ends);
        _info.Children.Add(_file);
        Add(bar, 0, _info);

        // One glass capsule, after Windows Media Player 11, with Play in the middle. Previous and next
        // (episode, or chapter) stay, dimmed when there's none
        _previous = CapsuleButton(Icon("\uF8AC", 14), "Previous (P)", () => Previous?.Invoke());
        _next = CapsuleButton(Icon("\uF8AD", 14), "Next (N)", () => Next?.Invoke());
        SetSkip(null, null);
        _back = CapsuleButton(Icon("\uED3C", 19), "Back 10 seconds (Left)", () => Back?.Invoke());
        _forward = CapsuleButton(Icon("\uED3D", 19), "Forward 30 seconds", () => Forward?.Invoke());
        _subtitles = CapsuleButton(Icon("\uED1E", 15), "Subtitles", () => Subtitles?.Invoke());
        _mute.FontSize = _fullScreen.FontSize = 15;
        _volume.Width = 80;
        _volume.Margin = new Thickness(2, 0, 10, 0);
        _volumeArea = WheelArea(_volume, notches => VolumeWheel?.Invoke(notches));

        var skips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var button in new[] { _previous, PlayButton(), _next }) skips.Children.Add(button);
        var pill = new Grid { VerticalAlignment = VerticalAlignment.Center };
        pill.Children.Add(Pill());
        pill.Children.Add(skips);

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(10, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach (var part in new FrameworkElement[]
        {
            CapsuleButton(_fullScreen, "Full screen (F, double-click)", () => FullScreen?.Invoke()),
            CapsuleButton(Icon("\uE8D6", 15), "Audio", () => Audio?.Invoke()),
            _subtitles,
            Separator(),
            _back,
            pill,
            _forward,
            Separator(),
            CapsuleButton(_mute, "Mute (M)", () => Mute?.Invoke()),
            _volumeArea,
        })
            controls.Children.Add(part);
        var middle = new Grid { VerticalAlignment = VerticalAlignment.Center };
        middle.Children.Add(Capsule());
        middle.Children.Add(controls);
        Add(bar, 1, middle);

        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(IconButton(Icon("\uE711"), "Close (Esc)", () => CloseClicked?.Invoke()));
        Add(bar, 2, right);

        foreach (var slider in new[] { _seek, _volume })
        {
            Knob(slider);
            // 5.3.0: the handle a little glass ball, as Play
            slider.Loaded += (_, _) => { if (Part<Microsoft.UI.Xaml.Controls.Primitives.Thumb>(slider, "HorizontalThumb") is { } thumb) thumb.Template = BallThumb.Value; };
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
        var height = (int)Math.Round(((Row.Length > 0 ? RowHeight : FadeHeight) + SeekHeight + ButtonsHeight) * scale);
        var position = AppWindow.Position;
        var size = AppWindow.Size;
        if (position.X == left && position.Y == bottom - height && size.Width == width && size.Height == height) return;
        Fit(width / scale);
        AppWindow.MoveAndResize(new RectInt32(left, bottom - height, width, height));
    }

    // A narrow window leaves things out, so what stays is never clipped
    private void Fit(double room)
    {
        _info.Visibility = room >= InfoFrom ? Visibility.Visible : Visibility.Collapsed;
        _leftColumn.Width = room >= CentredFrom ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        _volumeArea.Visibility = room >= VolumeFrom ? Visibility.Visible : Visibility.Collapsed;
        _back.Visibility = _forward.Visibility = room >= SkipsFrom ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetShown(bool shown)
    {
        if (shown)
        {
            _fadeOut.Stop();   // a fade out on its way is called off
            if (!AppWindow.IsVisible) AppWindow.Show(false);
            _root.Opacity = 1;
            return;
        }
        if (!AppWindow.IsVisible || _root.Opacity == 0) return;
        _root.Opacity = 0;
        SetTrack(false);
        if (UiSettings.ReduceMotion) AppWindow.Hide();
        else _fadeOut.Start();   // hidden once it has faded
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
        _fadeRow.Height = new GridLength(0);
        Moved?.Invoke();
    }

    public void SetRowLabel(string label) => _rowLabel.Text = label;

    /// <summary>Shows Previous and Next with what they go to (null hides one).</summary>
    public void SetSkip(string? previous, string? next)
    {
        Enable(_previous, previous);
        Enable(_next, next);
    }

    // There always, dimmed when there's nothing to go to
    private static void Enable(Button button, string? tip)
    {
        button.IsEnabled = tip != null;
        button.Opacity = tip != null ? 1 : 0.35;
        ToolTipService.SetToolTip(button, tip);
    }

    /// <summary>How far (DIPs) the Subtitles button's right edge is from the window's right.
    /// The menus open with their right edge there.</summary>
    public double MenuAnchorFromRight
    {
        get
        {
            var root = (FrameworkElement)Content;
            return root.ActualWidth - _subtitles.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(_subtitles.ActualWidth, 0)).X;
        }
    }

    public void HideRow()
    {
        if (Row.Length == 0) return;
        Row = "";
        _row.Visibility = Visibility.Collapsed;
        _fadeRow.Height = new GridLength(FadeHeight);
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
        var ends = "Ends " + DateTime.Now.AddMilliseconds(left).ToString("t");
        _ends.Text = lengthMs > 0 ? ends : "";
        _length.Text = _timeMode switch
        {
            1 => "-" + Format(left),
            2 => ends,
            _ => Format(lengthMs),
        };
        _playShape.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        _pauseShape.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        Pulse(!playing);
        _mute.Glyph = muted ? MuteGlyph : VolumeGlyph;
        _fullScreen.Glyph = fullScreen ? BackToWindowGlyph : FullScreenGlyph;
    }

    public void SetFile(string path) => _file.Text = System.IO.Path.GetFileName(path);

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
        SetTrack(true);
        // The thumb's middle travels the track, so its ends are half a thumb in
        var x = e.GetCurrentPoint(_seek).Position.X;
        var share = Math.Clamp((x - ThumbWidth / 2) / Math.Max(1, _seek.ActualWidth - ThumbWidth), 0, 1);
        var scale = Content.XamlRoot.RasterizationScale;
        SeekHover?.Invoke(share * _seek.Maximum, AppWindow.Position.X + (int)(e.GetCurrentPoint(null).Position.X * scale));
    }

    private static FontIcon Icon(string glyph, double size = 16) => new() { Glyph = glyph, FontSize = size };

    private static TextBlock Label() => new()
    {
        Text = "0:00",
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 220, 220, 220)),
        FontSize = 13,
    };

    private static Button IconButton(FrameworkElement icon, string tip, Action click)
    {
        // Never focused: a click must not take the keys away from the player
        var b = new Button
        {
            Content = icon,
            Width = 40,
            Height = 40,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(20),
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

    private static LinearGradientBrush Glass(params (byte A, byte R, byte G, byte B, double At)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0.5, 0), EndPoint = new Windows.Foundation.Point(0.5, 1) };
        foreach (var stop in stops) brush.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(stop.A, stop.R, stop.G, stop.B), Offset = stop.At });
        return brush;
    }

    private static RadialGradientBrush Radial(Windows.Foundation.Point centre, double radius, params (byte A, byte R, byte G, byte B, double At)[] stops)
    {
        var brush = new RadialGradientBrush { Center = centre, GradientOrigin = centre, RadiusX = radius, RadiusY = radius };
        foreach (var stop in stops) brush.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(stop.A, stop.R, stop.G, stop.B), Offset = stop.At });
        return brush;
    }

    // A button on the capsule: smaller, its icon in the theme's light purple; under the mouse a purple glow
    // rises from its bottom edge (5.3.0), as on Windows Media Player's buttons
    private static Button CapsuleButton(FontIcon icon, string tip, Action click)
    {
        icon.Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 214, 202, 255));
        var b = IconButton(icon, tip, click);
        b.Width = b.Height = 36;
        b.CornerRadius = new CornerRadius(18);
        b.Resources["ButtonBackgroundPointerOver"] = Radial(new Windows.Foundation.Point(0.5, 1), 0.75, (170, 214, 196, 255, 0), (90, 167, 139, 250, 0.45), (0, 167, 139, 250, 1));
        b.Resources["ButtonBackgroundPressed"] = Radial(new Windows.Foundation.Point(0.5, 1), 0.75, (90, 214, 196, 255, 0), (45, 167, 139, 250, 0.45), (0, 167, 139, 250, 1));
        b.Resources["ButtonBackgroundDisabled"] = new SolidColorBrush(Colors.Transparent);
        return b;
    }

    // Small knobs (glass balls, BallThumb), a thin seek bar
    private static void Knob(Slider slider)
    {
        slider.Resources["SliderTrackThemeHeight"] = 3.0;
        slider.Resources["SliderHorizontalThumbWidth"] = ThumbWidth;
        slider.Resources["SliderHorizontalThumbHeight"] = ThumbWidth;
    }

    /// <summary>5.3.0: a slider's handle as a little glass ball, layered as Play's (PlayButton).</summary>
    private static readonly Lazy<ControlTemplate> BallThumb = new(() => (ControlTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load($$"""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Thumb">
          <Grid>
            <Ellipse Fill="#FF2B1673"/>
            <Ellipse Margin="0.8">
              <Ellipse.Fill>
                <LinearGradientBrush StartPoint="0.5,1" EndPoint="0.5,0">
                  <GradientStop Offset="0" Color="#FFF4ECFF"/>
                  <GradientStop Offset="0.16" Color="#FFC4ACFF"/>
                  <GradientStop Offset="0.32" Color="#FF9670F2"/>
                  <GradientStop Offset="0.55" Color="#FF482C9E"/>
                  <GradientStop Offset="1" Color="#FFAAA4BE"/>
                </LinearGradientBrush>
              </Ellipse.Fill>
            </Ellipse>
            <Ellipse Margin="1.8">
              <Ellipse.Fill>
                <RadialGradientBrush Center="0.5,1" GradientOrigin="0.5,1" RadiusX="1" RadiusY="1">
                  <GradientStop Offset="0" Color="#FFE2CEFF"/>
                  <GradientStop Offset="0.3" Color="#FFAA80F6"/>
                  <GradientStop Offset="0.6" Color="#FF7042D8"/>
                  <GradientStop Offset="1" Color="#FF2A1280"/>
                </RadialGradientBrush>
              </Ellipse.Fill>
            </Ellipse>
            <Path Stretch="Fill" VerticalAlignment="Top" Margin="2.4,1.6,2.4,0" Height="5.4" Data="{{CapPath}}">
              <Path.Fill>
                <LinearGradientBrush StartPoint="0.5,0" EndPoint="0.5,1">
                  <GradientStop Offset="0" Color="#D0FFFFFF"/>
                  <GradientStop Offset="1" Color="#30DCD2FF"/>
                </LinearGradientBrush>
              </Path.Fill>
            </Path>
          </Grid>
        </ControlTemplate>
        """));

    private static Geometry PathData(string data) => (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), data);

    /// <summary>5.3.0: a glass-white shape with a soft shadow under it (from the shape's own outline), as the
    /// symbols on Windows Media Player's buttons. The cell holds the shadow, then the shape.</summary>
    private static Grid Glyph(string data, double width, double height, Thickness margin)
    {
        var shape = new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = PathData(data),
            Stretch = Stretch.Uniform,
            Width = width,
            Height = height,
            Fill = Glass((255, 255, 255, 255, 0), (255, 247, 244, 255, 0.45), (255, 233, 226, 250, 0.55), (255, 210, 200, 236, 1)),
            Stroke = new SolidColorBrush(Colors.White),
            StrokeThickness = 0.6,
        };
        var shadow = new Grid { Width = width, Height = height };
        var cell = new Grid { Margin = margin, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        cell.Children.Add(shadow);
        cell.Children.Add(shape);
        var compositor = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(shadow).Compositor;
        var drop = compositor.CreateDropShadow();
        drop.BlurRadius = 2.5f;
        drop.Offset = new System.Numerics.Vector3(-0.6f, 1f, 0);
        drop.Color = ColorHelper.FromArgb(150, 20, 0, 60);
        drop.Mask = shape.GetAlphaMask();
        var sprite = compositor.CreateSpriteVisual();
        sprite.Size = new System.Numerics.Vector2((float)width, (float)height);
        sprite.Shadow = drop;
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetElementChildVisual(shadow, sprite);
        return cell;
    }

    // The seek bar grows under the mouse
    private void SetTrack(bool thick)
    {
        if (_track is null || _done is null) return;
        _track.Height = _done.Height = thick ? 6 : 3;
    }

    private static T? Part<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found && found.Name == name) return found;
            if (Part<T>(child, name) is { } deeper) return deeper;
        }
        return null;
    }

    // While paused, Play glows slowly brighter and dimmer
    private void Pulse(bool on)
    {
        if (on == _pulsing || _halo is null) return;
        _pulsing = on;
        if (!on)
        {
            _pulse.Stop();
            _halo.Opacity = 0;
        }
        else if (UiSettings.ReduceMotion) _halo.Opacity = 0.6;
        else _pulse.Begin();
    }

    private static Border Separator() => new()
    {
        Width = 1,
        Height = 20,
        Margin = new Thickness(4, 0, 4, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Background = new SolidColorBrush(ColorHelper.FromArgb(70, 255, 255, 255)),
    };

    // Previous, Play and Next share a smaller capsule of their own
    private static Border Pill() => new()
    {
        Height = 28,
        Margin = new Thickness(4, 0, 4, 0),
        VerticalAlignment = VerticalAlignment.Center,
        CornerRadius = new CornerRadius(14),
        BorderThickness = new Thickness(1),
        BorderBrush = Glass((90, 255, 255, 255, 0), (30, 255, 255, 255, 1)),
        Background = Glass((60, 255, 255, 255, 0), (10, 255, 255, 255, 0.5), (0, 0, 0, 0, 0.51), (40, 0, 0, 0, 1)),
    };

    // The glass the buttons sit on; Play stands out of it
    private static Grid Capsule()
    {
        var capsule = new Grid { Height = 34, VerticalAlignment = VerticalAlignment.Center };
        capsule.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(17),
            BorderThickness = new Thickness(1),
            BorderBrush = Glass((150, 255, 255, 255, 0), (50, 255, 255, 255, 1)),
            Background = Glass((255, 62, 58, 84, 0), (255, 30, 27, 42, 0.5), (255, 12, 10, 20, 0.51), (255, 36, 30, 54, 1)),
        });
        capsule.Children.Add(new Border
        {
            Height = 14,
            Margin = new Thickness(4, 2, 4, 0),
            VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(13, 13, 4, 4),
            Background = Glass((60, 255, 255, 255, 0), (6, 255, 255, 255, 1)),
        });
        return capsule;
    }

    // A glass ball in the theme's purple, as Windows Media Player 11's (5.3.0): a dark outline, a rim lit
    // from below, a body glowing up from the bottom and a glass cap. Its look follows the button (SetBall).
    // It sits in a slightly bigger button, whose background is the glow under the mouse.
    private Button PlayButton()
    {
        var ball = new Grid { Width = PlaySize, Height = PlaySize };
        _ballOutline = new Ellipse();
        _ballEdge = new Ellipse { Margin = new Thickness(1) };
        _ballBody = new Ellipse { Margin = new Thickness(2.5) };
        _ballCap = new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = PathData(CapPath),
            Stretch = Stretch.Fill,
            Width = PlaySize * 0.8,
            Height = PlaySize * 0.42,
            Margin = new Thickness(0, PlaySize * 0.07, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Fill = Glass((200, 255, 255, 255, 0), (120, 236, 228, 255, 0.55), (40, 220, 210, 255, 1)),
        };
        _pauseShape.Visibility = Visibility.Collapsed;
        foreach (var layer in new FrameworkElement[] { _ballOutline, _ballEdge, _ballBody, _ballCap, _playShape, _pauseShape }) ball.Children.Add(layer);
        SetBall("Normal");

        _halo = new Ellipse
        {
            Width = PlaySize + 8,
            Height = PlaySize + 8,
            Opacity = 0,
            IsHitTestVisible = false,
            Fill = Radial(new Windows.Foundation.Point(0.5, 0.5), 0.5, (210, 167, 139, 250, 0.7), (0, 167, 139, 250, 1)),
        };
        var glow = new DoubleAnimation
        {
            From = 0.15,
            To = 0.95,
            Duration = new Duration(TimeSpan.FromSeconds(1.4)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Storyboard.SetTarget(glow, _halo);
        Storyboard.SetTargetProperty(glow, "Opacity");
        _pulse.Children.Add(glow);
        var face = new Grid { Width = PlaySize + 8, Height = PlaySize + 8 };
        face.Children.Add(_halo);
        face.Children.Add(ball);

        var b = IconButton(face, "Play or pause (Space)", () => PlayPause?.Invoke());
        b.Width = b.Height = PlaySize + 8;
        b.CornerRadius = new CornerRadius((PlaySize + 8) / 2);
        b.Resources["ButtonBackgroundPointerOver"] = Radial(new Windows.Foundation.Point(0.5, 0.5), 0.5, (170, 167, 139, 250, 0.7), (0, 167, 139, 250, 1));
        b.Resources["ButtonBackgroundPressed"] = Radial(new Windows.Foundation.Point(0.5, 0.5), 0.5, (90, 167, 139, 250, 0.7), (0, 167, 139, 250, 1));
        // the button's own states (those that show its glow) give the ball its look
        var watching = false;
        b.Loaded += (_, _) =>
        {
            if (watching || VisualTreeHelper.GetChildrenCount(b) == 0 || VisualTreeHelper.GetChild(b, 0) is not FrameworkElement template) return;
            if (VisualStateManager.GetVisualStateGroups(template).FirstOrDefault(g => g.Name == "CommonStates") is not { } states) return;
            states.CurrentStateChanged += (_, e) => SetBall(e.NewState?.Name ?? "Normal");
            watching = true;
        };
        _playButton = b;
        return b;
    }

    /// <summary>5.3.0: the ball's colours for the button's state (Normal, PointerOver, Pressed, Disabled):
    /// brighter under the mouse, darker pressed, grey when it can't be used.</summary>
    private void SetBall(string state)
    {
        if (_ballOutline is null || _ballEdge is null || _ballBody is null || _ballCap is null) return;
        // the rim from the bottom up, the body from its glow at the bottom up to the top
        static LinearGradientBrush Rim(params (byte R, byte G, byte B, double At)[] stops)
        {
            var brush = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0.5, 1), EndPoint = new Windows.Foundation.Point(0.5, 0) };
            foreach (var s in stops) brush.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(255, s.R, s.G, s.B), Offset = s.At });
            return brush;
        }
        static RadialGradientBrush Body(params (byte R, byte G, byte B, double At)[] stops) =>
            Radial(new Windows.Foundation.Point(0.5, 1), 1, stops.Select(s => ((byte)255, s.R, s.G, s.B, s.At)).ToArray());
        var (outline, edge, body, cap) = state switch
        {
            "PointerOver" => (ColorHelper.FromArgb(255, 52, 32, 138),
                Rim((255, 255, 255, 0), (222, 206, 255, 0.14), (178, 142, 255, 0.28), (96, 62, 192, 0.52), (112, 104, 150, 0.78), (176, 170, 198, 1)),
                Body((242, 228, 255, 0), (196, 156, 255, 0.22), (136, 88, 236, 0.45), (82, 44, 190, 0.7), (48, 24, 140, 1)), 1.0),
            "Pressed" => (ColorHelper.FromArgb(255, 34, 16, 96),
                Rim((214, 198, 252, 0), (166, 138, 240, 0.14), (120, 86, 220, 0.28), (58, 34, 134, 0.52), (88, 80, 122, 0.78), (150, 144, 170, 1)),
                Body((188, 160, 250, 0), (128, 86, 222, 0.22), (80, 40, 176, 0.45), (44, 20, 128, 0.7), (28, 12, 94, 1)), 0.75),
            "Disabled" => (ColorHelper.FromArgb(255, 58, 58, 66),
                Rim((230, 230, 234, 0), (190, 190, 198, 0.14), (150, 150, 160, 0.28), (84, 84, 94, 0.52), (104, 104, 114, 0.78), (160, 160, 170, 1)),
                Body((186, 186, 186, 0), (140, 140, 140, 0.22), (98, 98, 98, 0.45), (64, 64, 64, 0.7), (42, 42, 42, 1)), 0.6),
            _ => (ColorHelper.FromArgb(255, 43, 22, 115),
                Rim((244, 236, 255, 0), (196, 172, 255, 0.14), (150, 112, 242, 0.28), (72, 44, 158, 0.52), (104, 96, 140, 0.78), (170, 164, 190, 1)),
                Body((226, 206, 255, 0), (170, 128, 246, 0.22), (112, 66, 216, 0.45), (62, 30, 163, 0.7), (36, 16, 118, 1)), 1.0),
        };
        _ballOutline.Fill = new SolidColorBrush(outline);
        _ballEdge.Fill = edge;
        _ballBody.Fill = body;
        _ballCap.Opacity = cap;
        _playShape.Opacity = _pauseShape.Opacity = state == "Disabled" ? 0.5 : 1;
    }

    /// <summary>The slider in a cell that takes the wheel over the whole height of its line.</summary>
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
