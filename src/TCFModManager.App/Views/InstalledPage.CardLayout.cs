using System.ComponentModel;
using System.Windows;
using TCFModManager.App.Behaviors;
using TCFModManager.App.ViewModels;
using TCFModManager.Core.Models;

namespace TCFModManager.App.Views;

public partial class InstalledPage
{
    private void HookCardLayout()
    {
        CardDensity.Apply(RootGrid.Resources, ViewModel.CardLayout.Size);
        ViewModel.PropertyChanged += OnCardLayoutChanged;
    }

    private void OnCardLayoutChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstalledViewModel.CardLayout)) CardDensity.Apply(RootGrid.Resources, ViewModel.CardLayout.Size);
    }

    private void EditCardLayouts_Click(object sender, RoutedEventArgs e)
    {
        var sample = ViewModel.SampleCardForLayouts();
        var picked = CardLayoutEditorWindow.Edit(CardLayoutPage.Installed, ViewModel.CardLayout.Id, sample);
        if (picked is { } choice) ViewModel.UseCardLayout(choice.Id);
    }
}
