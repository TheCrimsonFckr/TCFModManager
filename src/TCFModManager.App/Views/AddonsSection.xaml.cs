using System.Windows.Controls;

namespace TCFModManager.App.Views;

// Hosts the addon list inside a mod's details dialog. DataContext is an AddonsSectionViewModel,
// set by the hosting dialog.
public partial class AddonsSection : UserControl
{
    public AddonsSection()
    {
        InitializeComponent();
    }
}
