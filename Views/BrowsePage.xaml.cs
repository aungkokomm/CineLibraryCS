using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using CineLibraryCS.Services;
using System.Runtime.InteropServices.WindowsRuntime;

namespace CineLibraryCS.Views;

/// <summary>
/// Generic "browse by X" page: vertical stack of banner cards, each tinted
/// with a representative fanart and clicking applies the corresponding
/// library filter. Used for Genre / Decade / Rating / Studio.
/// </summary>
public sealed partial class BrowsePage : Page
{
    public BrowsePage()
    {
        InitializeComponent();
    }

    public DatabaseService.BrowseFacet Facet { get; private set; } = DatabaseService.BrowseFacet.Genre;

    public void Load(DatabaseService.BrowseFacet facet)
    {
        Facet = facet;
        PageTitleText.Text = facet switch
        {
            DatabaseService.BrowseFacet.Genre  => "BY GENRE",
            DatabaseService.BrowseFacet.Decade => "BY DECADE",
            DatabaseService.BrowseFacet.Rating => "BY RATING",
            DatabaseService.BrowseFacet.Studio => "BY STUDIO",
            _ => "BROWSE"
        };
        PageSubText.Text = facet switch
        {
            DatabaseService.BrowseFacet.Genre  => "Pick a genre to filter the library.",
            DatabaseService.BrowseFacet.Decade => "Movies grouped by their decade of release.",
            DatabaseService.BrowseFacet.Rating => "Movies grouped by their star rating.",
            DatabaseService.BrowseFacet.Studio => "Movies grouped by studio.",
            _ => ""
        };

        var entries = AppState.Instance.Db.GetBrowseEntries(facet);
        if (entries.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            BannerRepeater.ItemsSource = null;
            return;
        }
        EmptyState.Visibility = Visibility.Collapsed;
        var banners = entries.Select(BuildBanner).ToList();
        BannerRepeater.ItemsSource = banners;
    }

    private Button BuildBanner(DatabaseService.BrowseEntry e)
    {
        // Outer button = whole banner clickable. Inside: a grid layered with
        // fanart image (if any), a gradient tint, and the label.
        var btn = new Button
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(12),
            Height = 132,
        };
        btn.Click += (_, _) => OnBannerClick(e);

        var border = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBrush"],
        };
        var grid = new Grid();
        border.Child = grid;

        // Background fanart
        var img = new Image
        {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };
        grid.Children.Add(img);
        _ = LoadFanartAsync(img, e.SampleFanart ?? e.SamplePoster);

        // Gradient overlay — purple → transparent → black for readability
        var overlay = new Border();
        var gb = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0.5),
            EndPoint = new Windows.Foundation.Point(1, 0.5),
        };
        gb.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(0xCC, 0x10, 0x10, 0x18), Offset = 0 });
        gb.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(0x55, 0x10, 0x10, 0x18), Offset = 0.6 });
        gb.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(0x88, 0x10, 0x10, 0x18), Offset = 1 });
        overlay.Background = gb;
        grid.Children.Add(overlay);

        // Label + count
        var labelStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(28, 0, 28, 0),
            Spacing = 4,
        };
        labelStack.Children.Add(new TextBlock
        {
            Text = e.Label,
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
        });
        labelStack.Children.Add(new TextBlock
        {
            Text = $"{e.Count} movie{(e.Count == 1 ? "" : "s")}",
            FontSize = 12,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
        });
        grid.Children.Add(labelStack);

        btn.Content = border;
        return btn;
    }

    private static async Task LoadFanartAsync(Image img, string? relPath)
    {
        if (relPath == null) return;
        var fullPath = AppState.Instance.Db.GetCachedImagePath(relPath);
        if (fullPath == null) return;
        try
        {
            var bytes = await Task.Run(() => ImageCache.GetOrLoad(relPath, fullPath));
            if (bytes == null) return;
            var bmp = new BitmapImage { DecodePixelWidth = 800 };
            var ms = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await ms.WriteAsync(bytes.AsBuffer());
            ms.Seek(0);
            await bmp.SetSourceAsync(ms);
            img.Source = bmp;
        }
        catch { }
    }

    private void OnBannerClick(DatabaseService.BrowseEntry e)
    {
        if (App.MainWindow is not MainWindow mw) return;
        switch (Facet)
        {
            case DatabaseService.BrowseFacet.Genre:
                mw.NavigateLibraryByGenre(e.Key);
                break;
            case DatabaseService.BrowseFacet.Decade:
                mw.NavigateLibraryByDecade(int.Parse(e.Key), e.Label);
                break;
            case DatabaseService.BrowseFacet.Rating:
                mw.NavigateLibraryByRatingBand(e.Key, e.Label);
                break;
            case DatabaseService.BrowseFacet.Studio:
                mw.NavigateLibraryByStudio(e.Key);
                break;
        }
    }
}
