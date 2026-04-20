using CineLibraryCS.Models;
using CineLibraryCS.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;

namespace CineLibraryCS.Views;

public class DecadeStatItem
{
    public int Decade { get; set; }
    public int Count { get; set; }
    public double Percentage { get; set; }
}

public sealed partial class StatisticsPage : Page
{
    private readonly DatabaseService _db;
    private int _totalMovies = 0;

    public ObservableCollection<DecadeStatItem> DecadeStats { get; set; } = new();
    public ObservableCollection<GenreFacet> TopDirectors { get; set; } = new();
    public ObservableCollection<GenreFacet> TopActors { get; set; } = new();

    public StatisticsPage()
    {
        this.InitializeComponent();
        _db = AppState.Instance.Db;
        this.Loaded += OnPageLoaded;
    }

        private async void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            await LoadStatisticsAsync();
        }

        private async Task LoadStatisticsAsync()
        {
            try
            {
                // Get total movies
                _totalMovies = _db.GetAllMovies().Count;
                TotalMoviesText.Text = _totalMovies.ToString();

                // Get runtime
                int totalHours = _db.GetTotalRuntimeHours();
                RuntimeHoursText.Text = totalHours.ToString();
                RuntimeDaysText.Text = (totalHours / 24.0).ToString("F1");
                RuntimeYearsText.Text = (totalHours / (24.0 * 365)).ToString("F2");

                // Get watch progress
                var (watched, total, percentage) = _db.GetWatchProgress();
                WatchProgressText.Text = $"{watched} / {total} watched ({percentage:F1}%)";
                WatchProgressBar.Value = percentage;

                // Get decade stats
                DecadeStats.Clear();
                var decadeData = _db.GetMoviesByDecade();
                if (decadeData.Count > 0)
                {
                    int maxCount = decadeData.Max(d => d.Count);
                    foreach (var (decade, count) in decadeData.OrderByDescending(d => d.Decade))
                    {
                        DecadeStats.Add(new DecadeStatItem
                        {
                            Decade = decade,
                            Count = count,
                            Percentage = (double)count / maxCount * 100
                        });
                    }
                }
                DecadeListView.ItemsSource = DecadeStats;

                // Get top directors
                TopDirectors.Clear();
                foreach (var director in _db.GetTopDirectors(10))
                {
                    TopDirectors.Add(director);
                }
                DirectorsListView.ItemsSource = TopDirectors;

                // Get top actors
                TopActors.Clear();
                foreach (var actor in _db.GetTopActors(10))
                {
                    TopActors.Add(actor);
                }
                ActorsListView.ItemsSource = TopActors;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading statistics: {ex.Message}");
            }
        }

        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            _ = LoadStatisticsAsync();
        }

        private void OnBackToggleClick(object sender, RoutedEventArgs e)
        {
            MainWindow.Current?.RefreshSidebar();
        }
    }
}
