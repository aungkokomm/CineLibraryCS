using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace CineLibraryCS.Controls;

/// <summary>
/// v4.4.0: holds one poster card in a grid cell, sized the way movie and show
/// cards size themselves since 4.1.0. It asks for the S / M / L / XL size, then
/// fills the cell the grid gives it (v4.8.0: all of it; see <see cref="FitGrid"/>).
/// </summary>
public sealed class CardCell : Panel
{
    /// <summary>
    /// v4.8.0: a grid of poster cards, a 2:3 poster with its caption under it.
    /// Cells widen to fill the row and grow taller in step, so the poster keeps
    /// its shape at any width. S has closer gaps, so as many fit in a row as before.
    /// </summary>
    public static void FitGrid(UniformGridLayout layout, (double Width, double Height) size, string density)
    {
        layout.MinItemWidth = size.Width;
        layout.MinItemHeight = size.Height;
        layout.MinColumnSpacing = GapFor(density);
        layout.MinRowSpacing = density == "S" ? 20 : 28;
        layout.ItemsStretch = UniformGridLayoutItemsStretch.Uniform;
    }

    /// <summary>v4.8.0: the space between cards side by side, in a grid or a row.</summary>
    public static double GapFor(string density) => density == "S" ? 16 : 24;

    /// <summary>
    /// v4.9.0 (#25): the size a grid draws its cards at, given the width it lays them
    /// out in (its available width, not its drawn one): as many as fit at their
    /// S / M / L / XL size with a gap after each, widened by whole pixels to fill the
    /// row, and taller in step. UniformGridLayout's own sums, measured to match.
    /// </summary>
    public static (double Width, double Height) StretchedSize(UniformGridLayout layout, double rowWidth)
    {
        var (width, height, gap) = (layout.MinItemWidth, layout.MinItemHeight, layout.MinColumnSpacing);
        var across = Math.Max(1, Math.Floor(rowWidth / (width + gap)));
        var extra = Math.Truncate((rowWidth - (across * (width + gap) - gap)) / across);
        return (width + extra, height + height * extra / width);
    }

    /// <summary>
    /// v4.8.0: an arrow key on a card. Focus goes to the nearest card that way in
    /// the same grid or row; up or down past its edge, to whatever is there (the
    /// toolbar above, the movies under a row of shows). False when nothing is.
    /// </summary>
    public static bool MoveFocus(UIElement card, Windows.System.VirtualKey key)
    {
        var direction = key switch
        {
            Windows.System.VirtualKey.Left  => Microsoft.UI.Xaml.Input.FocusNavigationDirection.Left,
            Windows.System.VirtualKey.Right => Microsoft.UI.Xaml.Input.FocusNavigationDirection.Right,
            Windows.System.VirtualKey.Up    => Microsoft.UI.Xaml.Input.FocusNavigationDirection.Up,
            Windows.System.VirtualKey.Down  => Microsoft.UI.Xaml.Input.FocusNavigationDirection.Down,
            _ => (Microsoft.UI.Xaml.Input.FocusNavigationDirection?)null,
        };
        if (direction is not { } dir || card.XamlRoot is not { } xamlRoot) return false;
        DependencyObject? grid = card;
        while (grid != null && grid is not ItemsRepeater)
            grid = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(grid);
        if (grid != null && Microsoft.UI.Xaml.Input.FocusManager.TryMoveFocus(dir,
                new Microsoft.UI.Xaml.Input.FindNextElementOptions { SearchRoot = grid }))
            return true;
        return dir is Microsoft.UI.Xaml.Input.FocusNavigationDirection.Up or Microsoft.UI.Xaml.Input.FocusNavigationDirection.Down
            && Microsoft.UI.Xaml.Input.FocusManager.TryMoveFocus(dir,
                new Microsoft.UI.Xaml.Input.FindNextElementOptions { SearchRoot = xamlRoot.Content });
    }

    public double CardWidth { get; set; } = 170;
    public double CardHeight { get; set; } = 300;

    // v4.5.0 (#22): the card is laid out at the width it's drawn at, or a title
    // that fit on one line of the wider card still took two and sat a line too high.
    protected override Size MeasureOverride(Size availableSize)
    {
        var size = new Size(DrawnWidth(availableSize.Width), CardHeight);
        foreach (var child in Children) child.Measure(size);
        return new Size(CardWidth, CardHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = DrawnWidth(finalSize.Width);
        // No second Measure here at the wider size: measuring at two sizes in
        // turn kept layout from settling, and the posters never loaded.
        foreach (var child in Children)
            child.Arrange(new Rect(0, 0, width, finalSize.Height));
        return finalSize;
    }

    private double DrawnWidth(double cellWidth)
        => double.IsInfinity(cellWidth) ? CardWidth : cellWidth;
}
