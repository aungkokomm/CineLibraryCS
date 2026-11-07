using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace CineLibraryCS.Controls;

/// <summary>
/// A page header on one row: [title + count] [filter pills] ... [tools]. When
/// the row doesn't fit, the pills move to a second line, and in a very narrow
/// window the tools get their own line too (as Aryan's library header does).
/// Expects three children in that order; a collapsed child takes no room.
/// </summary>
public sealed class HeaderBar : Panel
{
    public double HorizontalSpacing { get; set; } = 16;
    public double VerticalSpacing { get; set; } = 8;

    // Which children share a line: 0 = all three, 1 = title + tools / pills,
    // 2 = title / pills + tools, 3 = one line each.
    private int _mode;

    protected override Size MeasureOverride(Size availableSize)
    {
        var natural = new Size(double.PositiveInfinity, double.PositiveInfinity);
        foreach (var child in Children) child.Measure(natural);
        var (title, pills, tools) = Parts();
        double w(UIElement? e) => e?.DesiredSize.Width ?? 0;
        double h(UIElement? e) => e?.DesiredSize.Height ?? 0;
        double gap(UIElement? e) => e == null ? 0 : HorizontalSpacing;

        var width = availableSize.Width;
        if (w(title) + gap(pills) + w(pills) + gap(tools) + w(tools) <= width) _mode = 0;
        else if (w(title) + gap(tools) + w(tools) <= width) _mode = 1;
        else if (w(pills) + gap(tools) + w(tools) <= width) _mode = 2;
        else _mode = 3;

        // Let the title shrink (it trims) when even it alone is too wide.
        if (title != null && w(title) > width) title.Measure(new Size(width, double.PositiveInfinity));
        // 5.3.0: and pills on a line of their own that's too narrow for them: in a WrapPanel they wrap
        if (_mode is 1 or 3 && pills != null && w(pills) > width) pills.Measure(new Size(width, double.PositiveInfinity));

        double height = _mode switch
        {
            0 => Math.Max(h(title), Math.Max(h(pills), h(tools))),
            1 => Math.Max(h(title), h(tools)) + Line(pills),
            2 => h(title) + Line(Math.Max(h(pills), h(tools))),
            _ => h(title) + Line(pills) + Line(tools),
        };
        return new Size(double.IsInfinity(width) ? w(title) + w(pills) + w(tools) : width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (title, pills, tools) = Parts();
        double h(UIElement? e) => e?.DesiredSize.Height ?? 0;
        var width = finalSize.Width;

        void Put(UIElement? e, double x, double top, double rowHeight)
        {
            if (e == null) return;
            var size = e.DesiredSize;
            e.Arrange(new Rect(x, top + (rowHeight - size.Height) / 2, Math.Min(size.Width, width - x), size.Height));
        }
        double RightX(UIElement? e) => Math.Max(0, width - (e?.DesiredSize.Width ?? 0));

        switch (_mode)
        {
            case 0:
            {
                var row = Math.Max(h(title), Math.Max(h(pills), h(tools)));
                Put(title, 0, 0, row);
                Put(pills, (title?.DesiredSize.Width ?? 0) + HorizontalSpacing, 0, row);
                Put(tools, RightX(tools), 0, row);
                break;
            }
            case 1:
            {
                var row = Math.Max(h(title), h(tools));
                Put(title, 0, 0, row);
                Put(tools, RightX(tools), 0, row);
                Put(pills, 0, row + VerticalSpacing, h(pills));
                break;
            }
            case 2:
            {
                var second = Math.Max(h(pills), h(tools));
                Put(title, 0, 0, h(title));
                Put(pills, 0, h(title) + VerticalSpacing, second);
                Put(tools, RightX(tools), h(title) + VerticalSpacing, second);
                break;
            }
            default:
            {
                Put(title, 0, 0, h(title));
                var y = h(title) + (pills == null ? 0 : VerticalSpacing);
                Put(pills, 0, y, h(pills));
                y += h(pills) + (tools == null ? 0 : VerticalSpacing);
                Put(tools, 0, y, h(tools));
                break;
            }
        }
        return finalSize;
    }

    private double Line(UIElement? e) => e == null ? 0 : VerticalSpacing + e.DesiredSize.Height;
    private double Line(double height) => height <= 0 ? 0 : VerticalSpacing + height;

    private (UIElement? title, UIElement? pills, UIElement? tools) Parts()
    {
        UIElement? Visible(int i) =>
            i < Children.Count && Children[i].Visibility == Visibility.Visible ? Children[i] : null;
        return (Visible(0), Visible(1), Visible(2));
    }
}
