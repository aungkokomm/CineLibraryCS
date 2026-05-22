using Microsoft.UI.Xaml.Data;

namespace CineLibraryCS.Converters;

/// <summary>true → "✓" (watched), false → "○" (unwatched). Used by the TV
/// episode list's watched toggle.</summary>
public class WatchedGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => (value is bool b && b) ? "✓" : "○";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
