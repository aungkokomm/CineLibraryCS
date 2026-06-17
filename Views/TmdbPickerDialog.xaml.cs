using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using CineLibraryCS.Services.Tmdb;

namespace CineLibraryCS.Views;

/// <summary>
/// v3.4 — lightweight TMDb match picker used by "Fetch missing info" when a
/// movie has no stored tmdb_id, so the user confirms the right film before any
/// blanks are filled. Reuses <see cref="TmdbResultItem"/>.
/// </summary>
public sealed partial class TmdbPickerDialog : ContentDialog
{
    private readonly TmdbClient _client;

    /// <summary>The film the user chose, or null if cancelled.</summary>
    public TmdbMovie? Picked { get; private set; }

    public TmdbPickerDialog(TmdbClient client, string? initialTitle, int? year)
    {
        InitializeComponent();
        _client = client;
        TitleBox.Text = initialTitle ?? "";
        if (year is int y && y > 0) YearBox.Text = y.ToString();
        Loaded += async (_, _) => { if (!string.IsNullOrWhiteSpace(TitleBox.Text)) await DoSearchAsync(); };
    }

    private void OnTitleKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; _ = DoSearchAsync(); }
    }

    private void OnSearch(object sender, RoutedEventArgs e) => _ = DoSearchAsync();

    private async Task DoSearchAsync()
    {
        var title = (TitleBox.Text ?? "").Trim();
        if (title.Length == 0) return;
        int? year = int.TryParse((YearBox.Text ?? "").Trim(), out var y) && y > 1800 ? y : null;

        SearchBtn.IsEnabled = false;
        Busy.IsActive = true;
        StatusText.Visibility = Visibility.Collapsed;
        IsPrimaryButtonEnabled = false;
        try
        {
            var results = await _client.SearchMovieAsync(title, year);
            if (results.Count == 0)
            {
                StatusText.Text = $"No TMDb matches for “{title}”.";
                StatusText.Visibility = Visibility.Visible;
                ResultsList.ItemsSource = null;
                return;
            }
            var items = new List<TmdbResultItem>();
            foreach (var m in results)
                items.Add(new TmdbResultItem(m, _client.GetImageUrl(m.PosterPath, "w154")));
            ResultsList.ItemsSource = items;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TMDb picker search failed: {ex.Message}");
            StatusText.Text = "Couldn't reach TMDb. Check your connection and try again.";
            StatusText.Visibility = Visibility.Visible;
        }
        finally
        {
            Busy.IsActive = false;
            SearchBtn.IsEnabled = true;
        }
    }

    private void OnResultSelected(object sender, SelectionChangedEventArgs e)
    {
        Picked = (ResultsList.SelectedItem as TmdbResultItem)?.Source;
        IsPrimaryButtonEnabled = Picked != null;
    }
}
