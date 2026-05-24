using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;
using CineLibraryCS.Models;
using System.Runtime.InteropServices.WindowsRuntime;

namespace CineLibraryCS.Services;

/// <summary>
/// v2.9 — Renders a user list (or any movie set) into a shareable PNG.
///
/// Earlier attempt used Popup as the parent for the offscreen panel, but
/// WinUI 3's Popup doesn't always realize its child into the visual tree
/// (especially when positioned off-screen), so RenderTargetBitmap captured
/// a blank bitmap. The reliable pattern is to parent the panel directly
/// in the window's root Panel, push it off-screen with a TranslateTransform,
/// render, and remove — this guarantees realization and a real visual to
/// capture.
/// </summary>
public static class ListImageExporter
{
    public static async Task<bool> ExportAsync(
        XamlRoot xamlRoot, string listName, List<MovieListItem> movies, string outPath)
    {
        if (xamlRoot == null) return false;
        if (xamlRoot.Content is not Panel rootPanel)
        {
            System.Diagnostics.Debug.WriteLine("ListImageExporter: XamlRoot.Content is not a Panel");
            return false;
        }

        const int posterW = 180;
        const int posterH = 270;
        const int cols = 4;
        const int gap = 14;
        const int padding = 24;
        const int headerH = 84;
        // Cap to 12 rows (~48 posters) so very long lists still produce a usable image.
        var capped = movies.Take(cols * 12).ToList();
        int rows = Math.Max(1, (capped.Count + cols - 1) / cols);
        int contentW = cols * posterW + (cols - 1) * gap;
        int totalW = contentW + padding * 2;
        int totalH = headerH + rows * posterH + (rows - 1) * gap + padding * 2;

        // ── Compose offscreen panel ─────────────────────────────────────────
        var root = new Grid
        {
            Width = totalW,
            Height = totalH,
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x10, 0x10, 0x14)),
            // Off-screen position via transform — element stays in the live
            // visual tree so RenderTargetBitmap can capture it, but it never
            // shows up on screen for the user.
            RenderTransform = new TranslateTransform { X = -50000, Y = -50000 },
            IsHitTestVisible = false,
        };

        // Header
        var header = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 4,
            Padding = new Thickness(padding, padding, padding, 0),
        };
        header.Children.Add(new TextBlock
        {
            Text = listName.ToUpper(),
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            CharacterSpacing = 120,
            Foreground = new SolidColorBrush(Colors.White),
        });
        var subText = movies.Count == capped.Count
            ? $"{movies.Count} movie{(movies.Count == 1 ? "" : "s")}  ·  CineLibrary"
            : $"{capped.Count} of {movies.Count} movies  ·  CineLibrary";
        header.Children.Add(new TextBlock
        {
            Text = subText,
            FontSize = 12,
            CharacterSpacing = 80,
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
        });
        Grid.SetRow(header, 0);
        root.Children.Add(header);
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(headerH) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Poster grid
        var posterGrid = new Grid { Margin = new Thickness(padding, 0, padding, padding) };
        for (int c = 0; c < cols; c++)
            posterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(posterW) });
        for (int r = 0; r < rows; r++)
            posterGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(posterH) });

        // Track image elements so we can wait for ImageOpened before rendering.
        var pendingImages = new List<Image>();

        for (int i = 0; i < capped.Count; i++)
        {
            var movie = capped[i];
            int col = i % cols;
            int rowIdx = i / cols;
            var cell = BuildPosterCell(movie, posterW, posterH, pendingImages);
            Grid.SetColumn(cell, col);
            Grid.SetRow(cell, rowIdx);
            cell.Margin = new Thickness(
                col == 0 ? 0 : gap / 2,
                rowIdx == 0 ? 0 : gap / 2,
                col == cols - 1 ? 0 : gap / 2,
                rowIdx == rows - 1 ? 0 : gap / 2);
            posterGrid.Children.Add(cell);
        }
        Grid.SetRow(posterGrid, 1);
        root.Children.Add(posterGrid);

        // ── Attach to the window's root Panel ───────────────────────────────
        // If the root is a Grid with row/col defs, our element lands at (0,0)
        // but we've translated it -50000,-50000 so its on-screen footprint is
        // invisible. The Grid still allocates layout space for it, so we
        // also explicitly Panel.ZIndex it behind everything.
        Canvas.SetZIndex(root, -1);
        rootPanel.Children.Add(root);

        try
        {
            // Force layout. Two passes — once on the panel, once on the
            // window root — covers the case where the new child changes
            // the parent's measured size.
            root.Measure(new Windows.Foundation.Size(totalW, totalH));
            root.Arrange(new Windows.Foundation.Rect(0, 0, totalW, totalH));
            root.UpdateLayout();

            // Wait for image decodes. Track an ImageOpened/ImageFailed
            // signal per Image; bail on a hard timeout so a stubborn
            // decode can't block the export forever.
            await WaitForImagesAsync(pendingImages, totalTimeoutMs: 8000);

            // One more layout pass after images set their sources — they
            // can cause measure invalidation when the BitmapImage source
            // resolves (Image picks up natural size).
            root.UpdateLayout();
            await Task.Delay(50);

            var rtb = new RenderTargetBitmap();
            await rtb.RenderAsync(root, totalW, totalH);
            var px = await rtb.GetPixelsAsync();

            // Sanity: did we render anything? If the buffer is all zeros
            // RTB silently failed (the WinUI 3 quirk we're working around).
            if (rtb.PixelWidth == 0 || rtb.PixelHeight == 0)
            {
                System.Diagnostics.Debug.WriteLine("RenderTargetBitmap returned 0×0");
                return false;
            }

            await EncodePngAsync(outPath, px.ToArray(), rtb.PixelWidth, rtb.PixelHeight);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ListImageExporter failed: {ex.Message}");
            return false;
        }
        finally
        {
            rootPanel.Children.Remove(root);
        }
    }

    /// <summary>
    /// Wait until every Image either fires ImageOpened/ImageFailed or the
    /// total timeout expires. Each Image whose source loaded synchronously
    /// (cached bytes) signals immediately; cold reads can be slower.
    /// </summary>
    private static async Task WaitForImagesAsync(List<Image> images, int totalTimeoutMs)
    {
        if (images.Count == 0) return;
        var pending = new HashSet<Image>(images);
        var tcs = new TaskCompletionSource<bool>();

        void Done(Image img)
        {
            lock (pending)
            {
                pending.Remove(img);
                if (pending.Count == 0) tcs.TrySetResult(true);
            }
        }

        foreach (var img in images)
        {
            var captured = img;
            captured.ImageOpened += (_, _) => Done(captured);
            captured.ImageFailed += (_, _) => Done(captured);
        }

        var timeout = Task.Delay(totalTimeoutMs);
        await Task.WhenAny(tcs.Task, timeout);
    }

    private static async Task EncodePngAsync(string outPath, byte[] bgra8, int w, int h)
    {
        var dir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var fs = new FileStream(outPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        using var ras = fs.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ras);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            (uint)w, (uint)h,
            96, 96,
            bgra8);
        await encoder.FlushAsync();
    }

    private static FrameworkElement BuildPosterCell(MovieListItem movie, int w, int h, List<Image> pending)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x20, 0x20, 0x28)),
            CornerRadius = new CornerRadius(8),
            Width = w,
            Height = h,
        };
        var inner = new Grid();

        // Placeholder behind the poster.
        var placeholder = new TextBlock
        {
            Text = "🎬",
            FontSize = 36,
            Opacity = 0.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Colors.White),
        };
        inner.Children.Add(placeholder);

        // Poster. Read bytes synchronously from the cache; build the
        // BitmapImage with an InMemoryRandomAccessStream so SetSourceAsync
        // races against our render wait but is usually fast for cached
        // posters (LRU cache hits avoid disk altogether).
        if (!string.IsNullOrEmpty(movie.LocalPoster))
        {
            var full = AppState.Instance.Db.GetCachedImagePath(movie.LocalPoster);
            if (full != null)
            {
                var bytes = ImageCache.GetOrLoad(movie.LocalPoster, full);
                if (bytes != null)
                {
                    var bmp = new BitmapImage { DecodePixelWidth = w };
                    var image = new Image { Source = bmp, Stretch = Stretch.UniformToFill };
                    inner.Children.Add(image);
                    pending.Add(image);
                    _ = SetBitmapSourceAsync(bmp, bytes);
                }
            }
        }

        // Bottom gradient + title + year strip.
        var stripBorder = new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            CornerRadius = new CornerRadius(0, 0, 8, 8),
            Padding = new Thickness(10, 8, 10, 10),
        };
        var grad = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(0, 1),
        };
        grad.GradientStops.Add(new GradientStop { Offset = 0,    Color = Color.FromArgb(0x00, 0, 0, 0) });
        grad.GradientStops.Add(new GradientStop { Offset = 0.4,  Color = Color.FromArgb(0x99, 0, 0, 0) });
        grad.GradientStops.Add(new GradientStop { Offset = 1,    Color = Color.FromArgb(0xF2, 0, 0, 0) });
        stripBorder.Background = grad;

        var strip = new StackPanel { Spacing = 2 };
        strip.Children.Add(new TextBlock
        {
            Text = movie.Title,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2,
            TextWrapping = TextWrapping.WrapWholeWords,
        });
        if (movie.Year.HasValue)
            strip.Children.Add(new TextBlock
            {
                Text = movie.Year.ToString(),
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            });
        stripBorder.Child = strip;
        inner.Children.Add(stripBorder);

        border.Child = inner;
        return border;
    }

    private static async Task SetBitmapSourceAsync(BitmapImage bmp, byte[] bytes)
    {
        try
        {
            using var ms = new InMemoryRandomAccessStream();
            await ms.WriteAsync(bytes.AsBuffer());
            ms.Seek(0);
            await bmp.SetSourceAsync(ms);
        }
        catch { /* image failed will fire in WaitForImagesAsync */ }
    }
}
