using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CineLibraryCS.Models;
using CineLibraryCS.Services;

namespace CineLibraryCS.Views;

/// <summary>
/// v2.9 — "On This Day" full-page view. Surfaces two categories of
/// matches for today's calendar date:
///   • Movies you watched on this date in past years
///   • Movies released on this date in past years (anniversaries)
///
/// Looks and behaves like the other browse-style pages (back button,
/// title strip, scrollable card grid). The sidebar entry that leads
/// here is itself hidden when there's nothing today, so this page is
/// only reachable when there's content — the empty state is defensive.
/// </summary>
public sealed partial class OnThisDayPage : Page
{
    /// <summary>Fired when the user hits Back. Host wires this to NavigateTo("library").</summary>
    public event EventHandler? BackRequested;

    public OnThisDayPage()
    {
        InitializeComponent();
    }

    /// <summary>(Re)query today's matches and bind the two sections.</summary>
    public void Load()
    {
        // Cards come in the library's Size (S to XL): slots of that size, or a
        // bigger card overflows its slot and loses its corner buttons (#25)
        foreach (var layout in new[] { WatchedRepeater.Layout, ReleasedRepeater.Layout }.OfType<UniformGridLayout>())
            Controls.CardCell.FitGrid(layout, (MovieCardControl.GlobalCardWidth, MovieCardControl.GlobalCardHeight),
                                      MovieCardControl.GlobalDensity);

        var connected = AppState.Instance.Connected;
        var matches = AppState.Instance.Db.GetOnThisDayItems(connected, limit: 48);

        var watched = matches
            .Where(m => m.Reason == DatabaseService.OnThisDayReason.Watched)
            .Select(m => m.Movie).ToList();
        var released = matches
            .Where(m => m.Reason == DatabaseService.OnThisDayReason.Released)
            .Select(m => m.Movie).ToList();

        if (watched.Count > 0)
        {
            WatchedSection.Visibility = Visibility.Visible;
            WatchedSub.Text = watched.Count == 1 ? "1 movie" : $"{watched.Count} movies";
            WatchedRepeater.ItemsSource = watched;
        }
        else
        {
            WatchedSection.Visibility = Visibility.Collapsed;
            WatchedRepeater.ItemsSource = null;
        }

        if (released.Count > 0)
        {
            ReleasedSection.Visibility = Visibility.Visible;
            ReleasedSub.Text = released.Count == 1 ? "1 movie" : $"{released.Count} movies";
            ReleasedRepeater.ItemsSource = released;
        }
        else
        {
            ReleasedSection.Visibility = Visibility.Collapsed;
            ReleasedRepeater.ItemsSource = null;
        }

        // Adaptive sub-line on the header — mirrors the wording I used
        // when this was a dialog, so the moment of arrival reads naturally.
        SubText.Text = (watched.Count, released.Count) switch
        {
            (0, 0)            => DateTime.Now.ToString("MMMM d") + " — nothing in your library tied to this date.",
            (0, 1)            => "One movie released on this date in a past year.",
            (0, var rel)      => $"{rel} movies released on this date in past years.",
            (1, 0)            => "One movie you watched on this date in a past year.",
            (var w, 0)        => $"{w} movies you've watched on this date in years past.",
            (var w, var rel)  => $"{w} you've watched · {rel} released on this date.",
        };

        // 5.4.1: the actors and directors from your films born on this date
        var born = BornToday();
        BornRepeater.ItemsSource = born.Count > 0 ? born : null;
        BornSection.Visibility = born.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BornSub.Text = born.Count == 1 ? "1 person" : $"{born.Count} people";

        if (watched.Count + released.Count == 0 && born.Count > 0)
            SubText.Text = born.Count == 1 ? "Someone from your films was born on this date."
                         : $"{born.Count} people from your films were born on this date.";

        EmptyState.Visibility = (watched.Count + released.Count + born.Count) == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>5.4.1: the people TMDb told us about (opened on their page) born on this date, the oldest first.</summary>
    private static List<BirthdayPerson> BornToday()
    {
        var today = DateTime.Today;
        return People.BornOn(AppState.Instance.Db.GetPeopleBornAround(today), today).Select(p =>
        {
            var photo = People.PhotoPath(p.Info.TmdbId);
            return new BirthdayPerson(p.Name, People.BirthdayLine(p.Info, today),
                File.Exists(photo) ? new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(photo)) { DecodePixelWidth = 224 } : null);
        }).ToList();
    }

    /// <summary>Their page: as an actor, or as a director when they only direct; ‹ On This Day comes back.</summary>
    private void OnBornClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name } || App.MainWindow is not MainWindow mw) return;
        var back = mw.CaptureView();
        if (AppState.Instance.Db.IsOnlyDirector(name)) mw.NavigateLibraryByDirector(name);
        else mw.NavigateLibraryByActor(name);
        mw.SetLibraryBack("On This Day", back);
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
        => BackRequested?.Invoke(this, EventArgs.Empty);
}
