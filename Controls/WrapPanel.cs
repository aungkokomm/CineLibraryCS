using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace CineLibraryCS.Controls;

/// <summary>
/// Lays children out left to right and starts a new line when the next one
/// doesn't fit. WinUI has no built-in wrap panel for items of mixed widths.
/// </summary>
public sealed class WrapPanel : Panel
{
    public double HorizontalSpacing { get; set; }
    public double VerticalSpacing { get; set; }

    private double _measuredWidth;   // in full, before Size keeps it as a float

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        var (width, height) = Place(availableSize.Width, arrange: false);
        _measuredWidth = width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Break lines where Measure did. Aligned left, the panel is given its
        // measured width back as a float, a hair narrower than its widest line:
        // breaking again there moved the last item onto a line that no height
        // was kept for, over whatever comes next.
        Place(Math.Max(finalSize.Width, _measuredWidth), arrange: true);
        return finalSize;
    }

    private (double Width, double Height) Place(double width, bool arrange)
    {
        var line = new List<UIElement>();
        double x = 0, y = 0, lineHeight = 0, lineWidth = 0, widest = 0;

        void EndLine()
        {
            if (arrange)
            {
                double lx = 0;
                foreach (var c in line)
                {
                    c.Arrange(new Rect(lx, y, c.DesiredSize.Width, lineHeight));
                    lx += c.DesiredSize.Width + HorizontalSpacing;
                }
            }
            widest = Math.Max(widest, lineWidth);
            y += lineHeight + VerticalSpacing;
            line.Clear();
            x = 0;
            lineHeight = 0;
        }

        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            var size = child.DesiredSize;
            if (line.Count > 0 && x + size.Width > width) EndLine();
            line.Add(child);
            lineWidth = x + size.Width;   // the very sum the test above makes, so Arrange agrees
            x += size.Width + HorizontalSpacing;
            lineHeight = Math.Max(lineHeight, size.Height);
        }
        if (line.Count > 0) EndLine();

        return (widest, Math.Max(0, y - VerticalSpacing));
    }
}
