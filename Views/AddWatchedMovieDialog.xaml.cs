using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using CineLibraryCS.Services;
using CineLibraryCS.Services.Tmdb;

namespace CineLibraryCS.Views;

/// <summary>
/// v3.4 — lets the user record a film they watched but never had in the
/// library (no file on disk) straight into Watched &amp; Gone. Searches TMDb,
/// the user picks the match, the poster + details are pulled and stored as a
/// phantom archived record. This is the ONLY place CineLibrary touches the
/// network — and only when the user clicks Search / Add.
/// </summary>
public sealed partial class AddWatchedMovieDialog : ContentDialog
{
    private readonly TmdbClient _client = new();

    /// <summary>Set to the new record's id after a successful Add; null otherwise.</summary>
    public int? AddedMovieId { get; private set; }

    public AddWatchedMovieDialog()
    {
        InitializeComponent();
        WatchedDate.Date = DateTimeOffset.Now;   // default: watched today
    }

    private void OnTitleKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            _ = DoSearchAsync();
        }
    }

    private void OnSearch(object sender, RoutedEventArgs e) => _ = DoSearchAsync();

    private async Task DoSearchAsync()
    {
        var title = (TitleBox.Text ?? "").Trim();
        if (title.Length == 0) { ShowStatus("Type a movie title to search."); return; }

        int? year = int.TryParse((YearBox.Text ?? "").Trim(), out var y) && y > 1800 ? y : null;

        SearchBtn.IsEnabled = false;
        Busy.IsActive = true;
        StatusText.Visibility = Visibility.Collapsed;
        ResultsList.Visibility = Visibility.Collapsed;
        IsPrimaryButtonEnabled = false;

        try
        {
            var results = await _client.SearchMovieAsync(title, year);
            if (results.Count == 0)
            {
                ShowStatus($"No TMDb matches for “{title}”.");
                return;
            }

            var items = new List<TmdbResultItem>();
            foreach (var m in results)   // built on the UI thread → BitmapImage is safe
                items.Add(new TmdbResultItem(m, _client.GetImageUrl(m.PosterPath, "w154")));

            ResultsList.ItemsSource = items;
            ResultsList.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TMDb search failed: {ex.Message}");
            ShowStatus("Couldn't reach TMDb. Check your connection and try again.");
        }
        finally
        {
            Busy.IsActive = false;
            SearchBtn.IsEnabled = true;
        }
    }

    private void OnResultSelected(object sender, SelectionChangedEventArgs e)
        => IsPrimaryButtonEnabled = ResultsList.SelectedItem is TmdbResultItem;

    private async void OnAdd(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (ResultsList.SelectedItem is not TmdbResultItem picked) { args.Cancel = true; return; }

        // Keep the dialog open while we fetch details + poster; cancel the close
        // and surface an error if anything goes wrong.
        var deferral = args.GetDeferral();
        IsPrimaryButtonEnabled = false;
        Busy.IsActive = true;
        ShowStatus("Saving record…");

        try
        {
            // Full details (runtime, genres, studio, country, cert) — fall back to
            // the lighter search result if the details call fails.
            var d = await _client.GetMovieDetailsAsync(picked.Source.TmdbId) ?? picked.Source;

            // Poster → portable data folder, stored as a path relative to it so it
            // resolves exactly like a scanned poster and survives forever.
            string? posterRel = null;
            var posterPath = d.PosterPath ?? picked.Source.PosterPath;
            if (!string.IsNullOrEmpty(posterPath))
            {
                var fileName = $"{d.TmdbId}-{Guid.NewGuid():N}.jpg";
                var rel = "manual_posters/" + fileName;
                var full = System.IO.Path.Combine(AppState.Instance.DataDir,
                    "manual_posters", fileName);
                if (await _client.DownloadImageAsync(_client.GetImageUrl(posterPath, "original"), full))
                    posterRel = rel;
            }

            var watchedAt = (WatchedDate.Date ?? DateTimeOffset.Now).ToUnixTimeSeconds();
            var tags = (TagsBox.Text ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var studio = d.ProductionCompanies.Count > 0 ? d.ProductionCompanies[0].Name : null;
            var country = d.ProductionCountries.Count > 0 ? d.ProductionCountries[0].Name : null;

            var movieId = AppState.Instance.Db.InsertWatchedGoneRecord(
                title: string.IsNullOrWhiteSpace(d.Title) ? picked.Source.Title : d.Title,
                year: d.Year > 0 ? d.Year : (picked.Source.Year > 0 ? picked.Source.Year : null),
                rating: d.Rating > 0 ? d.Rating : null,
                votes: d.VoteCount > 0 ? d.VoteCount : null,
                runtime: d.Runtime > 0 ? d.Runtime : null,
                plot: string.IsNullOrWhiteSpace(d.Overview) ? picked.Source.Overview : d.Overview,
                tagline: string.IsNullOrWhiteSpace(d.Tagline) ? null : d.Tagline,
                mpaa: string.IsNullOrWhiteSpace(d.Certification) ? null : d.Certification,
                imdbId: string.IsNullOrWhiteSpace(d.ImdbId) ? null : d.ImdbId,
                tmdbId: d.TmdbId.ToString(),
                premiered: string.IsNullOrWhiteSpace(d.ReleaseDate) ? null : d.ReleaseDate,
                studio: studio,
                country: country,
                posterRelPath: posterRel,
                note: NoteBox.Text,
                tags: tags,
                watchedAtUnix: watchedAt);

            // Cast — download top-billed profile photos into the portable data
            // folder so the record's faces survive offline, then link them.
            if (d.Cast.Count > 0)
            {
                ShowStatus("Fetching cast photos…");
                var actors = new List<(string, string?, int, string?)>();
                foreach (var c in d.Cast)
                {
                    if (string.IsNullOrWhiteSpace(c.Name)) continue;
                    string? thumbRel = null;
                    if (!string.IsNullOrEmpty(c.ProfilePath))
                    {
                        var fileName = c.ProfilePath!.TrimStart('/');
                        var full = System.IO.Path.Combine(AppState.Instance.DataDir,
                            "manual_actors", fileName);
                        if (await _client.DownloadImageAsync(
                                _client.GetImageUrl(c.ProfilePath, "w185"), full))
                            thumbRel = "manual_actors/" + fileName;
                    }
                    actors.Add((c.Name, string.IsNullOrWhiteSpace(c.Character) ? null : c.Character,
                                c.Order, thumbRel));
                }
                AppState.Instance.Db.AddManualActors(movieId, actors);
            }

            // Genres / directors / writers, so the record's detail view is full.
            var genreNames = new List<string>();
            foreach (var g in d.Genres)
                if (!string.IsNullOrWhiteSpace(g.Name)) genreNames.Add(g.Name);
            AppState.Instance.Db.FillMovieGenreDirectorWriter(
                movieId, genreNames, d.Directors, d.Writers);

            AddedMovieId = movieId;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Add watched movie failed: {ex.Message}");
            AddedMovieId = null;
            args.Cancel = true;
            ShowStatus("Something went wrong saving the record. Please try again.");
            IsPrimaryButtonEnabled = true;
        }
        finally
        {
            Busy.IsActive = false;
            deferral.Complete();
        }
    }

    private void ShowStatus(string text)
    {
        StatusText.Text = text;
        StatusText.Visibility = Visibility.Visible;
    }
}

/// <summary>One row in the TMDb results list.</summary>
public sealed class TmdbResultItem
{
    public TmdbMovie Source { get; }
    public Microsoft.UI.Xaml.Media.ImageSource? Thumb { get; }

    public TmdbResultItem(TmdbMovie source, string thumbUrl)
    {
        Source = source;
        Thumb = string.IsNullOrEmpty(thumbUrl)
            ? null
            : new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(thumbUrl));
    }

    public string DisplayTitle => Source.Year > 0 ? $"{Source.Title} ({Source.Year})" : Source.Title;

    public string SubLine => Source.Rating > 0
        ? $"★ {Source.Rating:0.0}"
        : "Not yet rated";

    public string Overview => Source.Overview;
}
