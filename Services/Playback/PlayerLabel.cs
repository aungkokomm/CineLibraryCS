using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace CineLibraryCS.Services.Playback;

/// <summary>
/// v4.10.0: a line of text in a small see-through window over the video: what a key just
/// did, the time under the mouse on the seek bar, the title in full screen. A window for
/// the same reason as the controls, and like them it never takes the focus.
/// </summary>
internal sealed class PlayerLabel : Window
{
    private readonly TextBlock _text;
    private readonly double _height;
    private RectInt32 _shownRect;

    public IntPtr Hwnd { get; }
    public bool Shown => AppWindow.IsVisible;
    public string Text => _text.Text;

    private PlayerLabel(IntPtr owner, double fontSize, double height, double padding, Brush background, double corner, bool semiBold)
    {
        _height = height;
        _text = new TextBlock
        {
            FontSize = fontSize,
            FontWeight = semiBold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(padding, 0, padding, 0),
        };
        var root = new Grid { Background = background, CornerRadius = new CornerRadius(corner), RequestedTheme = ElementTheme.Dark };
        root.Children.Add(_text);
        Content = root;
        Title = "CineLibrary player label";
        SystemBackdrop = new SeeThroughBackdrop();
        Hwnd = PlayerControls.MakeOverlay(this, owner, menu: false);
    }

    /// <summary>What a key or the wheel just did, when the controls aren't up to show it.</summary>
    public static PlayerLabel Note(IntPtr owner) =>
        new(owner, 16, 40, 14, new SolidColorBrush(ColorHelper.FromArgb(170, 0, 0, 0)), 8, false);

    /// <summary>The time under the mouse on the seek bar.</summary>
    public static PlayerLabel Tip(IntPtr owner) =>
        new(owner, 13, 30, 10, new SolidColorBrush(ColorHelper.FromArgb(200, 0, 0, 0)), 6, false);

    /// <summary>The title along the top in full screen, fading into the picture.</summary>
    public static PlayerLabel TitleStrip(IntPtr owner)
    {
        var shade = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(0, 1) };
        shade.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(190, 0, 0, 0), Offset = 0 });
        shade.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(0, 0, 0, 0), Offset = 1 });
        return new(owner, 20, 72, 24, shade, 0, true);
    }

    /// <summary>
    /// Shows the text in a window as wide as it needs (at least <paramref name="minWidth"/>,
    /// physical pixels), where <paramref name="place"/> puts a window of that width and height.
    /// </summary>
    public void ShowText(string text, double scale, Func<int, int, PointInt32> place, int minWidth = 0)
    {
        _text.Text = text;
        _text.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = Math.Max(minWidth, (int)Math.Ceiling(_text.DesiredSize.Width * scale));
        var height = (int)Math.Round(_height * scale);
        var at = place(width, height);
        var rect = new RectInt32(at.X, at.Y, width, height);
        if (rect.X != _shownRect.X || rect.Y != _shownRect.Y || rect.Width != _shownRect.Width || rect.Height != _shownRect.Height)
        {
            AppWindow.MoveAndResize(rect);
            _shownRect = rect;
        }
        if (!AppWindow.IsVisible) AppWindow.Show(false);
    }

    public void HideText()
    {
        if (AppWindow.IsVisible) AppWindow.Hide();
    }
}
