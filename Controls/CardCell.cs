using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace CineLibraryCS.Controls;

/// <summary>
/// v4.4.0: holds one poster card in a grid cell, sized the way movie and show
/// cards size themselves since 4.1.0. It asks for the S / M / L / XL size, then
/// fills the cell the grid gives it, up to the width of a 2:3 poster at that
/// height, kept to the left so the grid lines up with the page title.
/// </summary>
public sealed class CardCell : Panel
{
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
        => double.IsInfinity(cellWidth) ? CardWidth
         : Math.Max(CardWidth, Math.Min(cellWidth, Math.Max(CardWidth, CardHeight * 2 / 3)));
}
