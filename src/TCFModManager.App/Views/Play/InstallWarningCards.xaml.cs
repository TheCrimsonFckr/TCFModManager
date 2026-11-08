using System.Windows.Controls;

namespace TCFModManager.App.Views.Play;

// The Profile page's install warnings: server mods that won't load, mods not for this SPT, and
// conflicts. DataContext is the page's PlayViewModel.
public partial class InstallWarningCards : UserControl
{
    public InstallWarningCards()
    {
        InitializeComponent();
    }
}
