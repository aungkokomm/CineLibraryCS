using CineLibraryCS.Models;
using CineLibraryCS.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CineLibraryCS.Views;

/// <summary>
/// 5.4.0: the Wishlist: films you want and don't have yet, the latest added first.
/// The ones in your library since are shown first, to take off.
/// </summary>
public sealed partial class WishlistPage : Page
{
    private List<TmdbFilmItem> _owned = new();
    private List<TmdbFilmItem> _wanted = new();

    public WishlistPage()
    {
        InitializeComponent();
        // A film taken off (here or on a person's page) leaves the page
        Loaded += (_, _) => { Wishlist.Changed -= OnChanged; Wishlist.Changed += OnChanged; };
        Unloaded += (_, _) => Wishlist.Changed -= OnChanged;
    }

    private void OnChanged() => DispatcherQueue.TryEnqueue(Load);

    public void Load()
    {
        var tag = AppState.Instance.GetPref("gridDensity", "M");
        foreach (var repeater in new[] { OwnedRepeater, WantedRepeater })
            Controls.CardCell.FitGrid((UniformGridLayout)repeater.Layout,
                (MovieCardControl.GlobalCardWidth, MovieCardControl.GlobalCardHeight), tag);

        var db = AppState.Instance.Db;
        var owned = new OwnedMovies(db.GetMovieKeys());
        var today = DateTime.Today;
        var items = db.GetWishlist().Select(w =>
        {
            var year = People.YearText(w.Film, today);
            var subtitle = string.IsNullOrEmpty(w.AddedFrom) ? year : $"{year} · {w.AddedFrom}";
            return new TmdbFilmItem(w.Film, subtitle, Wishlist.Poster(w.Film), w.AddedFrom, wished: true, owned: owned.Has(w.Film));
        }).ToList();
        _owned = items.Where(i => i.Owned).ToList();
        _wanted = items.Where(i => !i.Owned).ToList();

        OwnedRepeater.ItemsSource = _owned;
        WantedRepeater.ItemsSource = _wanted;
        OwnedHeader.Text = _owned.Count == 1 ? "NOW IN YOUR LIBRARY" : $"NOW IN YOUR LIBRARY · {_owned.Count}";
        OwnedSection.Visibility = _owned.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        WantedHeader.Visibility = _owned.Count > 0 && _wanted.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = _wanted.Count == 0 ? "" : _wanted.Count == 1 ? "1 film" : $"{_wanted.Count} films";
        CopyBtn.IsEnabled = _wanted.Count > 0;
    }

    private void OnTakeOffOwned(object sender, RoutedEventArgs e)
    {
        var films = _owned.Select(i => (i.Film, i.From)).ToList();
        if (films.Count == 0) return;
        Wishlist.Remove(films.Select(f => f.Film.TmdbId).ToList());
        (App.MainWindow as MainWindow)?.ShowToastWithUndo(
            films.Count == 1 ? $"Took {films[0].Film.Title} off your wishlist" : $"Took {films.Count} films off your wishlist",
            () => { foreach (var (film, from) in films) Wishlist.Add(film, from); });
    }

    private void OnCopyList(object sender, RoutedEventArgs e)
    {
        TmdbFilmCard.CopyText(string.Join(Environment.NewLine, _wanted.Select(i => Wishlist.Line(i.Film))));
        (App.MainWindow as MainWindow)?.ShowToast(_wanted.Count == 1 ? "Copied 1 film" : $"Copied {_wanted.Count} films");
    }
}
