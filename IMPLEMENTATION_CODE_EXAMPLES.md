# CineLibrary - Implementation Code Examples

## Quick-Start Code Snippets for Top Features

---

## 1️⃣ STATISTICS DASHBOARD

### Add to DatabaseService.cs

```csharp
// Get movies by decade
public List<(int decade, int count, double avgRating)> GetMoviesByDecade()
{
    var result = new List<(int, int, double)>();
    using var cmd = _conn.CreateCommand();
    cmd.CommandText = @"
        SELECT 
            (year / 10) * 10 as decade,
            COUNT(*) as count,
            AVG(CAST(rating AS FLOAT)) as avgRating
        FROM movies
        WHERE year IS NOT NULL
        GROUP BY (year / 10) * 10
        ORDER BY decade DESC";

    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        result.Add((
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetDouble(2)
        ));
    }
    return result;
}

// Get top directors
public List<GenreFacet> GetTopDirectors(int limit = 10)
{
    var result = new List<GenreFacet>();
    using var cmd = _conn.CreateCommand();
    cmd.CommandText = @"
        SELECT d.name, COUNT(md.movie_id) as count
        FROM directors d
        LEFT JOIN movie_directors md ON d.id = md.director_id
        GROUP BY d.id, d.name
        ORDER BY count DESC
        LIMIT @limit";
    cmd.Parameters.AddWithValue("@limit", limit);

    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        result.Add(new GenreFacet(
            reader.GetString(0),
            reader.GetInt32(1)
        ));
    }
    return result;
}

// Get top actors
public List<GenreFacet> GetTopActors(int limit = 10)
{
    var result = new List<GenreFacet>();
    using var cmd = _conn.CreateCommand();
    cmd.CommandText = @"
        SELECT actor_name, COUNT(*) as count
        FROM (
            SELECT DISTINCT m.id, a.name as actor_name
            FROM movies m
            JOIN movie_actors ma ON m.id = ma.movie_id
            JOIN actors a ON ma.actor_id = a.id
        )
        GROUP BY actor_name
        ORDER BY count DESC
        LIMIT @limit";
    cmd.Parameters.AddWithValue("@limit", limit);

    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        result.Add(new GenreFacet(
            reader.GetString(0),
            reader.GetInt32(1)
        ));
    }
    return result;
}

// Get watch progress
public (int watched, int total, double percent) GetWatchProgress()
{
    using var cmd = _conn.CreateCommand();
    cmd.CommandText = @"
        SELECT 
            SUM(CASE WHEN is_watched = 1 THEN 1 ELSE 0 END) as watched,
            COUNT(*) as total
        FROM movies
        WHERE is_missing = 0";

    using var reader = cmd.ExecuteReader();
    if (reader.Read())
    {
        int watched = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
        int total = reader.GetInt32(1);
        double percent = total > 0 ? (watched * 100.0 / total) : 0;
        return (watched, total, percent);
    }
    return (0, 0, 0);
}

// Total runtime in hours
public double GetTotalRuntimeHours()
{
    using var cmd = _conn.CreateCommand();
    cmd.CommandText = "SELECT SUM(CAST(runtime AS FLOAT)) FROM movies WHERE runtime IS NOT NULL";

    var result = cmd.ExecuteScalar();
    if (result is not DBNull)
    {
        return (double)result / 60.0; // Convert minutes to hours
    }
    return 0;
}
```

### Create StatisticsPage.xaml

```xaml
<Page
    x:Class="CineLibraryCS.Views.StatisticsPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Background="{ThemeResource BgBrush}">

    <Grid Padding="24">
        <ScrollViewer>
            <StackPanel Spacing="24">

                <!-- Title -->
                <TextBlock Text="📊 Collection Statistics" 
                           FontSize="24" FontWeight="Bold" 
                           Foreground="{ThemeResource TextBrush}"/>

                <!-- Stats Cards -->
                <Grid ColumnSpacing="16">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>

                    <!-- Total Movies -->
                    <Border Grid.Column="0" Background="{ThemeResource InputBgBrush}" 
                            CornerRadius="12" Padding="20">
                        <StackPanel>
                            <TextBlock Text="Total Movies" FontSize="12" 
                                       Foreground="{ThemeResource MutedBrush}"/>
                            <TextBlock x:Name="TotalMoviesText" Text="0" FontSize="28" 
                                       FontWeight="Bold" Foreground="{ThemeResource TextBrush}"/>
                        </StackPanel>
                    </Border>

                    <!-- Total Runtime -->
                    <Border Grid.Column="1" Background="{ThemeResource InputBgBrush}" 
                            CornerRadius="12" Padding="20">
                        <StackPanel>
                            <TextBlock Text="Total Runtime" FontSize="12" 
                                       Foreground="{ThemeResource MutedBrush}"/>
                            <TextBlock x:Name="RuntimeText" Text="0 hrs" FontSize="28" 
                                       FontWeight="Bold" Foreground="{ThemeResource TextBrush}"/>
                        </StackPanel>
                    </Border>

                    <!-- Watched -->
                    <Border Grid.Column="2" Background="{ThemeResource InputBgBrush}" 
                            CornerRadius="12" Padding="20">
                        <StackPanel>
                            <TextBlock Text="Watched" FontSize="12" 
                                       Foreground="{ThemeResource MutedBrush}"/>
                            <TextBlock x:Name="WatchedText" Text="0%" FontSize="28" 
                                       FontWeight="Bold" Foreground="#10b981"/>
                        </StackPanel>
                    </Border>

                    <!-- Avg Rating -->
                    <Border Grid.Column="3" Background="{ThemeResource InputBgBrush}" 
                            CornerRadius="12" Padding="20">
                        <StackPanel>
                            <TextBlock Text="Avg Rating" FontSize="12" 
                                       Foreground="{ThemeResource MutedBrush}"/>
                            <TextBlock x:Name="AvgRatingText" Text="7.2 ⭐" FontSize="28" 
                                       FontWeight="Bold" Foreground="#fbbf24"/>
                        </StackPanel>
                    </Border>
                </Grid>

                <!-- Top Genres -->
                <StackPanel>
                    <TextBlock Text="Top Genres" FontSize="16" FontWeight="Bold" 
                               Foreground="{ThemeResource TextBrush}" Margin="0,0,0,12"/>
                    <ItemsControl x:Name="TopGenresControl">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate x:DataType="models:GenreFacet">
                                <Border Background="{ThemeResource InputBgBrush}" 
                                        CornerRadius="8" Padding="12,8" Margin="0,4">
                                    <Grid>
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="100"/>
                                            <ColumnDefinition Width="*"/>
                                            <ColumnDefinition Width="Auto"/>
                                        </Grid.ColumnDefinitions>
                                        <TextBlock Grid.Column="0" Text="{x:Bind Name}" 
                                                   FontSize="13" Foreground="{ThemeResource TextBrush}"/>
                                        <ProgressBar Grid.Column="1" Margin="12,0" 
                                                     Value="{x:Bind Count}"/>
                                        <TextBlock Grid.Column="2" Text="{x:Bind Count}" 
                                                   FontSize="12" Foreground="{ThemeResource MutedBrush}"/>
                                    </Grid>
                                </Border>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>

            </StackPanel>
        </ScrollViewer>
    </Grid>
</Page>
```

---

## 2️⃣ ACTOR/DIRECTOR QUICK FILTER

### Modify MovieDetailDialog.xaml

```xaml
<!-- Add to cast section -->
<StackPanel Spacing="12">
    <TextBlock Text="CAST" FontSize="14" FontWeight="Bold" 
               Foreground="{ThemeResource TextBrush}"/>
    <ItemsControl x:Name="ActorsControl" ItemsSource="{x:Bind Actors, Mode=OneTime}">
        <ItemsControl.ItemsPanel>
            <ItemsPanelTemplate>
                <WrapGrid Orientation="Horizontal" VerticalSpacing="8" HorizontalSpacing="8"/>
            </ItemsPanelTemplate>
        </ItemsControl.ItemsPanel>
        <ItemsControl.ItemTemplate>
            <DataTemplate x:DataType="models:Actor">
                <Button Click="OnActorClick" 
                        Tag="{x:Bind Name}"
                        Background="{ThemeResource InputBgBrush}"
                        BorderBrush="{ThemeResource InputBorderBrush}"
                        Foreground="{ThemeResource TextBrush}"
                        CornerRadius="8"
                        Padding="12,8"
                        FontSize="12">
                    <TextBlock>
                        <Run Text="{x:Bind Name}"/>
                        <Run Text=" "/>
                        <Run Text="{x:Bind Role}" FontStyle="Italic" Opacity="0.7"/>
                    </TextBlock>
                </Button>
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</StackPanel>
```

### Add to MovieDetailDialog.xaml.cs

```csharp
private void OnActorClick(object sender, RoutedEventArgs e)
{
    if (sender is Button btn && btn.Tag is string actorName)
    {
        // Notify parent page to filter by this actor
        if (Window.Current?.Content is MainWindow mainWin)
        {
            var libraryPage = mainWin.GetCurrentLibraryPage();
            libraryPage?.FilterByActor(actorName);
        }
        Close();
    }
}

private void OnDirectorClick(object sender, RoutedEventArgs e)
{
    if (sender is Button btn && btn.Tag is string directorName)
    {
        if (Window.Current?.Content is MainWindow mainWin)
        {
            var libraryPage = mainWin.GetCurrentLibraryPage();
            libraryPage?.FilterByDirector(directorName);
        }
        Close();
    }
}
```

### Add to LibraryViewModel.cs

```csharp
private string? _filterActor;
private string? _filterDirector;

public string? FilterActor
{
    get => _filterActor;
    set => SetProperty(ref _filterActor, value);
}

public string? FilterDirector
{
    get => _filterDirector;
    set => SetProperty(ref _filterDirector, value);
}

public void FilterByActor(string actorName)
{
    FilterActor = actorName;
    FilterDirector = null;
    Genre = null;
    SearchText = "";
    _ = LoadAsync();
}

public void FilterByDirector(string directorName)
{
    FilterDirector = directorName;
    FilterActor = null;
    Genre = null;
    SearchText = "";
    _ = LoadAsync();
}

// Update BuildOpts to include actor/director filters
private DatabaseService.ListOptions BuildOpts(int offset) => new(
    Search: string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
    SortKey: SortKey.ToString().ToLower() switch { /* ... */ },
    SortDir: SortDir == SortDir.Asc ? "asc" : "desc",
    DriveSerial: DriveSerial,
    Genre: Genre,
    Actor: FilterActor,          // NEW
    Director: FilterDirector,     // NEW
    CollectionId: CollectionId,
    WatchedFilter: WatchedFilter switch { /* ... */ },
    FavoritesOnly: FavoritesOnly,
    Offset: offset,
    Limit: PageSize
);
```

### Add to DatabaseService.cs

```csharp
public record ListOptions(
    string? Search,
    string SortKey,
    string SortDir,
    string? DriveSerial,
    string? Genre,
    string? Actor,              // NEW
    string? Director,           // NEW
    int? CollectionId,
    WatchedFilter WatchedFilter,
    bool FavoritesOnly,
    int Offset,
    int Limit
);

public List<MovieListItem> GetMovies(ListOptions opts, Dictionary<string, string> connected)
{
    var result = new List<MovieListItem>();
    var sb = new StringBuilder(@"
        SELECT m.id, m.title, m.year, m.rating, m.runtime, 
               m.is_favorite, m.is_watched, m.local_poster, 
               m.volume_serial, d.label,
               GROUP_CONCAT(g.name) as genres
        FROM movies m
        LEFT JOIN drives d ON m.volume_serial = d.volume_serial
        LEFT JOIN movie_genres mg ON m.id = mg.movie_id
        LEFT JOIN genres g ON mg.genre_id = g.id
    ");

    // NEW: Actor filter
    if (!string.IsNullOrEmpty(opts.Actor))
    {
        sb.Append(@"
        INNER JOIN movie_actors ma ON m.id = ma.movie_id
        INNER JOIN actors a ON ma.actor_id = a.id
        ");
    }

    // NEW: Director filter
    if (!string.IsNullOrEmpty(opts.Director))
    {
        sb.Append(@"
        INNER JOIN movie_directors md ON m.id = md.movie_id
        INNER JOIN directors d2 ON md.director_id = d2.id
        ");
    }

    sb.Append(" WHERE 1=1 ");

    if (!string.IsNullOrEmpty(opts.Actor))
        sb.Append(" AND LOWER(a.name) = LOWER(@actor) ");

    if (!string.IsNullOrEmpty(opts.Director))
        sb.Append(" AND LOWER(d2.name) = LOWER(@director) ");

    // ... rest of where clauses ...

    using var cmd = _conn.CreateCommand();
    cmd.CommandText = sb.ToString();

    if (!string.IsNullOrEmpty(opts.Actor))
        cmd.Parameters.AddWithValue("@actor", opts.Actor);

    if (!string.IsNullOrEmpty(opts.Director))
        cmd.Parameters.AddWithValue("@director", opts.Director);

    // ... execute and populate results ...

    return result;
}
```

---

## 3️⃣ WATCHLIST FEATURE

### Database Schema Update

```sql
-- Add column (if doesn't exist)
ALTER TABLE movies ADD COLUMN is_watchlist INTEGER DEFAULT 0;

-- Create index for faster filtering
CREATE INDEX idx_watchlist ON movies(is_watchlist) WHERE is_watchlist = 1;
```

### Add to DatabaseService.cs

```csharp
public void SetWatchlist(int movieId, bool isWatchlist)
{
    using var cmd = _conn.CreateCommand();
    cmd.CommandText = "UPDATE movies SET is_watchlist = @val WHERE id = @id";
    cmd.Parameters.AddWithValue("@val", isWatchlist ? 1 : 0);
    cmd.Parameters.AddWithValue("@id", movieId);
    cmd.ExecuteNonQuery();
}

public int GetWatchlistCount()
{
    using var cmd = _conn.CreateCommand();
    cmd.CommandText = "SELECT COUNT(*) FROM movies WHERE is_watchlist = 1 AND is_missing = 0";
    return (int)cmd.ExecuteScalar()!;
}
```

### Add to LibraryViewModel.cs

```csharp
public enum ViewFilter { AllMovies, Watched, Unwatched, Favorites, Watchlist }

[ObservableProperty] private ViewFilter _viewFilter = ViewFilter.AllMovies;

public void ShowWatchlist()
{
    ViewFilter = ViewFilter.Watchlist;
    PageTitle = "📋 To Watch";
    _ = LoadAsync();
}

private DatabaseService.ListOptions BuildOpts(int offset)
{
    var watchedFilter = ViewFilter switch
    {
        ViewFilter.Watched => WatchedFilter.Watched,
        ViewFilter.Unwatched => WatchedFilter.Unwatched,
        _ => WatchedFilter.All,
    };

    var isWatchlistOnly = ViewFilter == ViewFilter.Watchlist;

    return new(
        Search: /* ... */,
        IsWatchlistOnly: isWatchlistOnly,
        // ... rest of params ...
    );
}
```

### Add UI Button to Toolbar

```xaml
<!-- In LibraryPage.xaml toolbar -->
<Button x:Name="WatchlistBtn"
        Click="OnWatchlistClick"
        Background="{ThemeResource InputBgBrush}"
        BorderBrush="{ThemeResource InputBorderBrush}"
        Foreground="{ThemeResource TextBrush}"
        CornerRadius="8"
        Padding="12,8"
        Height="36"
        ToolTipService.ToolTip="To Watch List">
    <StackPanel Orientation="Horizontal" Spacing="8">
        <TextBlock Text="📋"/>
        <TextBlock x:Name="WatchlistBadge" Text="0"/>
    </StackPanel>
</Button>
```

---

## 4️⃣ KEYBOARD SHORTCUTS DIALOG

### Create KeyboardShortcutsDialog.xaml

```xaml
<ContentDialog
    x:Class="CineLibraryCS.Views.KeyboardShortcutsDialog"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="⌨️ Keyboard Shortcuts"
    PrimaryButtonText="Close"
    Background="{ThemeResource BgBrush}">

    <ScrollViewer>
        <StackPanel Spacing="12" Padding="12">

            <TextBlock Text="Browsing" FontSize="14" FontWeight="Bold" 
                       Foreground="{ThemeResource TextBrush}" Margin="0,12,0,0"/>
            <Grid ColumnSpacing="20">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="120"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                <TextBlock Text="Ctrl+F" FontFamily="Consolas" FontWeight="Bold" 
                           Foreground="#a78bfa"/>
                <TextBlock Text="Focus search box" Foreground="{ThemeResource MutedBrush}"/>

                <TextBlock Grid.Row="1" Text="Ctrl+B" FontFamily="Consolas" FontWeight="Bold" 
                           Foreground="#a78bfa"/>
                <TextBlock Grid.Row="1" Text="Toggle sidebar" Foreground="{ThemeResource MutedBrush}"/>

                <TextBlock Grid.Row="2" Text="Esc" FontFamily="Consolas" FontWeight="Bold" 
                           Foreground="#a78bfa"/>
                <TextBlock Grid.Row="2" Text="Clear search" Foreground="{ThemeResource MutedBrush}"/>
            </Grid>

            <TextBlock Text="Movie Actions" FontSize="14" FontWeight="Bold" 
                       Foreground="{ThemeResource TextBrush}" Margin="0,12,0,0"/>
            <Grid ColumnSpacing="20">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="120"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                <TextBlock Text="Enter" FontFamily="Consolas" FontWeight="Bold" 
                           Foreground="#a78bfa"/>
                <TextBlock Text="Play selected movie" Foreground="{ThemeResource MutedBrush}"/>

                <TextBlock Grid.Row="1" Text="Ctrl+H" FontFamily="Consolas" FontWeight="Bold" 
                           Foreground="#a78bfa"/>
                <TextBlock Grid.Row="1" Text="Toggle watched" Foreground="{ThemeResource MutedBrush}"/>

                <TextBlock Grid.Row="2" Text="Ctrl+⭐" FontFamily="Consolas" FontWeight="Bold" 
                           Foreground="#a78bfa"/>
                <TextBlock Grid.Row="2" Text="Toggle favorite" Foreground="{ThemeResource MutedBrush}"/>
            </Grid>

        </StackPanel>
    </ScrollViewer>
</ContentDialog>
```

### Add to MainWindow.xaml.cs

```csharp
private async void OnKeyboardShortcutsClick(object sender, RoutedEventArgs e)
{
    var dialog = new KeyboardShortcutsDialog { XamlRoot = Content.XamlRoot };
    await dialog.ShowAsync();
}
```

---

## 5️⃣ DATABASE INDEXES (Performance)

### Add to DatabaseService.CreateSchema()

```csharp
private void CreateIndexes()
{
    Exec(@"
        CREATE INDEX IF NOT EXISTS idx_movies_title ON movies(title);
        CREATE INDEX IF NOT EXISTS idx_movies_year ON movies(year);
        CREATE INDEX IF NOT EXISTS idx_movies_volume ON movies(volume_serial);
        CREATE INDEX IF NOT EXISTS idx_movie_genres_genre_id ON movie_genres(genre_id);
        CREATE INDEX IF NOT EXISTS idx_movie_genres_movie_id ON movie_genres(movie_id);
        CREATE INDEX IF NOT EXISTS idx_movie_directors_director_id ON movie_directors(director_id);
        CREATE INDEX IF NOT EXISTS idx_watchlist ON movies(is_watchlist);
        CREATE INDEX IF NOT EXISTS idx_favorite ON movies(is_favorite);
        CREATE INDEX IF NOT EXISTS idx_watched ON movies(is_watched);
    ");
}

// Call from CreateSchema():
CreateIndexes();
```

---

## 📝 Implementation Checklist

- [ ] Add database queries (Stats, Actors, Directors)
- [ ] Create StatisticsPage.xaml/cs
- [ ] Add actor/director filter to LibraryViewModel
- [ ] Modify MovieDetailDialog with clickable actors/directors
- [ ] Add watchlist column to database
- [ ] Add watchlist filter UI
- [ ] Create KeyboardShortcutsDialog
- [ ] Add database indexes
- [ ] Test pagination (add visible counter)
- [ ] Test with 1000+ movies
- [ ] Update version to 1.3.0
- [ ] Release to GitHub

---

## 🎯 Priority Implementation Order

1. **Database Indexes** (5 mins) - improves performance immediately
2. **Watchlist Feature** (30 mins) - quick, high-value
3. **Keyboard Shortcuts Dialog** (30 mins) - easy, nice polish
4. **Statistics Dashboard** (2 hours) - visually impressive
5. **Actor/Director Filters** (1 hour) - improves discovery

**Total Time: ~4 hours** → v1.3.0 ready!

---

All code snippets are **ready to copy-paste** with minimal modifications needed.
