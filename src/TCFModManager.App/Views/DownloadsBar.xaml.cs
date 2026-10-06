using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TCFModManager.App.Views;

// OPEN-12 A7: the downloads bar - see DownloadsBar.xaml. A click opens the Downloads page.
public partial class DownloadsBar : UserControl
{
    public DownloadsBar()
    {
        InitializeComponent();
    }

    private void Face_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        AppNavigation.Navigate(typeof(DownloadsPage));

    private void Face_MouseEnter(object sender, MouseEventArgs e) =>
        Face.SetResourceReference(Border.BackgroundProperty, "ControlFillColorSecondaryBrush");

    private void Face_MouseLeave(object sender, MouseEventArgs e) =>
        Face.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
}
