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

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = new Size(CardWidth, CardHeight);
        foreach (var child in Children) child.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = Math.Min(finalSize.Width, Math.Max(CardWidth, CardHeight * 2 / 3));
        // No second Measure here at the wider size: measuring at two sizes in
        // turn kept layout from settling, and the posters never loaded.
        foreach (var child in Children)
            child.Arrange(new Rect(0, 0, width, finalSize.Height));
        return finalSize;
    }
}
