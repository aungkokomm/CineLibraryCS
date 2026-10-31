using System.Text.RegularExpressions;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace CineLibraryCS.Services.Playback;

/// <summary>What the menu lists: the audio or the subtitle tracks, with the one in use.</summary>
internal sealed record MenuSection(string Kind, string Header, IReadOnlyList<(int Id, string Name)> Tracks, int Current);

/// <summary>A row of the right-click menu: a label and choices side by side. Current -1: they're actions, none stays chosen.</summary>
internal sealed record MenuChoices(string Kind, string Label, IReadOnlyList<string> Choices, int Current);

/// <summary>
/// v4.10.0: the Audio or Subtitles menu, a small window that opens on the right above the
/// controls, or what the bar doesn't have at the mouse on a right-click, like the controls
/// never taking the focus. Frosted glass over the film. A list that scrolls, a line to a
/// track: the language, then the rest of its name (often a site's tag, or "Track 2").
/// </summary>
internal sealed partial class PlayerMenu : Window
{
    private const double Width = 240, OnlineWidth = 460, MoreWidth = 390, RowHeight = 32, RowGap = 1, ChoiceHeight = 36,
                          LabelWidth = 62, HeaderHeight = 26, EdgeHeight = 8, MaxHeight = 320;   // DIPs
    private static readonly Windows.UI.Color Check = ColorHelper.FromArgb(255, 196, 181, 253);
    private readonly Grid _root = new() { RequestedTheme = ElementTheme.Dark, Opacity = 0 };
    private readonly StackPanel _list = new() { Padding = new Thickness(0, EdgeHeight / 2, 0, EdgeHeight / 2) };
    /// <summary>The row in the Subtitles menu that looks online, in the place of a track's id.</summary>
    public const int FindOnline = -2;
    private double _height = EdgeHeight;
    private bool _closed;

    public IntPtr Hwnd { get; }
    /// <summary>"audio", "subtitles", "more" for the right-click menu, or "online" for what was found online.</summary>
    public string Kind { get; }

    /// <summary>A track was chosen ("audio" or "subtitles", and its VLC id), or a choice of the right-click menu (its kind and place).</summary>
    public event Action<string, int>? Chosen;

    public PlayerMenu(IntPtr owner, string kind, MenuSection section) : this(owner, kind)
    {
        _list.Children.Add(new TextBlock
        {
            Text = section.Header,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.65,
            Height = HeaderHeight,
            Padding = new Thickness(14, 6, 0, 0),
        });
        _height += HeaderHeight;
        foreach (var (id, name) in section.Tracks) _list.Children.Add(Row(section, id, name));
        if (section.Tracks.Count == 0)
        {
            _list.Children.Add(new TextBlock { Text = "None in this file", FontSize = 13, Opacity = 0.65, Height = RowHeight, Padding = new Thickness(38, 6, 0, 0) });
            _height += RowHeight;
        }
    }

    /// <summary>The right-click menu. It stays open, so a speed or a timing can be tried out.</summary>
    public PlayerMenu(IntPtr owner, IReadOnlyList<MenuChoices> rows) : this(owner, "more")
    {
        foreach (var row in rows)
        {
            _list.Children.Add(ChoiceRow(row));
            _height += ChoiceHeight;
        }
    }

    private PlayerMenu(IntPtr owner, string kind)
    {
        Kind = kind;

        // The light catches the top edge of the glass
        var sheen = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(0, 1) };
        sheen.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(22, 255, 255, 255), Offset = 0 });
        sheen.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(0, 255, 255, 255), Offset = 0.35 });
        _root.Background = sheen;
        _root.Children.Add(new ScrollViewer { Content = _list });
        if (!UiSettings.ReduceMotion) _root.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(120) };
        Content = _root;
        Title = "CineLibrary player menu";
        SystemBackdrop = new GlassBackdrop();
        Hwnd = PlayerControls.MakeOverlay(this, owner, menu: true);
        Closed += (_, _) => _closed = true;
    }

    private Button Row(MenuSection section, int id, string name)
    {
        var (main, sub) = Split(name);
        var current = id == section.Current;
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (current || id == FindOnline)
            row.Children.Add(new FontIcon
            {
                Glyph = current ? "\uE73E" : "\uE721",
                FontSize = 12,
                Foreground = current ? new SolidColorBrush(Check) : White(230),
                VerticalAlignment = VerticalAlignment.Center,
            });
        var language = new TextBlock
        {
            Text = main,
            FontSize = 13,
            FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(language, 1);
        row.Children.Add(language);
        if (sub.Length > 0)
        {
            var rest = new TextBlock { Text = sub, FontSize = 12, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(rest, 2);
            row.Children.Add(rest);
        }
        _height += RowHeight + RowGap;
        var button = Flat(row, current ? White(22) : new SolidColorBrush(Colors.Transparent));
        button.Height = RowHeight;
        button.Margin = new Thickness(4, RowGap / 2, 4, RowGap / 2);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Padding = new Thickness(8, 0, 10, 0);
        button.Click += (_, _) => Chosen?.Invoke(section.Kind, id);
        return button;
    }

    private Grid ChoiceRow(MenuChoices choices)
    {
        var row = new Grid { Height = ChoiceHeight, Padding = new Thickness(14, 0, 8, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = choices.Label, FontSize = 13, Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center });
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(chips, 1);
        row.Children.Add(chips);
        for (var i = 0; i < choices.Choices.Count; i++)
        {
            var place = i;
            var chip = Flat(new TextBlock { Text = choices.Choices[i], FontSize = 13 }, i == choices.Current ? White(46) : new SolidColorBrush(Colors.Transparent));
            chip.Height = 28;
            chip.MinWidth = 40;
            chip.Padding = new Thickness(8, 0, 8, 0);
            chip.Click += (_, _) =>
            {
                if (choices.Current >= 0)
                    foreach (var other in chips.Children.OfType<Button>())
                        other.Background = other == chip ? White(46) : new SolidColorBrush(Colors.Transparent);
                Chosen?.Invoke(choices.Kind, place);
            };
            chips.Children.Add(chip);
        }
        return row;
    }

    private static Button Flat(UIElement content, Brush background)
    {
        var button = new Button
        {
            Content = content,
            Background = background,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(5),
            AllowFocusOnInteraction = false,
            IsTabStop = false,
        };
        button.Resources["ButtonBackgroundPointerOver"] = White(34);
        button.Resources["ButtonBackgroundPressed"] = White(20);
        return button;
    }

    private static SolidColorBrush White(byte alpha) => new(ColorHelper.FromArgb(alpha, 255, 255, 255));

    private (int Width, int Height) SizeAt(double scale) =>
        ((int)Math.Round(Kind switch { "online" => OnlineWidth, "more" => MoreWidth, _ => Width } * scale), (int)Math.Round(Math.Min(_height, MaxHeight) * scale));

    /// <summary>Opens it with its bottom-right corner at the given point (physical pixels).</summary>
    public void ShowAt(int right, int bottom, double scale)
    {
        if (_closed) return;   // a closed window can't be moved: Windows refuses ("The parameter is incorrect")
        var (width, height) = SizeAt(scale);
        AppWindow.MoveAndResize(new RectInt32(right - width, bottom - height, width, height));
        AppWindow.Show(false);
        _root.Opacity = 1;
    }

    /// <summary>Opens it at the mouse, kept inside the video (physical pixels).</summary>
    public void ShowAtMouse(int x, int y, PlayerWindow.RECT video, double scale)
    {
        if (_closed) return;
        var (width, height) = SizeAt(scale);
        var left = Math.Clamp(x, video.Left, Math.Max(video.Left, video.Right - width));
        var top = Math.Clamp(y, video.Top, Math.Max(video.Top, video.Bottom - height));
        AppWindow.MoveAndResize(new RectInt32(left, top, width, height));
        AppWindow.Show(false);
        _root.Opacity = 1;
    }

    // VLC names a track "<its title> - [<language>]", or "Track 2 - [Hindi]"
    [GeneratedRegex(@"^(?<title>.*?)\s*-\s*\[(?<lang>[^\]]+)\]$")]
    private static partial Regex TrackName();

    private static (string Main, string Sub) Split(string name)
    {
        if (name is "Disable" or "") return ("Off", "");
        var m = TrackName().Match(name);
        return m.Success ? (m.Groups["lang"].Value, m.Groups["title"].Value) : (name, "");
    }

    /// <summary>What a track goes by when it's remembered: its language, else its whole name.</summary>
    public static string Language(string? name) => Split(name ?? "").Main;
}
