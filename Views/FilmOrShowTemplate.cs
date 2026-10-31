using CineLibraryCS.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CineLibraryCS.Views;

/// <summary>4.10.0: picks a film's or a show's template, for a list that holds both.</summary>
public sealed class FilmOrShowTemplate : DataTemplateSelector
{
    public DataTemplate? Film { get; set; }
    public DataTemplate? Show { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item is TvShowListItem ? Show : Film;
}
