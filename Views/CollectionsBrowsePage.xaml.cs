using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using CineLibraryCS.Services;

namespace CineLibraryCS.Views;

/// <summary>
/// Tile data-model for the Collections grid. We set CoverImage eagerly
/// in Load() via BitmapImage.UriSource — the decode itself stays lazy
/// (only happens when the Image element actually renders), so virtualization
/// still pays off. The original lazy-via-ElementPrepared design didn't fire
/// reliably because DataContext isn't always set by the time the event runs.
/// </summary>
public partial class CollectionTileVm : ObservableObject
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string CountText { get; init; } = "";
    [ObservableProperty] private BitmapImage? _coverImage;
}

public sealed partial class CollectionsBrowsePage : Page
{
    private List<CollectionTileVm> _tiles = new();

    public CollectionsBrowsePage()
    {
        InitializeComponent();
    }

    public void Load()
    {
        var entries = AppState.Instance.Db.GetCollectionGrid();
        CountText.Text = entries.Count == 0
            ? ""
            : $"{entries.Count} collection{(entries.Count == 1 ? "" : "s")}";
        if (entries.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            GridRepeater.ItemsSource = null;
            _tiles = new();
            return;
        }
        EmptyState.Visibility = Visibility.Collapsed;

        _tiles = entries.Select(e =>
        {
            var vm = new CollectionTileVm
            {
                Id = e.Id,
                Name = e.Name,
                CountText = $"{e.Count} movie{(e.Count == 1 ? "" : "s")}",
            };
            if (e.CoverPoster != null)
            {
                var fullPath = AppState.Instance.Db.GetCachedImagePath(e.CoverPoster);
                if (fullPath != null && File.Exists(fullPath))
                {
                    try
                    {
                        // BitmapImage with UriSource = file:///… is the cheapest
                        // way to hand the Image element a source. The bitmap
                        // doesn't actually decode until the Image is realized
                        // and visible, so creating one per tile is OK.
                        // DecodePixelWidth caps the in-memory size to roughly
                        // the rendered poster width.
                        var bmp = new BitmapImage { DecodePixelWidth = 200 };
                        // # / ? in the file name would otherwise be eaten by
                        // the URI parser as fragment / query delimiters.
                        bmp.UriSource = new Uri(fullPath.Replace("#", "%23").Replace("?", "%3F"));
                        vm.CoverImage = bmp;
                    }
                    catch { /* unreadable file → tile shows the placeholder bg */ }
                }
            }
            return vm;
        }).ToList();
        GridRepeater.ItemsSource = _tiles;
    }

    private void OnTileClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not int id) return;
        var tile = _tiles.FirstOrDefault(t => t.Id == id);
        if (tile == null) return;
        if (App.MainWindow is MainWindow mw)
            mw.NavigateLibraryByCollection(id, tile.Name);
    }
}
