using System.Windows;
using System.Windows.Controls;

namespace TCFModManager.App.Views.Play;

// The Server page's Fika headless card, on headless setups only. DataContext is the page's
// PlayViewModel.
public partial class HeadlessCard : UserControl
{
    public HeadlessCard()
    {
        InitializeComponent();
    }

    private void RoleShowMeHow_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.ShowHelp("options", "options.role");
}
