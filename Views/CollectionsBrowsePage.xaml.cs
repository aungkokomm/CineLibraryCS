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
    public double CardWidth { get; init; }
    public double CardHeight { get; init; }
    public Visibility WatchedVisibility { get; init; } = Visibility.Collapsed;
    // v4.4.0 (#18): "3/5" and the progress bar, as on show cards
    public string ProgressText { get; init; } = "";
    public GridLength DoneWidth { get; init; }
    public GridLength LeftWidth { get; init; }
    [ObservableProperty] private BitmapImage? _coverImage;
}

public sealed partial class CollectionsBrowsePage : Page
{
    private List<CollectionTileVm> _tiles = new();

    // v3.8.0: sort / watched filter / poster size, remembered in prefs like
    // the All movies and All TV shows toolbars.
    private string _sort = "name:asc";
    private string _filter = "all";
    private double _cardWidth = 150, _cardHeight = 280;
    private bool _uiReady;
    private readonly string _noCollectionsHint;

    public CollectionsBrowsePage()
    {
        InitializeComponent();
        _noCollectionsHint = EmptyHint.Text;
        _sort = AppState.Instance.GetPref("collSort", "name:asc");
        _filter = AppState.Instance.GetPref("collFilter", "all");
        if (_filter is not ("all" or "unwatched" or "watched")) _filter = "all";
        ApplyDensity(AppState.Instance.GetPref("collDensity", "M"));
        SyncToolbar();
        _uiReady = true;
    }

    public void Load()
    {
        var all = AppState.Instance.Db.GetCollectionGrid();
        var entries = Sort(Filter(all)).ToList();
        PageCountText.Text = all.Count == 0 ? ""
            : entries.Count == all.Count ? $"{all.Count} collection{(all.Count == 1 ? "" : "s")}"
            : $"{entries.Count} of {all.Count} collections";
        CollFilterPills.Visibility = CollToolbar.Visibility =
            all.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (entries.Count == 0)
        {
            EmptyTitle.Text = all.Count == 0 ? "No collections yet"
                : _filter == "watched" ? "No fully watched collections yet" : "Nothing left to watch";
            EmptyHint.Text = all.Count == 0 ? _noCollectionsHint : "Choose All to see every collection.";
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
                CardWidth = _cardWidth,
                CardHeight = _cardHeight,
                WatchedVisibility = e.Watched >= e.Count ? Visibility.Visible : Visibility.Collapsed,
                ProgressText = $"{Math.Min(e.Watched, e.Count)}/{e.Count}",
                DoneWidth = new GridLength(Math.Min(e.Watched, e.Count), GridUnitType.Star),
                LeftWidth = new GridLength(Math.Max(e.Count - e.Watched, 0), GridUnitType.Star),
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
                        var bmp = new BitmapImage { DecodePixelWidth = (int)Math.Max(200, _cardWidth * 2) };
                        // new Uri() already encodes '#' / '?' / spaces correctly;
                        // pre-escaping them double-encoded paths under a folder
                        // like "#Bollywood Movies" and broke the image load.
                        bmp.UriSource = new Uri(fullPath);
                        vm.CoverImage = bmp;
                    }
                    catch { /* unreadable file → tile shows the placeholder bg */ }
                }
            }
            return vm;
        }).ToList();
        GridRepeater.ItemsSource = _tiles;
    }

    private IEnumerable<DatabaseService.CollectionEntry> Filter(IEnumerable<DatabaseService.CollectionEntry> sets) => _filter switch
    {
        "watched"   => sets.Where(s => s.Watched >= s.Count),
        "unwatched" => sets.Where(s => s.Watched < s.Count),
        _           => sets,
    };

    // Name ↑ keeps the database order. The other sorts are stable on top of
    // it, so ties stay alphabetical; a set with no years goes last.
    private IEnumerable<DatabaseService.CollectionEntry> Sort(IEnumerable<DatabaseService.CollectionEntry> sets)
    {
        var parts = _sort.Split(':');
        bool desc = parts.Length > 1 && parts[1] == "desc";
        return parts[0] switch
        {
            "count"      => desc ? sets.OrderByDescending(s => s.Count) : sets.OrderBy(s => s.Count),
            "year"       => desc ? sets.OrderByDescending(s => s.LatestYear ?? 0)
                                 : sets.OrderBy(s => s.LatestYear ?? int.MaxValue),
            "date_added" => desc ? sets.OrderByDescending(s => s.LastAdded) : sets.OrderBy(s => s.LastAdded),
            _            => desc ? sets.Reverse() : sets,
        };
    }

    private void OnSortChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || CollSortCombo.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        _sort = tag;
        AppState.Instance.SetPref("collSort", tag);
        Load();
    }

    private void OnFilterClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        _filter = tag;
        AppState.Instance.SetPref("collFilter", tag);
        SyncToolbar();
        Load();
    }

    private void OnDensityClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }) return;
        ApplyDensity(tag);
        AppState.Instance.SetPref("collDensity", tag);
        Load();   // fresh tiles carry the new card size
    }

    private void ApplyDensity(string tag)
    {
        // v4.4.0 (#18): the show card sizes. The old ones (M was 150 x 280) were
        // narrower than a 2:3 poster, so the posters were cut at the sides.
        (_cardWidth, _cardHeight) = tag switch
        {
            "S"  => (130.0, 235.0),
            "L"  => (210.0, 365.0),
            "XL" => (250.0, 430.0),
            _    => (170.0, 300.0),   // M, the same sizes as All TV shows
        };
        CollGridLayout.MinItemWidth = _cardWidth;
        CollGridLayout.MinItemHeight = _cardHeight;
        CollDensityS.IsChecked  = tag == "S";
        CollDensityM.IsChecked  = tag is not ("S" or "L" or "XL");
        CollDensityL.IsChecked  = tag == "L";
        CollDensityXL.IsChecked = tag == "XL";
        CollDensityLabel.Text = $"Size: {(tag is "S" or "L" or "XL" ? tag : "M")}";
    }

    private void SyncToolbar()
    {
        // Only touch the combo when it's wrong: setting it fires OnSortChanged,
        // which would otherwise store a passing value as the user's choice.
        var index = 0;
        for (int i = 0; i < CollSortCombo.Items.Count; i++)
            if (CollSortCombo.Items[i] is ComboBoxItem { Tag: string t } && t == _sort) { index = i; break; }
        if (CollSortCombo.SelectedIndex != index) CollSortCombo.SelectedIndex = index;
        var pill = (Style)Application.Current.Resources["PillButtonStyle"];
        var active = (Style)Application.Current.Resources["PillButtonActiveStyle"];
        CollFilterAll.Style       = _filter == "all"       ? active : pill;
        CollFilterUnwatched.Style = _filter == "unwatched" ? active : pill;
        CollFilterWatched.Style   = _filter == "watched"   ? active : pill;
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
