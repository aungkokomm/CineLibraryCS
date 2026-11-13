using System.ComponentModel;
using CineLibraryCS.Models;
using CineLibraryCS.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace CineLibraryCS.Views;

/// <summary>5.4.0: a film that isn't in the library (see TmdbFilmItem).</summary>
public sealed partial class TmdbFilmCard : UserControl
{
    public static readonly DependencyProperty ItemProperty =
        DependencyProperty.Register(nameof(Item), typeof(TmdbFilmItem), typeof(TmdbFilmCard),
            new PropertyMetadata(null, (d, e) => ((TmdbFilmCard)d).OnItemChanged(e.OldValue as TmdbFilmItem)));

    public TmdbFilmItem? Item
    {
        get => (TmdbFilmItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public TmdbFilmCard()
    {
        InitializeComponent();
        var menu = new MenuFlyout();
        menu.Opening += (_, _) => FillMenu(menu);
        Root.ContextFlyout = menu;
    }

    private void OnItemChanged(TmdbFilmItem? old)
    {
        if (old != null) old.PropertyChanged -= OnItemPropertyChanged;
        if (Item is not { } item) return;
        item.PropertyChanged += OnItemPropertyChanged;
        TitleText.Text = PlaceholderTitle.Text = item.Film.Title;
        SubtitleText.Text = item.Subtitle;
        PosterImage.Source = item.Poster;
        OwnedBadge.Visibility = item.Owned ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(PosterBtn, $"{item.Film.Title}, {item.Subtitle}, open on TMDb");
        ShowWished();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TmdbFilmItem.Wished)) ShowWished();
    }

    private void ShowWished()
    {
        var wished = Item?.Wished == true;
        WishIcon.Glyph = wished ? "\uE73E" : "\uE710";   // a tick, or +
        WishIcon.Foreground = wished ? (Brush)Application.Current.Resources["BrandPurpleBrush"]
                                     : ThemeBrushes.Get("MutedBrush");
        var tip = wished ? "On your wishlist (click to take it off)" : "Add to wishlist";
        ToolTipService.SetToolTip(WishBtn, tip);
        AutomationProperties.SetName(WishBtn, tip);
    }

    private void OnWishClick(object sender, RoutedEventArgs e) => ToggleWished();

    private void ToggleWished()
    {
        if (Item is not { } item) return;
        if (!item.Wished)
        {
            Wishlist.Add(item.Film, item.From);
            item.Wished = true;
            return;
        }
        Wishlist.Remove(new[] { item.Film.TmdbId });
        item.Wished = false;
        var (film, from) = (item.Film, item.From);
        (App.MainWindow as MainWindow)?.ShowToastWithUndo($"Took {film.Title} off your wishlist", () =>
        {
            Wishlist.Add(film, from);
            item.Wished = true;
        });
    }

    private void OnPosterClick(object sender, RoutedEventArgs e) => OpenOnTmdb();

    private async void OpenOnTmdb()
    {
        if (Item is not { } item) return;
        try { await Launcher.LaunchUriAsync(new Uri($"https://www.themoviedb.org/movie/{item.Film.TmdbId}")); } catch { }
    }

    private void FillMenu(MenuFlyout menu)
    {
        menu.Items.Clear();
        if (Item is not { } item) return;
        var wish = new MenuFlyoutItem
        {
            Text = item.Wished ? "Take off the wishlist" : "Add to wishlist",
            Icon = new FontIcon { Glyph = item.Wished ? "\uE711" : "\uE710" },
        };
        wish.Click += (_, _) => ToggleWished();
        var open = new MenuFlyoutItem { Text = "Open on TMDb", Icon = new FontIcon { Glyph = "\uE8A7" } };
        open.Click += (_, _) => OpenOnTmdb();
        var copy = new MenuFlyoutItem { Text = "Copy title", Icon = new FontIcon { Glyph = "\uE8C8" } };
        copy.Click += (_, _) => CopyText(Wishlist.Line(item.Film));
        menu.Items.Add(wish);
        menu.Items.Add(open);
        menu.Items.Add(copy);
    }

    public static void CopyText(string text)
    {
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(text);
        try { Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package); } catch { }
    }

    // Asks for the S / M / L / XL size and fills the grid cell, as MovieCardControl does
    // (a long title would otherwise widen every cell of the grid).
    protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? MovieCardControl.GlobalCardWidth : availableSize.Width;
        base.MeasureOverride(new Windows.Foundation.Size(width, MovieCardControl.GlobalCardHeight));
        return new Windows.Foundation.Size(MovieCardControl.GlobalCardWidth, MovieCardControl.GlobalCardHeight);
    }
}
