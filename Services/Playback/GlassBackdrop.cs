using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace CineLibraryCS.Services.Playback;

/// <summary>
/// v4.10.0: frosted glass behind a menu over the video. Windows draws acrylic only for the
/// active window, and these never take the focus, so this one always asks as if it were.
/// Where acrylic is off (Transparency effects, battery saver) Windows paints the fallback.
/// </summary>
internal sealed class GlassBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _acrylic;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        base.OnTargetConnected(target, root);
        if (!DesktopAcrylicController.IsSupported()) return;
        _acrylic = new DesktopAcrylicController
        {
            TintColor = ColorHelper.FromArgb(255, 18, 18, 24),
            TintOpacity = 0.6f,
            LuminosityOpacity = 0.7f,
            FallbackColor = ColorHelper.FromArgb(255, 32, 32, 38),
        };
        _acrylic.SetSystemBackdropConfiguration(new SystemBackdropConfiguration { IsInputActive = true, Theme = SystemBackdropTheme.Dark });
        _acrylic.AddSystemBackdropTarget(target);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        _acrylic?.RemoveSystemBackdropTarget(target);
        _acrylic?.Dispose();
        _acrylic = null;
        base.OnTargetDisconnected(target);
    }
}
