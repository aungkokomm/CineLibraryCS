using CineLibraryCS.Models;
using CineLibraryCS.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;

namespace CineLibraryCS.Views
{
    public sealed partial class FilterResultsPage : Page
    {
        private readonly DatabaseService _db;
        public ObservableCollection<MovieListItem> Movies { get; set; } = new();

        private string _filterType = ""; // "actor" or "director"
        private string _filterValue = "";

        public FilterResultsPage()
        {
            this.InitializeComponent();
            _db = AppState.Services.GetRequiredService<DatabaseService>();
        }

        public void SetFilter(string filterType, string filterValue)
        {
            _filterType = filterType;
            _filterValue = filterValue;
            PageTitleText.Text = $"🎬 {filterValue}";
            FilterBadgeText.Text = $"{filterType.ToUpper()}: {filterValue}";
            _ = LoadFilteredMoviesAsync();
        }

        private async Task LoadFilteredMoviesAsync()
        {
            try
            {
                Movies.Clear();
                var options = new ListOptions { PageSize = 100 };

                if (_filterType.Equals("actor", StringComparison.OrdinalIgnoreCase))
                {
                    options.Actor = _filterValue;
                }
                else if (_filterType.Equals("director", StringComparison.OrdinalIgnoreCase))
                {
                    options.Director = _filterValue;
                }

                var movies = _db.GetMovies(options);
                foreach (var movie in movies)
                {
                    Movies.Add(movie);
                }

                MoviesListView.ItemsSource = Movies;
                ResultsCountText.Text = $"{Movies.Count} movie{(Movies.Count != 1 ? "s" : "")}";
                EmptyText.Visibility = Movies.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading filtered movies: {ex.Message}");
            }
        }

        private void OnMovieClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is MovieListItem movie)
            {
                _ = ShowMovieDetailsAsync(movie);
            }
        }

        private async Task ShowMovieDetailsAsync(MovieListItem movie)
        {
            var dialog = new MovieDetailDialog { Movie = new() { Id = movie.Id } };
            await dialog.ShowAsync();
        }

        private void OnClearFilterClick(object sender, RoutedEventArgs e)
        {
            MainWindow.Current?.NavigateToAllMovies();
        }

        private void OnBackToggleClick(object sender, RoutedEventArgs e)
        {
            MainWindow.Current?.RefreshSidebar();
        }
    }
}
