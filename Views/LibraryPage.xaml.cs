using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;
using CineLibraryCS.Models;
using CineLibraryCS.Services;
using CineLibraryCS.ViewModels;
using CineLibraryCS.Views;

namespace CineLibraryCS.Views;

public sealed partial class LibraryPage : Page
{
    private readonly LibraryViewModel _vm;
    public LibraryViewModel ViewModel => _vm;
    public event EventHandler? SidebarRefreshRequested;
    // Raised whenever the search text changes (incl. internal clears) so the
    // global title-bar search box can stay in sync.
    public event EventHandler<string>? SearchTextChanged;

    // ── Multi-select state (v2.5) ─────────────────────────────────────────
    private readonly HashSet<int> _selectedIds = new();
    private MovieListItem? _selectionAnchor;

    // Set to true only after construction finishes. XAML-driven events
    // (ComboBox.SelectionChanged on IsSelected="True", etc.) can fire during
    // InitializeComponent() — we must not touch _vm from those before it's wired up.
    private bool _ready;

    public LibraryPage()
    {
        // _vm MUST be created before InitializeComponent() — the SortCombo's
        // initial SelectionChanged fires inside InitializeComponent and touches _vm.
        _vm = new LibraryViewModel();

        InitializeComponent();

        // Assign ItemsSource once — ObservableCollection handles all future updates
        GridRepeater.ItemsSource = _vm.Movies;
        ListRepeater.ItemsSource = _vm.Movies;

        // Reflect saved prefs in the UI (sort dropdown, grid/list toggle)
        SyncUiFromVm();

        // Load density pref (S/M/L/XL)
        ApplyDensity(AppState.Instance.GetPref("gridDensity", "M"));

        // Keyboard shortcuts (Ctrl+F is handled globally in MainWindow,
        // which focuses the title-bar search box).
        AddAccelerator(VirtualKey.Escape, VirtualKeyModifiers.None, (_, a) =>
        {
            // Esc clears search first, then selection — never both at once.
            if (!string.IsNullOrEmpty(_vm.SearchText))
            {
                _vm.SearchText = "";   // sync event clears the title-bar box
                a.Handled = true;
                return;
            }
            if (_selectedIds.Count > 0)
            {
                ClearSelection();
                a.Handled = true;
            }
        });
        AddAccelerator(VirtualKey.A, VirtualKeyModifiers.Control, (_, a) =>
        {
            if (IsTextEditFocused()) return;
            SelectAllInView();
            a.Handled = true;
        });

        // Navigation shortcuts (v2.0.1) — PgDn/PgUp scroll one viewport,
        // Home/End jump to top/bottom, ↑/↓ scroll by a card-row. All gated
        // on focus: when the search box is editing text, these keys do
        // their default text-cursor thing and we leave them alone.
        AddAccelerator(VirtualKey.PageDown, VirtualKeyModifiers.None,
            (_, a) => { if (TryScrollByViewport(+1)) a.Handled = true; });
        AddAccelerator(VirtualKey.PageUp, VirtualKeyModifiers.None,
            (_, a) => { if (TryScrollByViewport(-1)) a.Handled = true; });
        AddAccelerator(VirtualKey.Home, VirtualKeyModifiers.None,
            (_, a) => { if (TryScrollTo(0)) a.Handled = true; });
        AddAccelerator(VirtualKey.End, VirtualKeyModifiers.None,
            (_, a) => { if (TryScrollToEnd()) a.Handled = true; });
        AddAccelerator(VirtualKey.Down, VirtualKeyModifiers.None,
            (_, a) => { if (TryScrollByRow(+1)) a.Handled = true; });
        AddAccelerator(VirtualKey.Up, VirtualKeyModifiers.None,
            (_, a) => { if (TryScrollByRow(-1)) a.Handled = true; });

        // v2.9 — Card / selection shortcuts. Gated on text-edit focus so
        // typing F or W into the search box doesn't toggle anything.
        //   /        focuses the search box (streaming-style search shortcut)
        //   F        toggle favorite on the current selection
        //   W        toggle watchlist on the current selection
        //   Delete   remove from current list (when viewing a list)
        AddAccelerator((VirtualKey)0xBF /* Oem2 = / */, VirtualKeyModifiers.None, (_, a) =>
        {
            if (IsTextEditFocused()) return;
            (App.MainWindow as CineLibraryCS.MainWindow)?.FocusTitleSearch();
            a.Handled = true;
        });
        AddAccelerator(VirtualKey.F, VirtualKeyModifiers.None, (_, a) =>
        {
            if (IsTextEditFocused() || _selectedIds.Count == 0) return;
            OnSelToggleFav(this, new RoutedEventArgs());
            a.Handled = true;
        });
        AddAccelerator(VirtualKey.W, VirtualKeyModifiers.None, (_, a) =>
        {
            if (IsTextEditFocused() || _selectedIds.Count == 0) return;
            OnSelToggleWatchlist(this, new RoutedEventArgs());
            a.Handled = true;
        });
        AddAccelerator(VirtualKey.Delete, VirtualKeyModifiers.None, (_, a) =>
        {
            if (IsTextEditFocused() || _selectedIds.Count == 0) return;
            if (_vm.UserListId == null) return;  // only meaningful in a list view
            OnSelRemoveFromCurrentList(this, new RoutedEventArgs());
            a.Handled = true;
        });

        _vm.PropertyChanged += OnVmPropertyChanged;
        _vm.Movies.CollectionChanged += (_, _) => UpdateEmptyState();

        // v4.0.0: the page is cached and put back on screen as it is, so
        // re-query the shows row each time; a show may have been made a
        // favorite (or finished) on its own page meanwhile.
        Loaded += (_, _) => { _shownRowKey = ""; RefreshShowsInList(); };
        // v4.1.0: a show card's hover panel changed a show (un-favorited it on
        // Favorites, finished it on Continue Watching): re-list the row.
        TvShowCard.ShowChanged += _ => { _shownRowKey = ""; RefreshShowsInList(); };

        // v2.8.2 — tap a show card in the "TV shows in this list" row to
        // open that show on the TV page.
        ShowsInListRepeater.Tapped += (s, e) => OpenTappedShow(e.OriginalSource);

        // v2.9 — same tap behaviour for the "TV shows matching" search row.
        ShowsInSearchRepeater.Tapped += (s, e) => OpenTappedShow(e.OriginalSource);
        // v4.7.0 (#25): and for the rows that stand in for them in list view
        ShowsInListRows.Tapped += (s, e) => OpenTappedShow(e.OriginalSource);
        ShowsInSearchRows.Tapped += (s, e) => OpenTappedShow(e.OriginalSource);

        // Wire up sidebar refresh from movie cards (watchlist / favorite / watched toggles
        // made inside the detail dialog need to bubble back up so the sidebar counts refresh)
        GridRepeater.ElementPrepared += OnGridRepeaterElementPrepared;
        GridRepeater.ElementClearing += OnGridRepeaterElementClearing;
        ListRepeater.ElementPrepared += OnListRepeaterElementPrepared;
        ListRepeater.ElementClearing += OnListRepeaterElementClearing;

        // Multi-select wiring (v2.5) — cards/rows raise this static event;
        // we manage the actual selection set and visual.
        MovieCardControl.AnyCardSelectionInteraction += OnCardSelectionInteraction;
        MovieCardControl.ResolveSelectionForDrag = ResolveSelectionForDrag;
        // v3.3 — a movie was sent to Watched & Gone (card context menu or
        // selection bar): drop it from the current view + re-count sidebar.
        // Subscribed on Loaded (not just ctor) because this page instance is
        // cached and re-shown — a ctor-only subscription would die for good
        // at the first Unloaded.
        MovieCardControl.AnyMovieArchived += OnAnyMovieArchived;
        // v4.5.0: the grid loads 60 at a time; a movie window opened from it gets
        // the whole view for ← and →.
        Func<System.Collections.IEnumerable, Task<List<int>>?> wholeGrid =
            grid => ReferenceEquals(grid, _vm.Movies) && _vm.HasMore ? _vm.GetAllMatchingIdsAsync() : null;
        MovieDetailDialog.WholeGrid = wholeGrid;
        Loaded += (_, _) =>
        {
            MovieCardControl.AnyMovieArchived -= OnAnyMovieArchived;
            MovieCardControl.AnyMovieArchived += OnAnyMovieArchived;
            MovieDetailDialog.WholeGrid = wholeGrid;
        };
        Unloaded += (_, _) =>
        {
            MovieCardControl.AnyCardSelectionInteraction -= OnCardSelectionInteraction;
            MovieCardControl.AnyMovieArchived -= OnAnyMovieArchived;
            if (MovieCardControl.ResolveSelectionForDrag == (Func<MovieListItem, IEnumerable<int>>)ResolveSelectionForDrag)
                MovieCardControl.ResolveSelectionForDrag = null;
            if (MovieDetailDialog.WholeGrid == wholeGrid) MovieDetailDialog.WholeGrid = null;
        };
        // v2.5.2 — preserve selection across reloads. When the filter or
        // search changes, movies that are still visible should stay
        // selected; movies that scrolled out of the view get pruned
        // from _selectedIds. Each newly-added item has its IsSelected
        // flag rehydrated from _selectedIds so its visual matches.
        _vm.Movies.CollectionChanged += (_, e) =>
        {
            switch (e.Action)
            {
                case System.Collections.Specialized.NotifyCollectionChangedAction.Add:
                    if (e.NewItems != null)
                        foreach (MovieListItem m in e.NewItems)
                            if (_selectedIds.Contains(m.Id) && !m.IsSelected)
                                m.IsSelected = true;
                    break;
                case System.Collections.Specialized.NotifyCollectionChangedAction.Reset:
                    // Movies just cleared — the next batch of Adds will repopulate.
                    // Schedule a prune after they land so _selectedIds drops any
                    // ids that no longer have a card on screen, and the "X
                    // selected" counter stays honest.
                    if (_selectedIds.Count > 0)
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            var visible = new HashSet<int>(_vm.Movies.Select(m => m.Id));
                            var before = _selectedIds.Count;
                            _selectedIds.IntersectWith(visible);
                            if (_selectedIds.Count != before) AfterSelectionChanged();
                        });
                    }
                    break;
            }
            RefreshRemoveFromListVisibility();
        };

        _ready = true;

        _ = _vm.LoadAsync();
    }

    // ── Multi-select handlers (v2.5) ──────────────────────────────────────

    private void OnCardSelectionInteraction(object? sender, MovieCardControl.SelectionInteractionArgs e)
    {
        if (e.Ctrl)
        {
            ToggleSelect(e.Movie);
            _selectionAnchor = e.Movie;
        }
        else if (e.Shift)
        {
            SelectRangeTo(e.Movie);
            _selectionAnchor = e.Movie;
        }
        else
        {
            // Plain tap: if a selection exists, exit selection mode.
            if (_selectedIds.Count > 0) ClearSelection();
            _selectionAnchor = e.Movie;
        }
    }

    private void ToggleSelect(MovieListItem m)
    {
        if (_selectedIds.Add(m.Id)) m.IsSelected = true;
        else { _selectedIds.Remove(m.Id); m.IsSelected = false; }
        AfterSelectionChanged();
    }

    private void SelectRangeTo(MovieListItem target)
    {
        if (_selectionAnchor == null) { ToggleSelect(target); return; }
        int a = _vm.Movies.IndexOf(_selectionAnchor);
        int b = _vm.Movies.IndexOf(target);
        if (a < 0 || b < 0) { ToggleSelect(target); return; }
        if (a > b) (a, b) = (b, a);
        for (int i = a; i <= b; i++)
        {
            var m = _vm.Movies[i];
            if (_selectedIds.Add(m.Id)) m.IsSelected = true;
        }
        AfterSelectionChanged();
    }

    // v4.8.0: every movie in the view, the ones further down too (they load 60
    // at a time as you scroll), so a whole filter can be fetched at once
    private async void SelectAllInView()
    {
        for (int guard = 0; guard < 10_000 && (_vm.HasMore || _vm.IsLoading); guard++)
        {
            if (_vm.IsLoading) { await Task.Delay(50); continue; }
            await _vm.LoadMoreAsync();
        }
        foreach (var m in _vm.Movies)
            if (_selectedIds.Add(m.Id)) m.IsSelected = true;
        AfterSelectionChanged();
    }

    private void ClearSelection()
    {
        foreach (var m in _vm.Movies)
            if (m.IsSelected) m.IsSelected = false;
        _selectedIds.Clear();
        AfterSelectionChanged();
    }

    private bool _selectionBarShown;

    private void AfterSelectionChanged()
    {
        MovieCardControl.CurrentSelectionCount = _selectedIds.Count;
        if (_selectedIds.Count == 0)
        {
            SelectionBar.Visibility = Visibility.Collapsed;
            _selectionBarShown = false;
            return;
        }
        SelectionCountText.Text = $"{_selectedIds.Count} selected";
        // v2.6 — animate the bar up the first time it appears in this
        // selection session; subsequent count changes just update the text.
        if (!_selectionBarShown)
        {
            SelectionBar.Visibility = Visibility.Visible;
            AnimateSelectionBarIn();
            _selectionBarShown = true;
        }
        RefreshRemoveFromListVisibility();
    }

    private void AnimateSelectionBarIn()
    {
        SelectionBarSlide.Y = 40;
        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        var anim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = 40, To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(220)),
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase
                { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut },
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(anim, SelectionBarSlide);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(anim, "Y");
        sb.Children.Add(anim);
        sb.Begin();
    }

    private void RefreshRemoveFromListVisibility()
    {
        SelRemoveFromListBtn.Visibility = (_vm.UserListId != null && _selectedIds.Count > 0)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// MovieCardControl asks for the id-set that a drag should carry. If
    /// the dragged card isn't in the current selection, treat the drag as
    /// single-card AND clear/replace the selection so what the user sees
    /// matches what they're dragging.
    /// </summary>
    private IEnumerable<int> ResolveSelectionForDrag(MovieListItem dragged)
    {
        if (_selectedIds.Contains(dragged.Id) && _selectedIds.Count > 1)
            return _selectedIds.ToList();
        // Drag of a single (possibly unselected) card.
        return new[] { dragged.Id };
    }

    /// <summary>
    /// Snapshot of selected MovieListItems for batch ops. Order follows
    /// the current Movies list so operations feel predictable.
    /// </summary>
    private List<MovieListItem> SelectedMovies() =>
        _vm.Movies.Where(m => _selectedIds.Contains(m.Id)).ToList();

    private void OnSelClear(object sender, RoutedEventArgs e) => ClearSelection();

    private void OnSelListsFlyoutOpening(object sender, object e)
    {
        SelListsFlyout.Items.Clear();
        var ids = SelectedMovies().Select(m => m.Id).ToList();
        if (ids.Count == 0) return;

        var lists = AppState.Instance.Db.GetUserLists();
        if (lists.Count == 0)
        {
            SelListsFlyout.Items.Add(new MenuFlyoutItem
            {
                Text = "(no lists yet — use + below)", IsEnabled = false
            });
        }
        foreach (var ul in lists)
        {
            var item = new MenuFlyoutItem { Text = $"📑 {ul.Name}" };
            var capturedUl = ul;
            item.Click += (_, _) => AddSelectedToList(capturedUl.Id, capturedUl.Name);
            SelListsFlyout.Items.Add(item);
        }
        SelListsFlyout.Items.Add(new MenuFlyoutSeparator());
        var newItem = new MenuFlyoutItem { Text = "+ New list…" };
        newItem.Click += async (_, _) =>
        {
            var name = await PromptNewListName();
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                var listId = AppState.Instance.Db.CreateUserList(name.Trim());
                AddSelectedToList(listId, name.Trim());
            }
            catch (Microsoft.Data.Sqlite.SqliteException)
            {
                if (App.MainWindow is MainWindow mw)
                    mw.ShowToast($"A list named “{name.Trim()}” already exists");
            }
        };
        SelListsFlyout.Items.Add(newItem);
    }

    private void AddSelectedToList(int listId, string listName)
    {
        var ids = SelectedMovies().Select(m => m.Id).ToList();
        if (ids.Count == 0) return;
        foreach (var mid in ids)
            AppState.Instance.Db.AddMovieToUserList(listId, mid);
        SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
        ShowSelectionToast($"Added {ids.Count} to “{listName}”", () =>
        {
            foreach (var mid in ids)
                AppState.Instance.Db.RemoveMovieFromUserList(listId, mid);
            SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    private void OnSelToggleWatched(object sender, RoutedEventArgs e)
    {
        var picked = SelectedMovies();
        if (picked.Count == 0) return;
        bool anyUnwatched = picked.Any(m => !m.IsWatched);
        var changed = new List<MovieListItem>();
        foreach (var m in picked)
        {
            if (m.IsWatched == anyUnwatched) continue;
            AppState.Instance.Db.ToggleWatched(m.Id);
            m.IsWatched = anyUnwatched;
            changed.Add(m);
        }
        SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
        ShowSelectionToast(
            $"{(anyUnwatched ? "Marked" : "Unmarked")} {changed.Count} watched",
            () => {
                foreach (var m in changed)
                {
                    AppState.Instance.Db.ToggleWatched(m.Id);
                    m.IsWatched = !anyUnwatched;
                }
                SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
            });
    }

    private void OnSelToggleWatchlist(object sender, RoutedEventArgs e)
    {
        var picked = SelectedMovies();
        if (picked.Count == 0) return;
        bool anyOff = picked.Any(m => !m.IsWatchlist);
        var changed = new List<MovieListItem>();
        foreach (var m in picked)
        {
            if (m.IsWatchlist == anyOff) continue;
            AppState.Instance.Db.SetWatchlist(m.Id, anyOff);
            m.IsWatchlist = anyOff;
            changed.Add(m);
        }
        SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
        ShowSelectionToast(
            $"{(anyOff ? "Added" : "Removed")} {changed.Count} {(anyOff ? "to" : "from")} watchlist",
            () => {
                foreach (var m in changed)
                {
                    AppState.Instance.Db.SetWatchlist(m.Id, !anyOff);
                    m.IsWatchlist = !anyOff;
                }
                SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
            });
    }

    private void OnAnyMovieArchived()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _ = _vm.LoadAsync();
            SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    // v3.3 — selection bar: send every selected movie to Watched & Gone.
    private async void OnSelArchive(object sender, RoutedEventArgs e)
    {
        var picked = SelectedMovies();
        if (picked.Count == 0) return;
        var dlg = new ContentDialog
        {
            Title = $"Send {picked.Count} movie(s) to Watched & Gone?",
            Content = "They move out of your library into Watched & Gone — posters, details, " +
                      "your notes and watch history are all kept as records. " +
                      "The files on your drives are not touched.",
            PrimaryButtonText = "Send",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = CineLibraryCS.MainWindow.CurrentTheme,
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

        var n = AppState.Instance.Db.ArchiveMovies(picked.Select(m => m.Id).ToList());
        ClearSelection();
        if (App.MainWindow is CineLibraryCS.MainWindow mw)
            mw.ShowToast($"Sent {n} movie(s) to Watched & Gone");
        MovieCardControl.RaiseMovieArchived();
    }

    // v4.8.0 — selection bar: Fetch missing info from TMDB for every selected
    // movie, one after another, with a sure match only (MovieFetch.SureMatchAsync)
    private async void OnSelFetchMissing(object sender, RoutedEventArgs e)
    {
        var picked = SelectedMovies();
        if (picked.Count == 0) return;
        var ask = new ContentDialog
        {
            Title = $"Fetch missing info for {picked.Count} movie(s)?",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "CineLibrary looks each one up on TMDb and fills in only what's missing, in your library " +
                       "and, with Save fetched info to your drives on, in the movie's .nfo when its drive is connected. " +
                       "A movie it can't match for sure is left out: one with no TMDb or IMDb id, and not exactly one " +
                       "film of its title and year on TMDb. Open those one by one to pick the match.",
            },
            PrimaryButtonText = "Fetch",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = CineLibraryCS.MainWindow.CurrentTheme,
        };
        if (await ask.ShowAsync() != ContentDialogResult.Primary) return;

        var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var bar = new ProgressBar { Maximum = picked.Count, Margin = new Thickness(0, 12, 0, 0) };
        var cts = new CancellationTokenSource();
        bool finished = false;
        var run = new ContentDialog
        {
            Title = "Fetching missing info",
            Content = new StackPanel { Children = { text, bar } },
            CloseButtonText = "Stop",
            XamlRoot = XamlRoot,
            RequestedTheme = CineLibraryCS.MainWindow.CurrentTheme,
        };
        run.CloseButtonClick += (_, _) => { if (!finished) cts.Cancel(); };
        var shown = run.ShowAsync();
        var progress = new Progress<(int Done, string Title)>(p =>
        {
            bar.Value = p.Done;
            if (p.Title.Length > 0) text.Text = $"{p.Done + 1} of {picked.Count}: {p.Title}";
        });
        var ids = picked.Select(m => m.Id).ToList();
        Services.MovieFetch.BulkResult r;
        try { r = await Task.Run(() => Services.MovieFetch.FetchManyAsync(ids, progress, cts.Token)); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Bulk fetch failed: {ex.Message}");
            r = new(0, 0, 0, new List<string>(), ids.Count, Stopped: false);
        }
        finished = true;

        ClearSelection();
        _ = _vm.LoadAsync();   // the cards show the new posters and details
        SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);

        var lines = new List<string> { $"Updated {r.Updated} of {ids.Count}." };
        if (r.Saved > 0) lines.Add($"{r.Saved} saved to their folders, .nfo included.");
        if (r.Waiting > 0) lines.Add($"{r.Waiting} go onto their drives when you connect them.");
        if (r.NoMatch.Count > 0)
            lines.Add($"No sure match on TMDb for {r.NoMatch.Count}; open each one to pick the match: " +
                      string.Join(", ", r.NoMatch.Take(12)) + (r.NoMatch.Count > 12 ? $" and {r.NoMatch.Count - 12} more." : "."));
        if (r.Failed > 0) lines.Add($"{r.Failed} couldn't be fetched (TMDb didn't answer). Try them again later.");
        if (cts.IsCancellationRequested)
        {
            // the dialog closed with Stop
            if (App.MainWindow is CineLibraryCS.MainWindow mw) mw.ShowToast($"Stopped. Updated {r.Updated} of {ids.Count}.");
            return;
        }
        run.Title = "Fetch finished";
        text.Text = string.Join("\n\n", lines);
        bar.Visibility = Visibility.Collapsed;
        run.CloseButtonText = "Close";
        await shown;
    }

    private void OnSelToggleFav(object sender, RoutedEventArgs e)
    {
        var picked = SelectedMovies();
        if (picked.Count == 0) return;
        bool anyOff = picked.Any(m => !m.IsFavorite);
        var changed = new List<MovieListItem>();
        foreach (var m in picked)
        {
            if (m.IsFavorite == anyOff) continue;
            AppState.Instance.Db.ToggleFavorite(m.Id);
            m.IsFavorite = anyOff;
            changed.Add(m);
        }
        SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
        ShowSelectionToast(
            $"{(anyOff ? "Favorited" : "Unfavorited")} {changed.Count}",
            () => {
                foreach (var m in changed)
                {
                    AppState.Instance.Db.ToggleFavorite(m.Id);
                    m.IsFavorite = !anyOff;
                }
                SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
            });
    }

    private void OnSelRemoveFromCurrentList(object sender, RoutedEventArgs e)
    {
        if (_vm.UserListId == null) return;
        var listId = _vm.UserListId.Value;
        var picked = SelectedMovies();
        if (picked.Count == 0) return;
        var ids = picked.Select(m => m.Id).ToList();
        // Snapshot of (movie, original index) so Undo can put each item
        // back exactly where the user removed it from. Reloading the page
        // would lose anything past page 1 of the paged list.
        var snapshots = picked
            .Select(m => (Movie: m, Index: _vm.Movies.IndexOf(m)))
            .OrderBy(s => s.Index)
            .ToList();
        foreach (var mid in ids)
            AppState.Instance.Db.RemoveMovieFromUserList(listId, mid);
        // Visually remove from the current view (we're viewing this list).
        foreach (var m in picked) _vm.Movies.Remove(m);
        _selectedIds.Clear();
        AfterSelectionChanged();
        SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
        ShowSelectionToast($"Removed {ids.Count} from list", () =>
        {
            foreach (var mid in ids)
                AppState.Instance.Db.AddMovieToUserList(listId, mid);
            // Re-insert in place. Iterate from the lowest-index outwards
            // so later items don't have to be re-indexed as we go.
            foreach (var snap in snapshots)
            {
                var idx = Math.Min(snap.Index, _vm.Movies.Count);
                _vm.Movies.Insert(idx, snap.Movie);
            }
            SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    private async Task<string?> PromptNewListName()
    {
        var box = new TextBox { PlaceholderText = "List name" };
        var dlg = new ContentDialog
        {
            Title = "New list",
            Content = box,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
            RequestedTheme = MainWindow.CurrentTheme,
        };
        var result = await dlg.ShowAsync();
        return result == ContentDialogResult.Primary ? box.Text : null;
    }

    private void ShowSelectionToast(string message, Action undo)
    {
        // Route through MainWindow's toast — keeps a single visual style.
        if (App.MainWindow is MainWindow mw)
            mw.ShowToastWithUndo(message, undo);
    }

    // Named handlers so we can unsubscribe on ElementClearing — anonymous
    // lambdas would accumulate every time ItemsRepeater recycles a card,
    // and a click would fire the handler N times → even count = no net
    // change → "Mark Watched looks like it stops working" bug.
    private void OnCardSidebarRefresh(object? s, EventArgs e)
        => SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
    private void OnCardWatchedToggle(object? s, MovieListItem movie)
        => _vm.ToggleWatched(movie);
    private void OnCardWatchlistToggle(object? s, MovieListItem movie)
    {
        _vm.ToggleWatchlistOnCard(movie);
        SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
    }
    private void OnRowSidebarRefresh(object? s, EventArgs e)
        => SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);

    private void OnGridRepeaterElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is MovieCardControl card)
        {
            // Defensive unsubscribe — if a card was prepared without a paired
            // clearing (rare, but keeps the count honest).
            card.SidebarRefreshRequested -= OnCardSidebarRefresh;
            card.WatchedToggleRequested  -= OnCardWatchedToggle;
            card.WatchlistToggleRequested -= OnCardWatchlistToggle;

            card.SidebarRefreshRequested += OnCardSidebarRefresh;
            card.WatchedToggleRequested  += OnCardWatchedToggle;
            card.WatchlistToggleRequested += OnCardWatchlistToggle;
        }
    }

    private void OnGridRepeaterElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is MovieCardControl card)
        {
            card.SidebarRefreshRequested -= OnCardSidebarRefresh;
            card.WatchedToggleRequested  -= OnCardWatchedToggle;
            card.WatchlistToggleRequested -= OnCardWatchlistToggle;
        }
    }

    private void OnListRepeaterElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is MovieRowControl row)
        {
            row.SidebarRefreshRequested -= OnRowSidebarRefresh;
            row.WatchedToggleRequested  -= OnCardWatchedToggle;
            row.WatchlistToggleRequested -= OnCardWatchlistToggle;
            row.SidebarRefreshRequested += OnRowSidebarRefresh;
            row.WatchedToggleRequested  += OnCardWatchedToggle;   // v4.7.0 (#25): as a card's
            row.WatchlistToggleRequested += OnCardWatchlistToggle;
        }
    }

    private void OnListRepeaterElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is MovieRowControl row)
        {
            row.SidebarRefreshRequested -= OnRowSidebarRefresh;
            row.WatchedToggleRequested  -= OnCardWatchedToggle;
            row.WatchlistToggleRequested -= OnCardWatchlistToggle;
        }
    }

    private void AddAccelerator(VirtualKey key, VirtualKeyModifiers mods,
        Windows.Foundation.TypedEventHandler<KeyboardAccelerator, KeyboardAcceleratorInvokedEventArgs> handler)
    {
        var acc = new KeyboardAccelerator { Key = key, Modifiers = mods };
        acc.Invoked += handler;
        KeyboardAccelerators.Add(acc);
    }

    public void FocusSearchBox()
    {
        (App.MainWindow as CineLibraryCS.MainWindow)?.FocusTitleSearch();
    }

    // ── Keyboard navigation (v2.0.1) ──────────────────────────────────────
    // All return true when they handled the keypress so the accelerator
    // can swallow it; false when focus is in a text input (let the user
    // navigate the text cursor instead) or scroll can't happen.

    /// <summary>
    /// True when the focused element is a text-editing surface (search box,
    /// notes editor, etc.). In that case we don't steal PgDn / arrow keys.
    /// </summary>
    private bool IsTextEditFocused()
    {
        var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot);
        return focused is TextBox or AutoSuggestBox || (
            focused is FrameworkElement fe &&
            (fe.Parent is AutoSuggestBox || fe.Parent is TextBox));
    }

    private bool TryScrollByViewport(int direction)
    {
        if (IsTextEditFocused()) return false;
        var target = MainScroller.VerticalOffset + direction * MainScroller.ViewportHeight;
        MainScroller.ChangeView(null, target, null);
        return true;
    }

    /// <summary>
    /// Scroll by roughly one row of cards/list-rows. Grid view: a row is
    /// the current card height + spacing. List view: a fixed ~44 px row.
    /// </summary>
    private bool TryScrollByRow(int direction)
    {
        if (IsTextEditFocused()) return false;
        // v4.8.0: on a card, ↑ / ↓ move to the card above or below instead
        if (Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) is MovieCardControl or TvShowCard) return false;
        double rowPx = _vm.ViewMode == ViewMode.Grid
            ? MovieCardControl.GlobalCardHeight + GridLayout.MinRowSpacing
            : 44;
        MainScroller.ChangeView(null, MainScroller.VerticalOffset + direction * rowPx, null);
        return true;
    }

    private bool TryScrollTo(double offset)
    {
        if (IsTextEditFocused()) return false;
        MainScroller.ChangeView(null, offset, null);
        return true;
    }

    /// <summary>
    /// End-of-list jump. Library uses 60-per-page lazy loading so we
    /// first drain remaining pages until everything's loaded, then scroll
    /// to the bottom. Small libraries finish instantly.
    /// </summary>
    private bool TryScrollToEnd()
    {
        if (IsTextEditFocused()) return false;
        _ = ScrollToEndAsync();
        return true;
    }

    private async Task ScrollToEndAsync()
    {
        // Drain remaining pages first so ScrollableHeight reflects the real end.
        int guard = 0;
        while (_vm.HasMore && !_vm.IsLoading && guard < 200)
        {
            await _vm.LoadMoreAsync();
            guard++;
        }
        // Layout pass needed before ScrollableHeight is final
        MainScroller.UpdateLayout();
        MainScroller.ChangeView(null, MainScroller.ScrollableHeight, null);
    }

    public void UpdatePageTitle(string title) => PageTitleText.Text = title;

    private void UpdateEmptyState()
    {
        // v4.0.0: a page whose only items are TV shows (the row above) isn't empty.
        var empty = _vm.Movies.Count == 0 && !_vm.IsLoading
                    && ShowsInListSection.Visibility != Visibility.Visible;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        GridBorder.Opacity = empty ? 0 : 1;
        ListBorder.Opacity = empty ? 0 : 1;

        if (!empty) return;

        // First-launch: zero drives in DB → big CTA pointing to Drives → Add folder.
        // Empty user list → list-specific message (v2.5.2).
        // Otherwise: filter-empty hint (existing behaviour).
        var hasAnyDrive = AppState.Instance.Db.GetDrives().Count > 0;
        if (!hasAnyDrive)
        {
            EmptyTitle.Text = "Welcome to CineLibrary";
            EmptyStateHint.Text =
                "Point CineLibrary at the folder where MediaElch saved your scraped " +
                "movies (each movie in its own folder with a .nfo + poster). " +
                "Drives → Add folder.";
            EmptyCtaBtn.Content = "📂 Add my movies folder";
            EmptyCtaBtn.Visibility = Visibility.Visible;
        }
        else if (_vm.UserListId != null && string.IsNullOrEmpty(_vm.SearchText))
        {
            EmptyTitle.Text = "This list is empty";
            EmptyStateHint.Text =
                "Add movies by right-clicking any poster and choosing " +
                "“📑 Add to list”, or from a movie's detail dialog. " +
                "You can also Ctrl+click multiple cards in the library and " +
                "drag them onto this list in the sidebar.";
            EmptyCtaBtn.Visibility = Visibility.Collapsed;
        }
        else
        {
            EmptyTitle.Text = "No movies match your filters";
            EmptyStateHint.Text = "Try clearing the search or choosing 'All' watched filter.";
            EmptyCtaBtn.Visibility = Visibility.Collapsed;
        }
    }

    private void OnEmptyCtaClick(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is MainWindow mw)
            mw.NavigateToDrivesAndAdd();
    }

    private void OnClearFilters(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        _vm.SearchText = "";
        // Reset watched-pill to All
        var pill = (Style)Application.Current.Resources["PillButtonStyle"];
        var pillActive = (Style)Application.Current.Resources["PillButtonActiveStyle"];
        FilterAll.Style = pillActive;
        FilterUnwatched.Style = pill;
        FilterWatched.Style = pill;
        _vm.WatchedFilter = WatchedFilter.All;
        _vm.ClearFilters();
        PageTitleText.Text = "All movies";
        UpdateClearFiltersButton();
    }

    private void UpdateClearFiltersButton()
    {
        // v2.6 — covers the new Decade / Rating / ContinueWatching filters too.
        ClearFiltersBtn.Visibility = AnyFilterActive()
            ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Density (S/M/L/XL) ────────────────────────────────────────────────

    private void OnDensityClick(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (sender is not FrameworkElement { Tag: string tag }) return;
        ApplyDensity(tag);
        AppState.Instance.SetPref("gridDensity", tag);
        // Fresh items, so the show cards in the rows take the new size (v4.5.1).
        _shownRowKey = "";
        RefreshShowsInList();
        _lastShowsSearchTerm = null;
        RefreshShowsInSearch();
    }

    private void ApplyDensity(string tag)
    {
        // v4.8.0: the sizes live with the card (a 2:3 poster and its caption)
        MovieCardControl.SetGlobalDensity(tag);
        TvShowCard.SetLibraryDensity(tag);
        Controls.CardCell.FitGrid(GridLayout, (MovieCardControl.GlobalCardWidth, MovieCardControl.GlobalCardHeight), tag);
        // the show rows above the grid: spaced like it, and as far from it as its rows
        ((StackLayout)ShowsInSearchRepeater.Layout).Spacing = ((StackLayout)ShowsInListRepeater.Layout).Spacing = Controls.CardCell.GapFor(tag);
        ShowsInSearchSection.Margin = ShowsInListSection.Margin = new Thickness(0, 0, 0, GridLayout.MinRowSpacing);

        DensityS.IsChecked  = tag == "S";
        DensityM.IsChecked  = tag == "M";
        DensityL.IsChecked  = tag == "L";
        DensityXL.IsChecked = tag == "XL";
        DensityLabel.Text = $"Size: {(tag is "S" or "L" or "XL" ? tag : "M")}";
    }

    private void SyncUiFromVm()
    {
        SyncSortCombo();

        // View mode toggle
        if (_vm.ViewMode == ViewMode.List)
        {
            GridViewToggle.IsChecked = false;
            ListViewToggle.IsChecked = true;
            GridBorder.Visibility = Visibility.Collapsed;
            ListBorder.Visibility = Visibility.Visible;
        }
        else
        {
            GridViewToggle.IsChecked = true;
            ListViewToggle.IsChecked = false;
            GridBorder.Visibility = Visibility.Visible;
            ListBorder.Visibility = Visibility.Collapsed;
        }
        ApplyShowsLayout(_vm.ViewMode == ViewMode.List);
    }

    /// <summary>v4.7.0 (#25): the shows on a page with movies come as rows in list view, cards otherwise.</summary>
    private void ApplyShowsLayout(bool list)
    {
        ShowsInListCards.Visibility = ShowsInSearchCards.Visibility = list ? Visibility.Collapsed : Visibility.Visible;
        ShowsInListRows.Visibility = ShowsInSearchRows.Visibility = list ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void OpenTappedShow(object originalSource)
    {
        var d = originalSource as DependencyObject;
        while (d != null && d is not TvShowCard && d is not TvShowRowControl)
            d = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(d);
        var show = d is TvShowCard card ? card.Show : (d as TvShowRowControl)?.Show;
        if (show != null && App.MainWindow is MainWindow mw)
            mw.OpenTvShow(show.Id);
    }

    // Sort combo — match the item whose Tag corresponds to current SortKey+SortDir.
    // v4.1.0: runs whenever the sort changes (it ran only at start, so Continue
    // Watching's own order never showed), and knows Last Watched (it read "Title").
    private void SyncSortCombo()
    {
        var keyStr = _vm.SortKey switch
        {
            SortKey.Year       => "year",
            SortKey.Rating     => "rating",
            SortKey.Runtime    => "runtime",
            SortKey.DateAdded  => "date_added",
            SortKey.LastPlayed => "last_played",
            _                  => "title"
        };
        var dirStr = _vm.SortDir == SortDir.Asc ? "asc" : "desc";
        var wantTag = $"{keyStr}:{dirStr}";
        for (int i = 0; i < SortCombo.Items.Count; i++)
        {
            if (SortCombo.Items[i] is ComboBoxItem ci && (ci.Tag as string) == wantTag)
            {
                SortCombo.SelectedIndex = i;
                break;
            }
        }
        if (SortCombo.SelectedIndex < 0) SortCombo.SelectedIndex = 0;
    }

    public void ApplyNavParam(LibraryNavParam p)
    {
        SearchBox.Text = "";
        _vm.SearchText = "";
        _shownRowKey = "";   // re-query the shows row: favorites may have changed elsewhere

        if (p.FavoritesOnly)
            _vm.SetFavorites();
        else if (p.DriveSerial != null)
            _vm.SetDriveFilter(p.DriveSerial, p.Label);
        else if (p.Genre != null)
            _vm.SetGenreFilter(p.Genre);
        else if (p.CollectionId != null)
            _vm.SetCollectionFilter(p.CollectionId, p.Label);
        else
            _vm.ClearFilters();

        // Breadcrumb-style title: "All movies › Drama". v4.8.0 (#25): a genre's page
        // holds its TV shows too, so just the genre, as Favorites is just "Favorites"
        PageTitleText.Text = p.Label == null ? "All movies"
            : p.Genre != null ? p.Label
            : $"All movies › {p.Label}";
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (e.PropertyName == nameof(LibraryViewModel.TotalCount) ||
                e.PropertyName == nameof(LibraryViewModel.HasMore) ||
                e.PropertyName == nameof(LibraryViewModel.FilterTotal))
            {
                // v3.9.0: how many movies the view holds. (It used to read
                // "60 of 1,200" while pages were still loading, which looked
                // like a filter.)
                UpdateCountText();
            }
            if (e.PropertyName == nameof(LibraryViewModel.IsLoading))
            {
                LoadingRing.IsActive = _vm.IsLoading;
                LoadingRing.Visibility = _vm.IsLoading ? Visibility.Visible : Visibility.Collapsed;
                UpdateEmptyState();
            }
            if (e.PropertyName == nameof(LibraryViewModel.SearchText))
                SearchTextChanged?.Invoke(this, _vm.SearchText ?? "");
            if (e.PropertyName is nameof(LibraryViewModel.SortKey) or nameof(LibraryViewModel.SortDir))
                SyncSortCombo();
            // Any filter-related VM change should reflect in the Clear button
            UpdateClearFiltersButton();
            UpdateFilterChips();
            RefreshShowsInList();
            RefreshShowsInSearch();
            // v2.9 — Recently Added / Recently Watched / On This Day are
            // accessed via sidebar entries instead of home rows now; the
            // horizontal rows interfered with vertical scrolling through
            // the main grid.
        });
    }

    // v2.9 — Recently Added / Recently Watched / On This Day rows used to
    // live at the top of the Library home, but a horizontal scroll row
    // inside a vertical ScrollViewer steals the mouse wheel and makes the
    // main grid feel unscrollable. They're now sidebar entries only.

    // ── v2.9 TV-in-search row ────────────────────────────────────────────────

    private string? _lastShowsSearchTerm;

    /// <summary>
    /// When the search box has a term, also surface matching TV shows above
    /// the movie grid. Re-queried only when the search term actually changes.
    /// </summary>
    private void RefreshShowsInSearch()
    {
        var q = (_vm.SearchText ?? "").Trim();
        if (q.Length < 2)
        {
            ShowsInSearchSection.Visibility = Visibility.Collapsed;
            ShowsInSearchRepeater.ItemsSource = ShowsInSearchRows.ItemsSource = null;
            _lastShowsSearchTerm = null;
            return;
        }
        if (q == _lastShowsSearchTerm) return;
        _lastShowsSearchTerm = q;

        var shows = AppState.Instance.Db.SearchTvShows(q, AppState.Instance.Connected, limit: 24);
        if (shows.Count == 0)
        {
            ShowsInSearchSection.Visibility = Visibility.Collapsed;
            ShowsInSearchRepeater.ItemsSource = ShowsInSearchRows.ItemsSource = null;
            return;
        }
        ShowsInSearchHeader.Text = shows.Count == 1
            ? "TV SHOW MATCHING"
            : $"TV SHOWS MATCHING ({shows.Count})";
        ShowsInSearchRepeater.ItemsSource = ShowsInSearchRows.ItemsSource = shows;
        ShowsInSearchSection.Visibility = Visibility.Visible;
    }

    // ── v2.9 Surprise Me 🎲 ──────────────────────────────────────────────────

    /// <summary>
    /// Filter-aware random pick. Uses the current LibraryViewModel filters so
    /// the dice are rolled across whatever the user is looking at — random
    /// 90s comedy, random movie in this list, random favorite, etc. Falls
    /// back to an unfiltered random unwatched if the filter set is empty.
    /// </summary>
    private void OnSurpriseMeClick(object sender, RoutedEventArgs e)
    {
        var opts = _vm.BuildOptsForPick();
        var id = AppState.Instance.Db.GetRandomMovieIdMatching(opts, AppState.Instance.Connected);
        if (id == null)
        {
            if (App.MainWindow is MainWindow mw)
                mw.ShowToast("No movies match the current filter — try clearing some filters");
            return;
        }
        var dialog = new MovieDetailDialog(id.Value);
        dialog.WatchlistChanged += (_, _) => SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
        dialog.Activate();
    }

    private string? _shownRowKey = "";   // "" = not run yet, so the first call always queries
    private int _rowShowCount;

    /// <summary>
    /// v2.8.2 — when the current view is a user list, show that list's TV
    /// shows in a row above the movie grid. v4.0.0: Favorites, To Watch and
    /// Continue Watching get the same row; v4.3.0: Notes too. Re-queried only
    /// when the view changes (or the page is opened again), so routine VM
    /// updates stay cheap.
    /// </summary>
    private void RefreshShowsInList()
    {
        DatabaseService.TvShowPage? page =
            _vm.FavoritesOnly      ? DatabaseService.TvShowPage.Favorites :
            _vm.IsWatchlistOnly    ? DatabaseService.TvShowPage.Watchlist :
            _vm.IsContinueWatching ? DatabaseService.TvShowPage.ContinueWatching :
            _vm.HasNoteOnly        ? DatabaseService.TvShowPage.Notes :
            _vm.IsRecentlyAdded    ? DatabaseService.TvShowPage.RecentlyAdded : null;   // v4.8.0 (#25)
        var key = _vm.UserListId is int listId ? $"list:{listId}"
                : _vm.Genre is string genreKey ? $"genre:{genreKey}"
                : page?.ToString();
        if (key == _shownRowKey) return;
        _shownRowKey = key;

        var db = AppState.Instance.Db;
        var shows = _vm.UserListId is int id ? db.GetTvShowsInList(id, AppState.Instance.Connected)
                  : _vm.Genre is string genre ? db.GetTvShowsInGenre(genre, AppState.Instance.Connected)   // v4.8.0 (#25): By Genre
                  : page is { } p ? db.GetTvShowsForPage(p, AppState.Instance.Connected)
                  : new List<TvShowListItem>();
        ShowsInListHeader.Text = _vm.UserListId != null ? "TV SHOWS IN THIS LIST" : "TV SHOWS";
        ShowsInListRepeater.ItemsSource = ShowsInListRows.ItemsSource = shows.Count > 0 ? shows : null;
        ShowsInListSection.Visibility = shows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _rowShowCount = shows.Count;
        UpdateCountText();
        UpdateEmptyState();
    }

    /// <summary>"128 movies", plus " · 3 shows" when the shows row is showing (v4.0.0).</summary>
    private void UpdateCountText()
    {
        var movies = _vm.FilterTotal == 1 ? "1 movie" : $"{_vm.FilterTotal:N0} movies";
        MovieCountText.Text = _rowShowCount == 0 ? movies
            : $"{movies} · {(_rowShowCount == 1 ? "1 show" : $"{_rowShowCount} shows")}";
    }

    /// <summary>
    /// v2.6 — render a chip per active VM filter so the user can see what's
    /// narrowing the view and drop any single one with one click.
    ///
    /// Each chip's onClear action also reloads — VM filter setters have no
    /// partial OnXxxChanged handlers, so nulling the field alone wouldn't
    /// re-query. We do a reload + re-render the chip strip to keep
    /// everything in sync.
    /// </summary>
    private void UpdateFilterChips()
    {
        ActiveFilterChips.Items.Clear();
        AddChip("Genre",    _vm.Genre,           () => DropFilter(() => _vm.Genre = null));
        if (_vm.FilterDecadeStart.HasValue)
            AddChip("Decade", $"{_vm.FilterDecadeStart.Value}s",
                () => DropFilter(() => _vm.FilterDecadeStart = null));
        AddChip("Rating",   _vm.FilterRatingBand,() => DropFilter(() => _vm.FilterRatingBand = null));
        AddChip("Actor",    _vm.FilterActor,     () => DropFilter(() => _vm.FilterActor = null));
        AddChip("Director", _vm.FilterDirector,  () => DropFilter(() => _vm.FilterDirector = null));
        AddChip("Studio",   _vm.FilterStudio,    () => DropFilter(() => _vm.FilterStudio = null));
        if (_vm.FavoritesOnly)
            AddChip("Favorites", "♥",   () => DropFilter(() => _vm.FavoritesOnly = false));
        if (_vm.IsWatchlistOnly)
            AddChip("Watchlist", "📌",  () => DropFilter(() => _vm.IsWatchlistOnly = false));
        if (_vm.IsContinueWatching)
            AddChip("Continue", "▶",    () => DropFilter(() => _vm.IsContinueWatching = false));
        if (_vm.IsRecentlyWatched)
            AddChip("Recent", "🕓",     () => DropFilter(() => _vm.IsRecentlyWatched = false));
        if (_vm.IsRecentlyAdded)
            AddChip("New", "🆕",        () => DropFilter(() => _vm.IsRecentlyAdded = false));
        if (_vm.HasNoteOnly)
            AddChip("Notes", "📝",      () => DropFilter(() => _vm.HasNoteOnly = false));
        if (_vm.FewCastOnly)
            AddChip("Cast", "one or none", () => DropFilter(() => _vm.FewCastOnly = false));
        if (_vm.TagId != null && !string.IsNullOrEmpty(_vm.TagName))
            AddChip("Tag",   _vm.TagName, () => DropFilter(() => { _vm.TagId = null; _vm.TagName = null; }));
        ActiveFilterChips.Visibility = ActiveFilterChips.Items.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Apply a single filter mutation, then reload. If the result is "no
    /// filters left", also reset the page title so the breadcrumb doesn't
    /// stick around ("ALL MOVIES › 2010S" should revert to "ALL MOVIES").
    /// </summary>
    private void DropFilter(Action mutate)
    {
        mutate();
        if (!AnyFilterActive())
        {
            _vm.PageTitle = "All Movies";
            PageTitleText.Text = "All movies";
        }
        _ = _vm.LoadAsync();
        UpdateClearFiltersButton();
    }

    private bool AnyFilterActive() =>
        !string.IsNullOrEmpty(_vm.SearchText) ||
        _vm.WatchedFilter != WatchedFilter.All ||
        _vm.FavoritesOnly || _vm.IsWatchlistOnly || _vm.IsContinueWatching ||
        _vm.IsRecentlyWatched || _vm.IsRecentlyAdded || _vm.HasNoteOnly || _vm.FewCastOnly ||
        _vm.DriveSerial != null || _vm.Genre != null || _vm.CollectionId != null ||
        _vm.FilterActor != null || _vm.FilterDirector != null || _vm.FilterStudio != null ||
        _vm.FilterDecadeStart != null || _vm.FilterRatingBand != null ||
        _vm.UserListId != null || _vm.TagId != null;

    private void AddChip(string label, string? value, Action onClear)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var border = new Border
        {
            Background = CineLibraryCS.Services.ThemeBrushes.Get("ChipBrush"),
            BorderBrush = CineLibraryCS.Services.ThemeBrushes.Get("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 3, 6, 3),
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        sp.Children.Add(new TextBlock
        {
            FontSize = 11,
            Foreground = CineLibraryCS.Services.ThemeBrushes.Get("MutedBrush"),
            Text = label + ":",
            VerticalAlignment = VerticalAlignment.Center,
        });
        sp.Children.Add(new TextBlock
        {
            FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = CineLibraryCS.Services.ThemeBrushes.Get("TextBrush"),
            Text = value,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var x = new Button
        {
            Content = "✕", FontSize = 10,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Foreground = CineLibraryCS.Services.ThemeBrushes.Get("MutedBrush"),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 0, 4, 0),
            MinWidth = 18, MinHeight = 18,
        };
        ToolTipService.SetToolTip(x, $"Clear {label.ToLower()} filter");
        x.Click += (_, _) => onClear();
        sp.Children.Add(x);
        border.Child = sp;
        ActiveFilterChips.Items.Add(border);
    }

    // ── Search ────────────────────────────────────────────────────────────

    private void OnSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (!_ready) return;
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            _vm.SearchText = sender.Text;
        // v2.6 — Esc hint visibility tracks "has text" (Esc only does
        // something when there's a search to clear).
        UpdateSearchEscHint();
    }

    // v3.1 — search scope (All / Title / Cast & crew).
    private void OnScopeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (ScopeCombo.SelectedItem is ComboBoxItem item && item.Tag is string scope)
            _vm.SearchScope = scope;
    }

    private void OnSearchBoxFocus(object sender, RoutedEventArgs e)
    {
        SearchBox.BorderBrush = CineLibraryCS.Services.ThemeBrushes.Get("BrandPurpleBrush");
        UpdateSearchEscHint();
    }

    private void OnSearchBoxBlur(object sender, RoutedEventArgs e)
    {
        SearchBox.BorderBrush = CineLibraryCS.Services.ThemeBrushes.Get("InputBorderBrush");
        UpdateSearchEscHint();
    }

    private void UpdateSearchEscHint()
    {
        // Show only when the box is focused AND has text to clear.
        var hasText = !string.IsNullOrEmpty(SearchBox.Text);
        var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot)
            is FrameworkElement fe && (fe == SearchBox || fe.Parent == SearchBox);
        SearchEscHint.Visibility = (hasText && focused) ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Sort ──────────────────────────────────────────────────────────────

    private void OnSortChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (SortCombo.SelectedItem is not ComboBoxItem item) return;
        var parts = (item.Tag as string ?? "title:asc").Split(':');
        _vm.SortKey = parts[0] switch
        {
            "year"        => SortKey.Year,
            "rating"      => SortKey.Rating,
            "runtime"     => SortKey.Runtime,
            "date_added"  => SortKey.DateAdded,
            "last_played" => SortKey.LastPlayed,
            _             => SortKey.Title
        };
        _vm.SortDir = parts.Length > 1 && parts[1] == "desc" ? SortDir.Desc : SortDir.Asc;
    }

    // ── View mode ─────────────────────────────────────────────────────────

    private void OnViewGrid(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        GridViewToggle.IsChecked = true;
        ListViewToggle.IsChecked  = false;
        GridBorder.Visibility = Visibility.Visible;
        ListBorder.Visibility = Visibility.Collapsed;
        ApplyShowsLayout(false);
        _vm.ViewMode = ViewMode.Grid;
    }

    private void OnViewList(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        ListViewToggle.IsChecked  = true;
        GridViewToggle.IsChecked = false;
        GridBorder.Visibility = Visibility.Collapsed;
        ListBorder.Visibility = Visibility.Visible;
        ApplyShowsLayout(true);
        _vm.ViewMode = ViewMode.List;
    }

    // ── Watched filter ────────────────────────────────────────────────────

    private void OnWatchedFilter(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (sender is not Button btn) return;
        var pill = (Style)Application.Current.Resources["PillButtonStyle"];
        var pillActive = (Style)Application.Current.Resources["PillButtonActiveStyle"];
        FilterAll.Style       = pill;
        FilterWatched.Style   = pill;
        FilterUnwatched.Style = pill;
        btn.Style = pillActive;

        _vm.WatchedFilter = (btn.Tag as string) switch
        {
            "watched"   => WatchedFilter.Watched,
            "unwatched" => WatchedFilter.Unwatched,
            _           => WatchedFilter.All
        };
    }

    // ── Infinite scroll ───────────────────────────────────────────────────

    private void OnScrollChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        if (sv.VerticalOffset >= sv.ScrollableHeight - 300 && _vm.HasMore && !_vm.IsLoading)
            _ = _vm.LoadMoreAsync();
    }

    // ── Export ────────────────────────────────────────────────────────────

    // v3.9.0 — Export moved to Tools in the sidebar. These let it offer
    // "this view" and write every matching movie, not just the loaded pages.
    public int ViewCount => _vm.FilterTotal;
    public Task<List<MovieListItem>> GetViewMoviesAsync() => _vm.GetAllMatchingAsync();

    // ── Back/Toggle sidebar button ────────────────────────────────────────

    public void ShowBackButton(bool show)
    {
        // Button is already defined in XAML with Visibility="Collapsed"
        // This will be called by MainWindow to show/hide it
    }

    // v2.7 — "back to Browse" button. MainWindow drives visibility via
    // ShowBrowseBack / HideBrowseBack and handles the actual navigation.
    private void OnBrowseBackClick(object sender, RoutedEventArgs e)
        => (App.MainWindow as MainWindow)?.OnLibraryBackRequested();

    public void ShowBrowseBack(string label)
    {
        BackLabel.Text = label;
        BackToggleBtn.Visibility = Visibility.Visible;
    }

    public void HideBrowseBack() => BackToggleBtn.Visibility = Visibility.Collapsed;
}


