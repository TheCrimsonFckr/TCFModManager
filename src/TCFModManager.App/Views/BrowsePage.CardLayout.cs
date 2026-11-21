using System.ComponentModel;
using System.Windows;
using TCFModManager.App.Behaviors;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

public partial class BrowsePage
{
    private void HookCardLayout()
    {
        Loaded += (_, _) =>
        {
            CardDensity.Apply(ResultsListBox.Resources, ViewModel.CardLayout.Size);
            ViewModel.PropertyChanged -= OnCardLayoutChanged;
            ViewModel.PropertyChanged += OnCardLayoutChanged;
        };
        Unloaded += (_, _) => ViewModel.PropertyChanged -= OnCardLayoutChanged;
    }

    private void OnCardLayoutChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BrowseViewModel.CardLayout)) CardDensity.Apply(ResultsListBox.Resources, ViewModel.CardLayout.Size);
    }
}
