using Microsoft.UI.Xaml.Data;
using Windows.UI;

namespace CineLibraryCS.Converters;

public class BoolToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => (value is bool b && b)
            ? Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E)   // green online
            : Color.FromArgb(0xFF, 0x6B, 0x72, 0x80);   // grey offline

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
