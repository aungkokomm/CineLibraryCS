using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace CineLibraryCS.Services;

/// <summary>
/// Resolves a theme-aware brush honouring the app's ACTUAL UI theme.
///
/// <para><c>Application.Current.Resources["X"]</c> resolves a brush from the
/// merged <c>ThemeDictionaries</c> against <c>Application.RequestedTheme</c>
/// (which follows the <b>system</b> theme), not the in-app theme the user
/// picked. For anyone whose Windows theme differs from their chosen
/// CineLibrary theme, that returns the wrong variant — e.g. a light (white)
/// card brush behind a dark dialog, which made the shortcuts dialog's key
/// chips render as white boxes with invisible text.</para>
///
/// <para>This reads the variant matching the live window theme instead, so
/// code-built brushes always match what's on screen. Brand brushes
/// (BrandPurpleBrush, AccentRedBrush, …) live outside ThemeDictionaries and
/// fall through to the flat app-level lookup.</para>
/// </summary>
public static class ThemeBrushes
{
    public static SolidColorBrush Get(string key)
    {
        var theme = (App.MainWindow?.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Dark;
        var dictKey = theme == ElementTheme.Light ? "Light" : "Dark";

        foreach (var md in Application.Current.Resources.MergedDictionaries)
        {
            if (md.ThemeDictionaries.TryGetValue(dictKey, out var obj)
                && obj is ResourceDictionary rd
                && rd.TryGetValue(key, out var b)
                && b is SolidColorBrush br)
                return br;
        }

        // Brand / theme-agnostic brushes live at the dictionary root.
        if (Application.Current.Resources.TryGetValue(key, out var flat) && flat is SolidColorBrush fb)
            return fb;

        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }
}
