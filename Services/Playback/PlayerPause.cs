using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace CineLibraryCS.Services.Playback;

/// <summary>What the pause screen shows; Facts is one line (year, length, rating, genres).</summary>
internal sealed record PauseInfo(string Title, string Facts, string Plot, string People, string? PosterPath);

/// <summary>
/// v4.10.0: a few seconds into a pause, the item's details from the library over the dimmed
/// picture, in a window under the controls. What the mouse does on it counts as done on the
/// video, so a click still plays and the controls still come up.
/// </summary>
internal sealed class PlayerPause : Window
{
    private const double NarrowWidth = 760, ShortHeight = 460;   // DIPs: under these the poster leaves the text the room
    private const double TextWidest = 640, RowMargin = 72, PosterRoom = 220 + 32;   // DIPs
    private readonly Grid _root = new() { RequestedTheme = ElementTheme.Dark, Opacity = 0 };
    private readonly Border _poster = new() { Width = 220, Height = 330, CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _left = Line(15, 1);
    private readonly StackPanel _text = new() { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
    private readonly bool _hasPoster;

    public IntPtr Hwnd { get; }
    public bool Shown => AppWindow.IsVisible;

    public event Action? Clicked, DoubleClicked;
    /// <summary>Over the video, in client pixels.</summary>
    public event Action<int, int>? MouseMoved, RightClicked;
    /// <summary>+1 up, -1 down.</summary>
    public event Action<int>? Wheel;

    public PlayerPause(IntPtr owner, PauseInfo info)
    {
        var dim = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0.5), EndPoint = new Windows.Foundation.Point(1, 0.5) };
        dim.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(235, 0, 0, 0), Offset = 0 });
        dim.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(200, 0, 0, 0), Offset = 0.6 });
        dim.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(110, 0, 0, 0), Offset = 1 });
        _root.Background = dim;

        var title = Line(32, 2);
        title.Text = info.Title;
        title.FontWeight = FontWeights.SemiBold;
        var facts = Line(14, 1);
        facts.Text = info.Facts;
        facts.Opacity = 0.75;
        var plot = Line(15, 7);
        plot.Text = info.Plot;
        plot.LineHeight = 23;
        var people = Line(13, 2);
        people.Text = info.People;
        people.Opacity = 0.75;

        foreach (var line in new[] { title, facts, _left, plot, people })
            if (line == _left || line.Text.Length > 0) _text.Children.Add(line);

        _hasPoster = info.PosterPath != null;
        if (info.PosterPath is { } poster)
            _poster.Child = new Image { Source = new BitmapImage(new Uri(poster)) { DecodePixelWidth = 440 }, Stretch = Stretch.UniformToFill };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 32,
            Margin = new Thickness(RowMargin, 0, RowMargin, 96),   // the bottom clears the controls
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        row.Children.Add(_poster);
        row.Children.Add(_text);
        _root.Children.Add(row);

        _root.Tapped += (_, _) => Clicked?.Invoke();
        _root.DoubleTapped += (_, _) => DoubleClicked?.Invoke();
        _root.RightTapped += (_, e) => { var p = e.GetPosition(_root); RightClicked?.Invoke(Pixels(p.X), Pixels(p.Y)); };
        _root.PointerMoved += (_, e) => { var p = e.GetCurrentPoint(_root).Position; MouseMoved?.Invoke(Pixels(p.X), Pixels(p.Y)); };
        _root.PointerWheelChanged += (_, e) => Wheel?.Invoke(e.GetCurrentPoint(_root).Properties.MouseWheelDelta > 0 ? 1 : -1);
        if (!UiSettings.ReduceMotion) _root.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(250) };

        Content = _root;
        Title = "CineLibrary pause screen";
        SystemBackdrop = new SeeThroughBackdrop();
        Hwnd = PlayerControls.MakeOverlay(this, owner, menu: false);
    }

    private int Pixels(double dips) => (int)(dips * _root.XamlRoot.RasterizationScale);

    private static TextBlock Line(double size, int lines) => new()
    {
        FontSize = size,
        Foreground = new SolidColorBrush(Colors.White),
        TextWrapping = TextWrapping.WrapWholeWords,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxLines = lines,
    };

    public void SetTimeLeft(string text) => _left.Text = text;

    /// <summary>Covers the video (physical pixels), under the controls.</summary>
    public void ShowOver(PlayerWindow.RECT area, IntPtr controls, double scale)
    {
        int width = area.Right - area.Left, height = area.Bottom - area.Top;
        var poster = _hasPoster && width / scale >= NarrowWidth && height / scale >= ShortHeight;
        _poster.Visibility = poster ? Visibility.Visible : Visibility.Collapsed;
        // a horizontal StackPanel lets its children be as wide as they like, and clips them
        _text.MaxWidth = Math.Clamp(width / scale - 2 * RowMargin - (poster ? PosterRoom : 0), 120, TextWidest);
        var at = AppWindow.Position;
        var size = AppWindow.Size;
        if (at.X != area.Left || at.Y != area.Top || size.Width != width || size.Height != height)
            AppWindow.MoveAndResize(new RectInt32(area.Left, area.Top, width, height));
        if (AppWindow.IsVisible) return;
        AppWindow.Show(false);
        SetWindowPos(Hwnd, controls, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate);
        _root.Opacity = 1;
    }

    public void HideScreen()
    {
        if (!AppWindow.IsVisible) return;
        AppWindow.Hide();
        _root.Opacity = 0;
    }

    private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoActivate = 0x0010;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
