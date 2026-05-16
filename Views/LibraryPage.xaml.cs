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
        // Reset selection when the underlying list reloads (filter / search /
        // user-list change). The reload calls Movies.Clear() first, so we
        // hook the Reset action to wipe the selection set in lockstep.
        _vm.Movies.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset
                && _selectedIds.Count > 0)
            {
                _selectedIds.Clear();
                AfterSelectionChanged();
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

    private void AfterSelectionChanged()
    {
        MovieCardControl.CurrentSelectionCount = _selectedIds.Count;
        if (_selectedIds.Count == 0)
        {
            SelectionBar.Visibility = Visibility.Collapsed;
            return;
        }
        SelectionBar.Visibility = Visibility.Visible;
        SelectionCountText.Text = $"{_selectedIds.Count} selected";
        RefreshRemoveFromListVisibility();
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
            catch (Microsoft.Data.Sqlite.SqliteException) { /* dup */ }
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
            SidebarRefreshRequested?.Invoke(this, EventArgs.Empty);
            _ = _vm.LoadAsync();
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
        var anyFilter =
            !string.IsNullOrEmpty(_vm.SearchText) ||
            _vm.WatchedFilter != WatchedFilter.All ||
            _vm.FavoritesOnly ||
            _vm.IsWatchlistOnly ||
            _vm.DriveSerial != null ||
            _vm.Genre != null ||
            _vm.CollectionId != null ||
            _vm.FilterActor != null ||
            _vm.FilterDirector != null ||
            _vm.FilterStudio != null ||
            _vm.UserListId != null;
        ClearFiltersBtn.Visibility = anyFilter ? Visibility.Visible : Visibility.Collapsed;
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
        });
    }

    // ── Search ────────────────────────────────────────────────────────────

    private void OnSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (!_ready) return;
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            _vm.SearchText = sender.Text;
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


