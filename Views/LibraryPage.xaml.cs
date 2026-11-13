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
        SelAddToListBtn.Content = ActionLabel.AddToList("Add to list ▾");
        SelWatchedBtn.Content = ActionLabel.Watched(true);
        SelWatchlistBtn.Content = ActionLabel.ToWatch(true);
        SelFavBtn.Content = ActionLabel.Favorite(false);

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

        // 5.4.0: a person's films show whether they're on the Wishlist, as it changes elsewhere
        Wishlist.Changed += () =>
        {
            if (PersonFilmsRepeater.ItemsSource is not List<TmdbFilmItem> films) return;
            var wished = AppState.Instance.Db.GetWishlistIds();
            foreach (var film in films) film.Wished = wished.Contains(film.Film.TmdbId);
        };

        // 5.4.0: Alt+Left, the keyboard's Back key and the mouse's back button do ‹ Back too,
        // when the page has one. The keys are added here: XAML can't read "GoBack" as a key
        // (it stopped this page from loading).
        BackToggleBtn.KeyboardAccelerators.Add(new KeyboardAccelerator { Key = VirtualKey.Left, Modifiers = VirtualKeyModifiers.Menu });
        BackToggleBtn.KeyboardAccelerators.Add(new KeyboardAccelerator { Key = VirtualKey.GoBack });
        AddHandler(PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (BackToggleBtn.Visibility != Visibility.Visible
                || !e.GetCurrentPoint(this).Properties.IsXButton1Pressed) return;
            e.Handled = true;
            (App.MainWindow as MainWindow)?.OnLibraryBackRequested();
        }), handledEventsToo: true);

        // v4.0.0: the page is cached and put back on screen as it is, so
        // re-query the shows row each time; a show may have been made a
        // favorite (or finished) on its own page meanwhile.
        Loaded += (_, _) => { FitShowRows(); _shownRowKey = ""; RefreshShowsInList(); };
        // v4.9.0 (#25): the grid's cards widen with the window, and the shows rows' with them
        // (Watched & Gone sizes the same cards for its own row meanwhile, hence on Loaded too)
        GridBorder.SizeChanged += (_, e) => { if (e.PreviousSize.Width != e.NewSize.Width) FitShowRows(); };
        // v4.1.0: a show card's hover panel changed a show (un-favorited it on
        // Favorites, finished it on Continue Watching): re-list the row.
        TvShowCard.ShowChanged += _ => { _shownRowKey = ""; RefreshShowsInList(); };
        Services.Playback.BuiltInPlayer.Stopped += OnPlaybackStopped;

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
        foreach (var repeater in ContinueRepeaters)
        {
            repeater.ElementPrepared += OnGridRepeaterElementPrepared;
            repeater.ElementPrepared += OnListRepeaterElementPrepared;
            repeater.ElementClearing += OnGridRepeaterElementClearing;
            repeater.ElementClearing += OnListRepeaterElementClearing;
            repeater.Tapped += (s, e) => OpenTappedShow(e.OriginalSource);
        }
        ContinueGroups.SizeChanged += (_, e) => { if (e.PreviousSize.Width != e.NewSize.Width) FitShowRows(); };
        _vm.Movies.CollectionChanged += (_, _) =>
        {
            if (_continuePosted || !_vm.IsContinueWatching) return;
            _continuePosted = true;
            DispatcherQueue.Post(() => { _continuePosted = false; ApplyContinueGroups(); });
        };

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
                        DispatcherQueue.Post(() =>
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
            $"{(anyOff ? "Added" : "Removed")} {changed.Count} {(anyOff ? "to" : "from")} To Watch",
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
        DispatcherQueue.Post(() =>
        {
            _ = _vm.LoadAsync();
            _personKey = null;   // 5.4.0: a person's Watched & Gone and other films again
            RefreshPerson();
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

    /// <summary>5.4.0: the cards again from the database, after a scan in the background.</summary>
    public void Reload() => _ = _vm.LoadAsync();

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
                    && _rowShowCount == 0
                    && _vm.FilterActor == null && _vm.FilterDirector == null;   // 5.4.0: a person's page has its own parts
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
                "Point CineLibrary at your movies folder, scraped with CineLibrary Essentials " +
                "(each movie in its own folder with a .nfo + poster). " +
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
        UpdateAudioButton();
        PageTitleText.Text = "All movies";
        UpdateClearFiltersButton();
    }

    // ── Audio language (5.3.0) ────────────────────────────────────────────

    /// <summary>Any, then the languages the films' sound is in, most films first, with how many.</summary>
    private void OnAudioMenuOpening(object sender, object e)
    {
        AudioMenu.Items.Clear();
        void Add(string text, string? count, string? name, IReadOnlyList<string>? codes)
        {
            var item = new RadioMenuFlyoutItem { Text = text, GroupName = "audio", IsChecked = _vm.FilterAudio == name };
            if (count != null) item.KeyboardAcceleratorTextOverride = count;
            item.Click += (_, _) =>
            {
                _vm.SetAudioFilter(name, codes);
                UpdateAudioButton();
                _ = _vm.LoadAsync();
                UpdateClearFiltersButton();
            };
            AudioMenu.Items.Add(item);
        }
        Add("Any", null, null, null);
        var languages = AppState.Instance.Db.GetAudioLanguages();
        if (languages.Count > 0) AudioMenu.Items.Add(new MenuFlyoutSeparator());
        foreach (var (name, codes, films) in languages) Add(name, films.ToString("N0"), name, codes);
    }

    private void UpdateAudioButton()
    {
        AudioLabel.Text = _vm.FilterAudio is { } name ? $"Audio: {name}" : "Audio: Any";
        AudioDropDown.Style = (Style)Application.Current.Resources[_vm.FilterAudio is null ? "PillDropDownStyle" : "PillDropDownActiveStyle"];
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
        foreach (var personGrid in new[] { PersonFilmsRepeater, PersonGoneRepeater })   // 5.4.0: a person's other films
            Controls.CardCell.FitGrid((UniformGridLayout)personGrid.Layout,
                (MovieCardControl.GlobalCardWidth, MovieCardControl.GlobalCardHeight), tag);
        // the show rows above the grid: spaced like it, and as far from it as its rows
        // (v4.9.0: counting the room under them for their scroll bar)
        ((StackLayout)ShowsInSearchRepeater.Layout).Spacing = ((StackLayout)ShowsInListRepeater.Layout).Spacing = Controls.CardCell.GapFor(tag);
        ShowsInSearchSection.Margin = ShowsInListSection.Margin = new Thickness(0, 0, 0, GridLayout.MinRowSpacing - ShowsInListCards.Padding.Bottom);
        FitShowRows();
        _continueList = null;
        ApplyContinueGroups();

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
        ApplyContinueGroups();
    }

    /// <summary>v4.9.0 (#25): the show cards in the rows above the grid as big as its movie cards.
    /// 4.10.0: and those in Continue Watching's groups, the width they're laid out in.</summary>
    private void FitShowRows() => TvShowCard.FitRowsToGrid(GridLayout,
        ContinueGroups.Visibility == Visibility.Visible
            ? Microsoft.UI.Xaml.Controls.Primitives.LayoutInformation.GetAvailableSize(ContinueGroups).Width - ContinueGroups.Padding.Left - ContinueGroups.Padding.Right
            : Microsoft.UI.Xaml.Controls.Primitives.LayoutInformation.GetAvailableSize(GridRepeater).Width,
        [ShowsInListRepeater, ShowsInSearchRepeater, PersonGoneShowsRepeater, .. ContinueRepeaters]);

    private ItemsRepeater[] ContinueRepeaters => new[] { ContinueWeek, ContinueMonth, ContinueEarlier };
    private bool? _continueList;   // cards or rows in the groups now; null to lay them out afresh
    private bool _continuePosted;

    /// <summary>
    /// 4.10.0: Continue Watching in its own date order comes in three groups, films and
    /// shows together, latest first. Any other sort lists them as the other pages do.
    /// </summary>
    private void ApplyContinueGroups()
    {
        var grouped = _vm.IsContinueWatching && _vm.SortKey == SortKey.LastPlayed && _vm.SortDir == SortDir.Desc;
        var list = _vm.ViewMode == ViewMode.List;
        GridBorder.Visibility = !grouped && !list ? Visibility.Visible : Visibility.Collapsed;
        ListBorder.Visibility = !grouped && list ? Visibility.Visible : Visibility.Collapsed;
        ShowsInListSection.Visibility = !grouped && _rowShowCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        ContinueGroups.Visibility = grouped ? Visibility.Visible : Visibility.Collapsed;
        if (!grouped)
        {
            foreach (var repeater in ContinueRepeaters) repeater.ItemsSource = null;
            return;
        }

        if (_continueList != list)
        {
            _continueList = list;
            foreach (var repeater in ContinueRepeaters)
            {
                repeater.ItemsSource = null;
                repeater.ItemTemplate = Resources[list ? "ContinueRows" : "ContinueCards"];
                repeater.Layout = list ? new StackLayout { Spacing = 2 } : new UniformGridLayout
                {
                    MinItemWidth = GridLayout.MinItemWidth,
                    MinItemHeight = GridLayout.MinItemHeight + TvShowCard.CaptionHeight - MovieCardControl.CaptionHeight,
                    MinColumnSpacing = GridLayout.MinColumnSpacing,
                    MinRowSpacing = GridLayout.MinRowSpacing,
                    ItemsStretch = GridLayout.ItemsStretch,
                };
            }
        }

        var items = _vm.Movies.Select(m => (Played: m.LastPlayed, Item: (object)m))
            .Concat(_rowShows.Select(s => (Played: s.LastPlayed, Item: (object)s)))
            .OrderByDescending(i => i.Played).ToList();
        const long Day = 86_400;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var groups = new[]
        {
            (Header: ContinueWeekHeader, Repeater: ContinueWeek, From: now - 7 * Day, To: long.MaxValue),
            (Header: ContinueMonthHeader, Repeater: ContinueMonth, From: now - 30 * Day, To: now - 7 * Day),
            (Header: ContinueEarlierHeader, Repeater: ContinueEarlier, From: long.MinValue, To: now - 30 * Day),
        };
        foreach (var g in groups)
        {
            var part = items.Where(i => i.Played >= g.From && i.Played < g.To).Select(i => i.Item).ToList();
            g.Header.Visibility = g.Repeater.Visibility = part.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (g.Repeater.ItemsSource is not List<object> shown || !shown.SequenceEqual(part))
                g.Repeater.ItemsSource = part;
        }
        UpdateStillWatching(now, ContinueEarlierHeader.Visibility == Visibility.Visible);
    }

    // ── 5.3.0: Still watching? ────────────────────────────────────────────

    private const long StillWatchingAfter = 60 * 86_400;   // not gone back to in two months
    private const string StillWatchingKeptPref = "continueKeptFrom";   // "Keep": films played before this aren't asked about

    private long StillWatchingFrom() =>
        long.TryParse(AppState.Instance.GetPref(StillWatchingKeptPref, "0"), out var from) ? from : 0;

    /// <summary>Under Earlier: the films not gone back to in two months, to take out of Continue Watching.
    /// Films only: a show is there because some of its episodes were watched.</summary>
    private void UpdateStillWatching(long now, bool earlierShown)
    {
        var stale = earlierShown ? AppState.Instance.Db.GetContinueWatchingPlayedBetween(StillWatchingFrom(), now - StillWatchingAfter) : 0;
        StillWatchingBar.Visibility = stale > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (stale == 0) return;
        StillWatchingText.Text = stale == 1
            ? "One film here you haven't gone back to in over two months. Still watching it?"
            : $"{stale} films here you haven't gone back to in over two months. Still watching them?";
        StillWatchingRemoveBtn.Content = stale == 1 ? "Remove it" : "Remove them";
    }

    private void OnStillWatchingRemove(object sender, RoutedEventArgs e)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var removed = AppState.Instance.Db.HideFromContinueWatching(StillWatchingFrom(), now - StillWatchingAfter);
        StillWatchingBar.Visibility = Visibility.Collapsed;
        _ = _vm.LoadAsync();
        SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
        if (App.MainWindow is MainWindow mw && removed > 0)
            mw.ShowToast(removed == 1 ? "Out of Continue Watching. Played again, it starts where it stopped"
                                      : $"{removed} films out of Continue Watching. Played again, each starts where it stopped");
    }

    private void OnStillWatchingKeep(object sender, RoutedEventArgs e)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        AppState.Instance.SetPref(StillWatchingKeptPref, (now - StillWatchingAfter).ToString(System.Globalization.CultureInfo.InvariantCulture));
        StillWatchingBar.Visibility = Visibility.Collapsed;
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
        DispatcherQueue.Post(() =>
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
            RefreshPerson();
            UpdatePersonSections();
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

    /// <summary>v4.10.0: a film or episode stopped: its card shows how far it got, and Continue
    /// Watching lists what's still part-way, latest first.</summary>
    private void OnPlaybackStopped(PlaybackItem item)
    {
        if (item.Kind == PlaybackKind.Movie && _vm.Movies.FirstOrDefault(m => m.Id == item.Id) is { } movie)
            movie.ResumeMs = AppState.Instance.Db.GetResumeMs(false, item.Id);
        if (!_vm.IsContinueWatching) return;
        _vm.ShowContinueWatching();
        _shownRowKey = "";
        RefreshShowsInList();
    }

    // ── 5.4.0: the actor or director above their films ───────────────────────

    private string? _personKey;
    private PersonInfo? _person;
    private bool _personBioOpen;
    private bool _personFilms;   // the "not in your library" part is ready to show
    private int _personGoneCount;
    private DatabaseService.PersonStats? _personStats;   // 5.4.1: the numbers under "In your library"
    private int _personGen;                              // which opening of a person a late answer belongs to
    private int _baseFacts;                              // born, age, birthplace: Awards go after them

    /// <summary>
    /// On "Movies with" an actor or "Directed by" a director: their photo, facts and
    /// biography from TMDb, then their films in three parts: in your library (the grid),
    /// in Watched &amp; Gone, and not in your library (a director's every film, an actor's
    /// best known). Kept after the first look (see People), so it shows at once and
    /// offline after that.
    /// </summary>
    private async void RefreshPerson()
    {
        var name = _vm.FilterActor ?? _vm.FilterDirector;
        var director = _vm.FilterActor == null && name != null;
        var key = name == null ? null : $"{(director ? "d" : "a")}:{name}";
        if (key == _personKey) return;
        _personKey = key;
        _person = null;
        _personBioOpen = false;
        _personFilms = false;
        PersonPanel.Visibility = PersonOwnedHeader.Visibility = Visibility.Collapsed;
        PersonGoneSection.Visibility = PersonFilmsSection.Visibility = Visibility.Collapsed;
        PersonFilmsRepeater.ItemsSource = PersonGoneRepeater.ItemsSource = PersonGoneShowsRepeater.ItemsSource = null;
        _personGoneCount = 0;
        PersonFacts.Children.Clear();
        PersonPeople.Children.Clear();
        PersonAwardsPanel.Child = null;
        PersonAwardsPanel.Visibility = Visibility.Collapsed;
        _personStats = null;
        var gen = ++_personGen;
        if (name == null) return;

        // 5.4.1: from the library at once: the numbers under "In your library", and who they work with
        _personStats = AppState.Instance.Db.GetPersonStats(name, director);
        ShowCollaborators(name, director);

        // Their Watched & Gone records by the cast and crew kept with them (offline too)
        ShowPersonGone(name, director, null);

        // While TMDb answers (at once when it's kept): the initials and a line saying so
        PersonInitials.Text = Initials(name);
        PersonPhoto.Source = null;
        PersonLife.Text = "Looking them up on TMDb…";
        PersonLife.Visibility = Visibility.Visible;
        PersonBio.Visibility = PersonLinks.Visibility = Visibility.Collapsed;
        PersonPanel.Visibility = Visibility.Visible;
        UpdatePersonSections();

        var (info, offline) = await People.GetAsync(name);
        if (_personKey != key) return;   // another person was opened meanwhile
        if (info == null)
        {
            if (offline) PersonLife.Text = "Connect to the internet to see who they are and their other films.";
            else if (PersonPeople.Children.Count > 0) PersonLife.Text = director ? "Director" : "Actor";   // TMDb doesn't know them
            else PersonPanel.Visibility = Visibility.Collapsed;
            return;
        }
        _person = info;

        var photo = People.PhotoPath(info.TmdbId);
        if (File.Exists(photo)) PersonPhoto.Source = new BitmapImage(new Uri(photo)) { DecodePixelWidth = 192 };
        var today = DateTime.Today;
        ShowFacts(info, today);
        _baseFacts = PersonFacts.Children.Count;
        if (!director && People.BirthYear(info) is int born) _vm.SetBirthYear(name, born);   // 5.4.1: their age in each film
        _ = ShowPersonAwardsAsync(info.ImdbId, gen);
        PersonLife.Text = director ? "Director" : "Actor";
        PersonLife.Visibility = PersonFacts.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PersonBio.Text = info.Biography.Length > 0 ? info.Biography : "TMDb has no biography for them yet.";
        PersonBio.MaxLines = 3;
        PersonMoreBtn.Content = "More";
        PersonMoreBtn.Visibility = Visibility.Collapsed;   // shown when the text is cut (OnPersonBioTrimmed)
        PersonImdbBtn.Visibility = string.IsNullOrEmpty(info.ImdbId) ? Visibility.Collapsed : Visibility.Visible;
        PersonBio.Visibility = PersonLinks.Visibility = Visibility.Visible;
        PersonBio.UpdateLayout();
        if (PersonBio.IsTextTrimmed) PersonMoreBtn.Visibility = Visibility.Visible;

        // Their films you had: Watched & Gone records that are films of theirs on TMDb too
        var db = AppState.Instance.Db;
        var owned = new OwnedMovies(db.GetMovieKeys());
        var gone = new OwnedMovies(db.GetMovieKeys(gone: true));
        ShowPersonGone(name, director, (director ? info.Directed : info.Acted).Select(gone.Find).OfType<int>().ToHashSet());

        // and the ones you don't have and haven't seen
        var (missing, total, _, _) = People.Strip(info, director, owned, gone, today);
        // 5.4.1: how many of a director's films you've seen (watched in the library, or in Watched & Gone)
        if (director && info.Directed.Where(f => People.IsReleased(f, today)).ToList() is { Count: > 0 } released)
        {
            var watched = db.GetWatchedMovieIds();
            var seen = released.Count(f => owned.Find(f) is int row && watched.Contains(row) || gone.Has(f));
            var green = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentGreenBrush"];
            PersonFacts.Children.Add(Fact(green, "Seen", SeenBar(seen, released.Count, green)));
        }
        _personFilms = director ? total > 0 : missing.Count > 0;
        string Films(int n) => n == 1 ? "film" : "films";
        PersonFilmsCount.Text = missing.Count.ToString("N0");
        PersonFilmsCountPill.Visibility = missing.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PersonFilmsNote.Text = !director ? "Their best-known films"
            : missing.Count == 0 ? $"You have or have seen all {total} {Films(total)} they directed"
            : $"of the {total} {Films(total)} they directed";
        var wished = db.GetWishlistIds();
        PersonFilmsRepeater.ItemsSource = missing.Select(f => new TmdbFilmItem(f, People.YearText(f, today),
            Wishlist.Poster(f), info.Name.Length > 0 ? info.Name : name, wished.Contains(f.TmdbId))).ToList();
        PersonFilmsCards.Visibility = missing.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdatePersonSections();
    }

    /// <summary>5.4.1: a label in its colour over a value with the bar beside it, as DetailField.Field, in any colour.</summary>
    private static StackPanel Fact(Microsoft.UI.Xaml.Media.Brush brush, string label, UIElement value)
    {
        var fact = new StackPanel { Spacing = 5 };
        fact.Children.Add(DetailField.Label(label, brush));
        fact.Children.Add(new Border { BorderBrush = brush, BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(10, 0, 0, 0), Child = value });
        return fact;
    }

    /// <summary>5.4.1: "7 of their 12 films" over a bar.</summary>
    private static StackPanel SeenBar(int seen, int total, Microsoft.UI.Xaml.Media.Brush brush)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = $"{seen} of their {total} {(total == 1 ? "film" : "films")}", FontSize = 14, Foreground = ThemeBrushes.Get("TextBrush") });
        panel.Children.Add(new ProgressBar { Width = 180, Maximum = total, Value = seen, Foreground = brush, HorizontalAlignment = HorizontalAlignment.Left });
        return panel;
    }

    /// <summary>
    /// 5.4.1: who they work with most in your films: an actor's co-stars and directors, a
    /// director's actors, each with how many films; click one for their page (‹ Back to this one).
    /// </summary>
    private void ShowCollaborators(string name, bool director)
    {
        PersonPeople.Children.Clear();
        var people = AppState.Instance.Db.GetCollaborators(name, director);
        void Add(string label, string brushKey, List<(string Name, int Films, bool IsDirector)> list)
        {
            if (list.Count == 0) return;
            var shown = list.Select(p => $"{p.Name} ({p.Films})").ToList();
            var person = shown.Zip(list).ToDictionary(x => x.First, x => x.Second);
            var brush = ThemeBrushes.Get(brushKey);
            var names = DetailField.Names(shown, s => { if (person.TryGetValue(s, out var p)) GoToPerson(p.Name, p.IsDirector); },
                                          fontSize: 14, hover: brush);
            names.MaxWidth = 560;
            PersonPeople.Children.Add(Fact(brush, label, names));
        }
        Add(director ? "Often cast" : "Often with", "DetailPeopleBrush", people.Where(p => !p.IsDirector).ToList());
        Add("Often directed by", "DetailProductionBrush", people.Where(p => p.IsDirector).ToList());
    }

    private void GoToPerson(string person, bool isDirector)
    {
        if (App.MainWindow is not MainWindow mw) return;
        var label = _vm.FilterActor ?? _vm.FilterDirector ?? "Back";
        var back = mw.CaptureView();
        if (isDirector) mw.NavigateLibraryByDirector(person);
        else mw.NavigateLibraryByActor(person);
        mw.SetLibraryBack(label, back);
    }

    /// <summary>5.4.1: their awards from Wikidata: a summary among the facts that opens the list.</summary>
    private async Task ShowPersonAwardsAsync(string? imdbId, int gen)
    {
        var awards = await Awards.ForPersonAsync(imdbId);
        if (gen != _personGen || awards is not { Count: > 0 }) return;
        var link = new HyperlinkButton { Content = Awards.Summary(awards), Padding = new Thickness(0), FontSize = 14 };
        link.Click += (_, _) => PersonAwardsPanel.Visibility =
            PersonAwardsPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        ToolTipService.SetToolTip(link, "Show or hide their awards (from Wikidata)");
        PersonFacts.Children.Insert(Math.Min(_baseFacts, PersonFacts.Children.Count), Fact(ThemeBrushes.Get("ImdbGoldBrush"), "Awards", link));
        PersonAwardsPanel.Child = AwardsView.List(awards, forFilm: false);
    }

    /// <summary>Born, age or died, and birthplace, coloured by kind as the movie window's details.</summary>
    private void ShowFacts(PersonInfo info, DateTime today)
    {
        PersonFacts.Children.Clear();
        foreach (var (kind, label, value) in People.Facts(info, today))
        {
            var colour = kind switch
            {
                "born" => DetailField.Kind.People,
                "age" => DetailField.Kind.Production,
                "died" => DetailField.Kind.Story,
                _ => DetailField.Kind.File,
            };
            PersonFacts.Children.Add(DetailField.Field(colour, label, new TextBlock
            {
                Text = value, FontSize = 14, TextWrapping = TextWrapping.Wrap, MaxWidth = 420,
                Foreground = ThemeBrushes.Get("TextBrush"),
            }));
        }
    }

    /// <summary>
    /// The person's films and shows in Watched &amp; Gone: the records with them in the cast
    /// (or as the director) kept with the record, and <paramref name="matched"/>, the records
    /// that are their films on TMDb. Newest first.
    /// </summary>
    private void ShowPersonGone(string name, bool director, IReadOnlySet<int>? matched)
    {
        var db = AppState.Instance.Db;
        var connected = AppState.Instance.Connected;
        var movies = db.GetMovies(new DatabaseService.ListOptions(ArchivedOnly: true,
            Actor: director ? null : name, Director: director ? name : null,
            SortKey: "year", SortDir: "desc", Limit: 10000), connected);
        if (matched is { Count: > 0 })
        {
            var have = movies.Select(m => m.Id).ToHashSet();
            var more = db.GetMovies(new DatabaseService.ListOptions(ArchivedOnly: true, SortKey: "year", SortDir: "desc",
                Limit: 10000), connected).Where(m => matched.Contains(m.Id) && !have.Contains(m.Id));
            movies = movies.Concat(more).OrderByDescending(m => m.Year ?? 0).ToList();
        }
        var shows = director ? new List<TvShowListItem>() : db.GetTvShowsWithActor(name, connected, gone: true);
        PersonGoneRepeater.ItemsSource = movies.Count > 0 ? movies : null;
        PersonGoneShowsRepeater.ItemsSource = shows.Count > 0 ? shows : null;
        PersonGoneMovies.Visibility = movies.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PersonGoneShowsCards.Visibility = shows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var m in movies) m.AgeInRole = _vm.AgeIn(m);   // 5.4.1: once their birthday is known
        _personGoneCount = movies.Count + shows.Count;
        PersonGoneCount.Text = _personGoneCount.ToString("N0");
        PersonGoneNote.Text = "Watched and let go, kept as records";
        UpdatePersonSections();
    }

    /// <summary>
    /// The headings and parts of a person's page. Watched &amp; Gone and the films not in
    /// the library come after every one of theirs in it, so only once the grid holds them all.
    /// </summary>
    private void UpdatePersonSections()
    {
        var person = _personKey != null;
        var loaded = person && !_vm.HasMore && !_vm.IsLoading;
        var (movies, shows) = (_vm.FilterTotal, _rowShowCount);
        // (not while the first of their films load: the count would be the last page's)
        PersonOwnedHeader.Visibility = person && (_vm.Movies.Count > 0 || !_vm.IsLoading) ? Visibility.Visible : Visibility.Collapsed;
        PersonOwnedCount.Text = (movies + shows).ToString("N0");
        var note = new List<string>();
        if (movies + shows == 0) note.Add("None of theirs yet");
        else if (shows > 0) note.Add($"{movies:N0} {(movies == 1 ? "movie" : "movies")}, {shows:N0} TV {(shows == 1 ? "show" : "shows")}");
        if (_personStats is { Films: > 0 } st)   // 5.4.1: your history with them
        {
            note.Add($"{st.Watched:N0} watched");
            if (st.AverageRating is double avg) note.Add($"★ {avg.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} average");
            if (st.FirstYear is int first && st.LastYear is int last) note.Add(first == last ? $"{first}" : $"{first}-{last}");
        }
        PersonOwnedNote.Text = string.Join(" · ", note);
        PersonGoneSection.Visibility = loaded && _personGoneCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        PersonFilmsSection.Visibility = loaded && _personFilms ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "?"
             : parts.Length == 1 ? parts[0][..1].ToUpperInvariant()
             : (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
    }

    private void OnPersonBioTrimmed(TextBlock sender, IsTextTrimmedChangedEventArgs e)
    {
        PersonMoreBtn.Visibility = sender.IsTextTrimmed || _personBioOpen ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPersonMore(object sender, RoutedEventArgs e)
    {
        _personBioOpen = !_personBioOpen;
        PersonBio.MaxLines = _personBioOpen ? 0 : 3;
        PersonMoreBtn.Content = _personBioOpen ? "Less" : "More";
    }

    private async void OnPersonLink(object sender, RoutedEventArgs e)
    {
        if (_person is not { } p) return;
        var url = ReferenceEquals(sender, PersonImdbBtn) ? $"https://www.imdb.com/name/{p.ImdbId}/"
                                                         : $"https://www.themoviedb.org/person/{p.TmdbId}";
        try { await Launcher.LaunchUriAsync(new Uri(url)); } catch { }
    }

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
        var term = $"{q}|{_vm.WatchedFilter}";
        if (term == _lastShowsSearchTerm) return;
        _lastShowsSearchTerm = term;

        var shows = ByWatched(AppState.Instance.Db.SearchTvShows(q, AppState.Instance.Connected, limit: 24));
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
    private List<TvShowListItem> _rowShows = new();

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
                : _vm.FilterActor is string actorKey ? $"actor:{actorKey}"
                : page?.ToString();
        key = $"{key}|{_vm.WatchedFilter}|{_vm.FilterAudio}";
        if (key == _shownRowKey) { ApplyContinueGroups(); return; }
        _shownRowKey = key;

        var db = AppState.Instance.Db;
        var shows = _vm.UserListId is int id ? db.GetTvShowsInList(id, AppState.Instance.Connected)
                  : _vm.Genre is string genre ? db.GetTvShowsInGenre(genre, AppState.Instance.Connected)   // v4.8.0 (#25): By Genre
                  : _vm.FilterActor is string actor ? db.GetTvShowsWithActor(actor, AppState.Instance.Connected)   // 5.4.0: their shows
                  : page is { } p ? db.GetTvShowsForPage(p, AppState.Instance.Connected)
                  : new List<TvShowListItem>();
        shows = ByWatched(shows);
        if (_vm.FilterAudio != null) shows = new();   // 5.3.0: the Audio filter is the films' sound; shows aren't in it
        ShowsInListHeader.Text = _vm.UserListId != null ? "TV SHOWS IN THIS LIST" : "TV SHOWS";
        ShowsInListRepeater.ItemsSource = ShowsInListRows.ItemsSource = shows.Count > 0 ? shows : null;
        _rowShows = shows;
        _rowShowCount = shows.Count;
        ApplyContinueGroups();
        UpdateCountText();
        UpdateEmptyState();
    }

    /// <summary>4.10.0 (#25): the Watched and Unwatched pills cover the shows too, as on All TV shows.</summary>
    private List<TvShowListItem> ByWatched(List<TvShowListItem> shows) => _vm.WatchedFilter switch
    {
        WatchedFilter.Watched => shows.Where(s => s.FullyWatched).ToList(),
        WatchedFilter.Unwatched => shows.Where(s => !s.FullyWatched).ToList(),
        _ => shows,
    };

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
        AddChip("Writer",   _vm.FilterWriter,    () => DropFilter(() => _vm.FilterWriter = null));
        // 5.3.0 (#25): the sidebar's own icons, in its colours (were emoji)
        if (_vm.FavoritesOnly)
            AddChip("Favorites", "", () => DropFilter(() => _vm.FavoritesOnly = false), "NavLibraryIconBrush");
        if (_vm.IsWatchlistOnly)
            AddChip("To Watch", "",  () => DropFilter(() => _vm.IsWatchlistOnly = false), "NavLibraryIconBrush");
        if (_vm.IsContinueWatching)
            AddChip("Continue", "",  () => DropFilter(() => _vm.IsContinueWatching = false), "NavLibraryIconBrush");
        if (_vm.IsRecentlyWatched)
            AddChip("Recent", "",    () => DropFilter(() => _vm.IsRecentlyWatched = false), "NavDiscoverIconBrush");
        if (_vm.IsRecentlyAdded)
            AddChip("New", "",       () => DropFilter(() => _vm.IsRecentlyAdded = false), "NavDiscoverIconBrush");
        if (_vm.HasNoteOnly)
            AddChip("Notes", "",     () => DropFilter(() => _vm.HasNoteOnly = false), "NavLibraryIconBrush");
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
        _vm.FilterActor != null || _vm.FilterDirector != null || _vm.FilterStudio != null || _vm.FilterWriter != null ||
        _vm.FilterDecadeStart != null || _vm.FilterRatingBand != null ||
        _vm.UserListId != null || _vm.TagId != null || _vm.FilterAudio != null;

    /// <summary>A filter chip: "Label: value" and a ✕. With iconBrush the value is an icon glyph, drawn in that brush.</summary>
    private void AddChip(string label, string? value, Action onClear, string? iconBrush = null)
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
        if (iconBrush != null)
            sp.Children.Add(new FontIcon
            {
                Glyph = value, FontSize = 13,
                Foreground = CineLibraryCS.Services.ThemeBrushes.Get(iconBrush),
                VerticalAlignment = VerticalAlignment.Center,
            });
        else
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
        ApplyContinueGroups();
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
        ApplyContinueGroups();
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

    // 5.4.0: what MainWindow.CaptureView keeps, to come back to this view
    public string PageTitle => PageTitleText.Text;
    public string? BackLabelShown => BackToggleBtn.Visibility == Visibility.Visible ? BackLabel.Text : null;
    public double ScrollOffset => MainScroller.VerticalOffset;

    /// <summary>Scrolls to where the view was, loading more of it first when that was further down.</summary>
    public async Task ScrollToAsync(double offset)
    {
        for (var i = 0; i < 30 && _vm.HasMore; i++)
        {
            MainScroller.UpdateLayout();
            if (MainScroller.ScrollableHeight >= offset) break;
            await _vm.LoadMoreAsync();
        }
        MainScroller.UpdateLayout();
        MainScroller.ChangeView(null, offset, null, true);
    }
}


