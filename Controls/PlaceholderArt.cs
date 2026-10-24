using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CineLibraryCS.Controls;

/// <summary>
/// v4.8.0: a stand-in poster for a title that has none: its name in large type
/// on a deep two-colour gradient. The colours come from the title, so a movie
/// keeps the same ones every time. White text reads on all of them, in both themes.
/// </summary>
public static class PlaceholderArt
{
    private static readonly (uint Top, uint Bottom)[] Palette =
    {
        (0xFF3B2A6B, 0xFF1A1233),   // violet
        (0xFF1F4E5A, 0xFF0D2328),   // teal
        (0xFF5A2340, 0xFF27101D),   // plum
        (0xFF2E3B55, 0xFF141A27),   // slate
        (0xFF2F4A2C, 0xFF132012),   // forest
        (0xFF5A3A1E, 0xFF27190C),   // umber
    };

    public static LinearGradientBrush BrushFor(string? title)
    {
        var (top, bottom) = Palette[Hash(title) % Palette.Length];
        return new LinearGradientBrush
        {
            StartPoint = new(0, 0),
            EndPoint = new(1, 1),
            GradientStops =
            {
                new GradientStop { Color = ToColor(top), Offset = 0 },
                new GradientStop { Color = ToColor(bottom), Offset = 1 },
            },
        };
    }

    /// <summary>The title's type size on a card this wide.</summary>
    public static double FontSizeFor(double cardWidth) => Math.Clamp(Math.Round(cardWidth / 8), 14, 28);

    // string.GetHashCode differs from run to run; this doesn't.
    private static int Hash(string? s)
    {
        unchecked
        {
            int h = 17;
            foreach (var ch in s ?? "") h = h * 31 + ch;
            return h & 0x7FFFFFFF;
        }
    }

    private static Color ToColor(uint argb)
        => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
}
