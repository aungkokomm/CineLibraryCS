using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using CineLibraryCS.Models;
using CineLibraryCS.Services;
using System.Runtime.InteropServices.WindowsRuntime;

namespace CineLibraryCS.Views;

public sealed partial class TvEpisodeCard : UserControl
{
    public static readonly DependencyProperty EpisodeProperty =
        DependencyProperty.Register(nameof(Episode), typeof(TvEpisodeItem), typeof(TvEpisodeCard),
            new PropertyMetadata(null, OnEpisodeChanged));

    public TvEpisodeItem? Episode
    {
        get => (TvEpisodeItem?)GetValue(EpisodeProperty);
        set => SetValue(EpisodeProperty, value);
    }

    // Static so the host page wires once, regardless of recycling.
    public static event Action<TvEpisodeItem>? AnyPlay;
    public static event Action<TvEpisodeItem>? AnyWatchedToggle;

    public TvEpisodeCard()
    {
        InitializeComponent();
        PointerEntered += (_, _) => { HoverOverlay.Visibility = Visibility.Visible; HoverOverlay.Opacity = 1; };
        PointerExited  += (_, _) => { HoverOverlay.Opacity = 0; HoverOverlay.Visibility = Visibility.Collapsed; };
        DoubleTapped   += (_, _) => { if (Episode != null) AnyPlay?.Invoke(Episode); };
    }

    private static void OnEpisodeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TvEpisodeCard c) return;
        if (e.OldValue is TvEpisodeItem prev) prev.PropertyChanged -= c.OnEpPropChanged;
        if (e.NewValue is TvEpisodeItem ep) { c.Populate(ep); ep.PropertyChanged += c.OnEpPropChanged; }
    }

    private void OnEpPropChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (Episode != null && e.PropertyName == nameof(TvEpisodeItem.IsWatched))
            ApplyWatchedVisual(Episode.IsWatched);
    }

    private void Populate(TvEpisodeItem ep)
    {
        CodeText.Text = ep.Code;
        TitleText.Text = ep.Title;
        MetaText.Text = string.Join("  ·  ", new[] { ep.RuntimeText, ep.RatingText }
            .Where(s => !string.IsNullOrEmpty(s)));
        ApplyWatchedVisual(ep.IsWatched);
        LoadThumbAsync(ep.LocalThumb);
    }

    private void ApplyWatchedVisual(bool watched)
    {
        WatchedBadge.Visibility = watched ? Visibility.Visible : Visibility.Collapsed;
        WatchedDim.Visibility = watched ? Visibility.Visible : Visibility.Collapsed;
        WatchedToggleBtn.Content = watched ? "✓ Watched" : "○ Mark watched";
    }

    private void OnPlay(object sender, RoutedEventArgs e)
    {
        if (Episode != null) AnyPlay?.Invoke(Episode);
    }

    private void OnToggleWatched(object sender, RoutedEventArgs e)
    {
        if (Episode != null) AnyWatchedToggle?.Invoke(Episode);
    }

    private int _token;
    private async void LoadThumbAsync(string? relPath)
    {
        var my = ++_token;
        ThumbImage.Source = null;
        ThumbPlaceholder.Visibility = Visibility.Visible;
        if (relPath == null) return;
        var full = AppState.Instance.Db.GetCachedImagePath(relPath);
        if (full == null) return;
        try
        {
            var bytes = await Task.Run(() => ImageCache.GetOrLoad(relPath, full));
            if (my != _token || bytes == null) return;
            var bmp = new BitmapImage { DecodePixelWidth = 300 };
            using var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await ms.WriteAsync(bytes.AsBuffer());
            if (my != _token) return;
            ms.Seek(0);
            await bmp.SetSourceAsync(ms);
            if (my != _token) return;
            ThumbImage.Source = bmp;
            ThumbPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch { }
    }
}
