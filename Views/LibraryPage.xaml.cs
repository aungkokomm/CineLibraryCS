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
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CineLibraryCS.Views;

public sealed partial class LibraryPage : Page
{
    private readonly LibraryViewModel _vm;
    public LibraryViewModel ViewModel => _vm;
    public event EventHandler? SidebarRefreshRequested;

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

        // Keyboard shortcuts
        AddAccelerator(VirtualKey.F, VirtualKeyModifiers.Control, (_, a) =>
        {
            SearchBox.Focus(FocusState.Programmatic);
            a.Handled = true;
        });
        AddAccelerator(VirtualKey.Escape, VirtualKeyModifiers.None, (_, a) =>
        {
            // Esc clears search first, then selection — never both at once.
            if (!string.IsNullOrEmpty(SearchBox.Text))
            {
                SearchBox.Text = "";
                _vm.SearchText = "";
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
            SelectAllVisible();
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

        _vm.PropertyChanged += OnVmPropertyChanged;
        _vm.Movies.CollectionChanged += (_, _) => UpdateEmptyState();

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
        Unloaded += (_, _) =>
        {
            MovieCardControl.AnyCardSelectionInteraction -= OnCardSelectionInteraction;
            if (MovieCardControl.ResolveSelectionForDrag == (Func<MovieListItem, IEnumerable<int>>)ResolveSelectionForDrag)
                MovieCardControl.ResolveSelectionForDrag = null;
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

    private void SelectAllVisible()
    {
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
            row.SidebarRefreshRequested += OnRowSidebarRefresh;
        }
    }

    private void OnListRepeaterElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is MovieRowControl row)
            row.SidebarRefreshRequested -= OnRowSidebarRefresh;
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
        SearchBox.Focus(FocusState.Programmatic);
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
        double rowPx = _vm.ViewMode == ViewMode.Grid
            ? MovieCardControl.GlobalCardHeight + 12
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
        var empty = _vm.Movies.Count == 0 && !_vm.IsLoading;
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
        PageTitleText.Text = "ALL MOVIES";
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
        if (sender is not Microsoft.UI.Xaml.Controls.Primitives.ToggleButton btn || btn.Tag is not string tag) return;
        ApplyDensity(tag);
        AppState.Instance.SetPref("gridDensity", tag);
    }

    private void ApplyDensity(string tag)
    {
        var (w, h) = tag switch
        {
            "S"  => (120.0, 220.0),
            "L"  => (190.0, 340.0),
            "XL" => (240.0, 420.0),
            _    => (150.0, 280.0),   // M
        };

        MovieCardControl.SetGlobalSize(w, h);
        GridLayout.MinItemWidth = w;
        GridLayout.MinItemHeight = h;

        DensityS.IsChecked  = tag == "S";
        DensityM.IsChecked  = tag == "M";
        DensityL.IsChecked  = tag == "L";
        DensityXL.IsChecked = tag == "XL";
    }

    private void SyncUiFromVm()
    {
        // Sort combo — match the item whose Tag corresponds to current SortKey+SortDir
        var keyStr = _vm.SortKey switch
        {
            SortKey.Year      => "year",
            SortKey.Rating    => "rating",
            SortKey.Runtime   => "runtime",
            SortKey.DateAdded => "date_added",
            _                 => "title"
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
    }

    public void ApplyNavParam(LibraryNavParam p)
    {
        SearchBox.Text = "";
        _vm.SearchText = "";

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

        // Breadcrumb-style title: "ALL MOVIES › DRAMA"
        PageTitleText.Text = p.Label == null
            ? "ALL MOVIES"
            : $"ALL MOVIES › {p.Label.ToUpper()}";
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (e.PropertyName == nameof(LibraryViewModel.TotalCount) ||
                e.PropertyName == nameof(LibraryViewModel.HasMore) ||
                e.PropertyName == nameof(LibraryViewModel.FilterTotal))
            {
                // "60 of 1,200 movies" while pages are still loading,
                // "850 movies" once everything fits.
                MovieCountText.Text = _vm.FilterTotal == _vm.TotalCount
                    ? $"{_vm.FilterTotal:N0} movies"
                    : $"{_vm.TotalCount:N0} of {_vm.FilterTotal:N0} movies";
            }
            if (e.PropertyName == nameof(LibraryViewModel.IsLoading))
            {
                LoadingRing.IsActive = _vm.IsLoading;
                LoadingRing.Visibility = _vm.IsLoading ? Visibility.Visible : Visibility.Collapsed;
                UpdateEmptyState();
            }
            // Any filter-related VM change should reflect in the Clear button
            UpdateClearFiltersButton();
            UpdateFilterChips();
        });
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
            AddChip("Favorites", "★",   () => DropFilter(() => _vm.FavoritesOnly = false));
        if (_vm.IsWatchlistOnly)
            AddChip("Watchlist", "📌",  () => DropFilter(() => _vm.IsWatchlistOnly = false));
        if (_vm.IsContinueWatching)
            AddChip("Continue", "▶",    () => DropFilter(() => _vm.IsContinueWatching = false));
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
            PageTitleText.Text = "ALL MOVIES";
        }
        _ = _vm.LoadAsync();
        UpdateClearFiltersButton();
    }

    private bool AnyFilterActive() =>
        !string.IsNullOrEmpty(_vm.SearchText) ||
        _vm.WatchedFilter != WatchedFilter.All ||
        _vm.FavoritesOnly || _vm.IsWatchlistOnly || _vm.IsContinueWatching ||
        _vm.DriveSerial != null || _vm.Genre != null || _vm.CollectionId != null ||
        _vm.FilterActor != null || _vm.FilterDirector != null || _vm.FilterStudio != null ||
        _vm.FilterDecadeStart != null || _vm.FilterRatingBand != null ||
        _vm.UserListId != null;

    private void AddChip(string label, string? value, Action onClear)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var border = new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ChipBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 3, 6, 3),
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        sp.Children.Add(new TextBlock
        {
            FontSize = 11,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["MutedBrush"],
            Text = label + ":",
            VerticalAlignment = VerticalAlignment.Center,
        });
        sp.Children.Add(new TextBlock
        {
            FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextBrush"],
            Text = value,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var x = new Button
        {
            Content = "✕", FontSize = 10,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["MutedBrush"],
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

    private void OnSearchBoxFocus(object sender, RoutedEventArgs e)
    {
        SearchBox.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)
            Application.Current.Resources["BrandPurpleBrush"];
        UpdateSearchEscHint();
    }

    private void OnSearchBoxBlur(object sender, RoutedEventArgs e)
    {
        SearchBox.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)
            Application.Current.Resources["InputBorderBrush"];
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
            "year"       => SortKey.Year,
            "rating"     => SortKey.Rating,
            "runtime"    => SortKey.Runtime,
            "date_added" => SortKey.DateAdded,
            _            => SortKey.Title
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
        _vm.ViewMode = ViewMode.Grid;
    }

    private void OnViewList(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        ListViewToggle.IsChecked  = true;
        GridViewToggle.IsChecked = false;
        GridBorder.Visibility = Visibility.Collapsed;
        ListBorder.Visibility = Visibility.Visible;
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

    private async void OnExportCsvDefault(SplitButton sender, SplitButtonClickEventArgs e)
        => await ExportCsvCore();

    private async void OnExportCsv(object sender, RoutedEventArgs e)
        => await ExportCsvCore();

    private async Task ExportCsvCore()
    {
        var path = await PickSaveFile("CSV file", ".csv", "movies_export");
        if (path == null) return;
        var mainVm = new MainViewModel();
        await mainVm.ExportCsvAsync(_vm.Movies, path);
        App.MainWindow?.ShowToast("Exported to CSV");
    }

    private async void OnExportHtml(object sender, RoutedEventArgs e)
    {
        var path = await PickSaveFile("HTML file", ".html", "movies_export");
        if (path == null) return;
        var mainVm = new MainViewModel();
        await mainVm.ExportHtmlAsync(_vm.Movies, path);
        App.MainWindow?.ShowToast("Exported to HTML");
    }

    private async Task<string?> PickSaveFile(string type, string ext, string suggestedName)
    {
        var picker = new FileSavePicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add(type, new List<string> { ext });
        picker.SuggestedFileName = suggestedName;
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    // ── Back/Toggle sidebar button ────────────────────────────────────────

    public void ShowBackButton(bool show)
    {
        // Button is already defined in XAML with Visibility="Collapsed"
        // This will be called by MainWindow to show/hide it
    }

    private void OnBackToggleClick(object sender, RoutedEventArgs e)
    {
        // Get the main window and toggle sidebar
        if (App.MainWindow is MainWindow mainWindow)
        {
            var toggleSidebarMethod = typeof(MainWindow).GetMethod("OnToggleSidebar", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            toggleSidebarMethod?.Invoke(mainWindow, new object[] { sender, e });
        }
    }
}


