using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

// Hosts the addon list inside a mod's details dialog. DataContext is an AddonsSectionViewModel,
// set by the hosting dialog.
public partial class AddonsSection : UserControl
{
    public AddonsSection()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is AddonsSectionViewModel old) old.FocusRequested -= OnFocusRequested;
        if (e.NewValue is AddonsSectionViewModel now) now.FocusRequested += OnFocusRequested;
    }

    // OPEN-36 D3: scrolls the dialog to the addon a Browse search opened it on, once its row exists.
    private void OnFocusRequested(object? sender, AddonRowViewModel row) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            (AddonsList.ItemContainerGenerator.ContainerFromItem(row) as FrameworkElement)?.BringIntoView());
}
