using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using CineLibraryCS.Services;

namespace CineLibraryCS.Views;

/// <summary>
/// v4.7.0: the Tags page (BROWSE → Tags). Your tags and the ones from .nfo
/// files as pills with their counts; click one to see its movies and shows.
/// Right-click: Pin to sidebar, and for your own tags Rename and Delete
/// (a tag from an .nfo file lives in the file, so CineLibrary Essentials changes it).
/// </summary>
public sealed partial class TagsPage : Page
{
    private List<DatabaseService.TagSummary> _tags = new();
    private string _show = "all";   // all | yours | nfo
    private bool _ready;            // the sort box fires while the page is being built

    public TagsPage()
    {
        InitializeComponent();
    }

    /// <summary>(Re)reads the tags and draws them.</summary>
    public void Load()
    {
        _ready = true;
        _tags = AppState.Instance.Db.GetAllTags().Where(t => t.Total > 0).ToList();
        var yours = _tags.Count(t => t.Yours);
        CountText.Text = $"{Plural(_tags.Count, "tag")} · {yours:N0} yours";
        Draw();
    }

    private void Draw()
    {
        var q = SearchBox.Text.Trim();
        IEnumerable<DatabaseService.TagSummary> shown = _tags;
        if (q.Length > 0) shown = shown.Where(t => t.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
        shown = SortCombo.SelectedIndex == 1
            ? shown.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            : shown.OrderByDescending(t => t.Total).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase);
        var list = shown.ToList();
        var pinned = SidebarPrefs.PinnedTags.ToHashSet(StringComparer.OrdinalIgnoreCase);

        YoursPanel.Children.Clear();
        NfoPanel.Children.Clear();
        foreach (var t in list)
        {
            if (t.Yours && _show != "nfo") YoursPanel.Children.Add(Pill(t, pinned.Contains(t.Name)));
            else if (!t.Yours && _show != "yours") NfoPanel.Children.Add(Pill(t, pinned.Contains(t.Name)));
        }
        YoursSection.Visibility = YoursPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NfoSection.Visibility = NfoPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var empty = YoursPanel.Children.Count + NfoPanel.Children.Count == 0;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _tags.Count == 0
            ? "No tags yet. Add one in a movie's window, or with CineLibrary Essentials."
            : "No tags match.";
    }

    private Button Pill(DatabaseService.TagSummary t, bool pinned)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (pinned)
            content.Children.Add(new FontIcon
            {
                Glyph = "", FontSize = 11, FontFamily = new FontFamily("Segoe Fluent Icons,Segoe MDL2 Assets"),
                Foreground = ThemeBrushes.Get("BrandPurpleBrush"), VerticalAlignment = VerticalAlignment.Center,
            });
        content.Children.Add(new TextBlock { Text = t.Name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock
        {
            Text = t.Total.ToString("N0"), FontSize = 11, Opacity = 0.75,
            Foreground = ThemeBrushes.Get("MutedBrush"), VerticalAlignment = VerticalAlignment.Center,
        });

        var pill = new Button
        {
            Content = content,
            Padding = new Thickness(12, 5, 12, 5),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            // Your tags: brand outline on the input fill; .nfo tags: a quiet outline, like the movie window's
            Background = t.Yours ? ThemeBrushes.Get("InputBgBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderBrush = ThemeBrushes.Get(t.Yours ? "BrandPurpleBrush" : "BorderBrush"),
            Foreground = ThemeBrushes.Get(t.Yours ? "TextBrush" : "MutedBrush"),
        };
        ToolTipService.SetToolTip(pill, TagTooltip(t));
        pill.Click += (_, _) => { if (App.MainWindow is MainWindow mw) mw.NavigateLibraryByTag(t.Id, t.Name); };

        var menu = new MenuFlyout();
        var pin = new MenuFlyoutItem
        {
            Text = pinned ? "Unpin from sidebar" : "Pin to sidebar",
            Icon = new FontIcon { Glyph = pinned ? "" : "" },
        };
        pin.Click += (_, _) => { SidebarPrefs.SetPinned(t.Name, !pinned); Changed(); };
        menu.Items.Add(pin);
        if (t.Yours)
        {
            var rename = new MenuFlyoutItem { Text = "Rename…", Icon = new FontIcon { Glyph = "" } };
            rename.Click += async (_, _) => await RenameAsync(t);
            var delete = new MenuFlyoutItem { Text = "Delete…", Icon = new FontIcon { Glyph = "" } };
            delete.Click += async (_, _) => await DeleteAsync(t);
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(rename);
            menu.Items.Add(delete);
        }
        pill.ContextFlyout = menu;
        return pill;
    }

    private static string TagTooltip(DatabaseService.TagSummary t)
    {
        var parts = new List<string>();
        if (t.MovieCount > 0) parts.Add(Plural(t.MovieCount, "movie"));
        if (t.ShowCount > 0) parts.Add(Plural(t.ShowCount, "show"));
        return string.Join(" · ", parts);
    }

    private async Task RenameAsync(DatabaseService.TagSummary t)
    {
        var box = new TextBox { Text = t.Name, SelectionStart = 0, SelectionLength = t.Name.Length };
        var dialog = new ContentDialog
        {
            Title = "Rename tag",
            Content = box,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = MainWindow.CurrentTheme,
        };
        box.Loaded += (_, _) => box.Focus(FocusState.Programmatic);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var name = box.Text.Trim();
        if (name.Length == 0 || name == t.Name) return;

        AppState.Instance.Db.RenameOwnTag(t.Id, name);
        SidebarPrefs.RenamePinned(t.Name, name);
        Changed();
    }

    private async Task DeleteAsync(DatabaseService.TagSummary t)
    {
        var dialog = new ContentDialog
        {
            Title = $"Delete the tag “{t.Name}”?",
            Content = new TextBlock
            {
                Text = "It's taken off every movie and show you gave it to. Tags from .nfo files stay, and no files are changed.",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = MainWindow.CurrentTheme,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        AppState.Instance.Db.DeleteOwnTag(t.Id);
        // Still pinned only while an .nfo file has the tag
        if (!AppState.Instance.Db.GetAllTags().Any(x => x.Total > 0 && x.Name.Equals(t.Name, StringComparison.OrdinalIgnoreCase)))
            SidebarPrefs.SetPinned(t.Name, false);
        Changed();
    }

    private void Changed()
    {
        Load();
        if (App.MainWindow is MainWindow mw) mw.RefreshSidebar();
    }

    private void OnShowClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string show }) return;
        _show = show;
        foreach (var b in new[] { ShowAll, ShowYours, ShowNfo })
            b.Style = (Style)Application.Current.Resources[ReferenceEquals(b, sender) ? "PillButtonActiveStyle" : "PillButtonStyle"];
        Draw();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) { if (_ready) Draw(); }

    private void OnSortChanged(object sender, SelectionChangedEventArgs e) { if (_ready) Draw(); }

    private static string Plural(int n, string word) => $"{n:N0} {word}{(n == 1 ? "" : "s")}";
}
