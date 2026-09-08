using System.Windows;
using System.Windows.Controls;

namespace TCFModManager.App.Views;

//
// The Server Map page. It has no ViewModel of its own: everything on it is the shared connection in
// AppServices.ServerMap, which the sidebar item and the Options section also read, and a second
// object here would be a second answer to the same question.
//
// DataContext is set in XAML rather than here, so the designer shows real bindings.
//
public partial class ServerMapPage : Page
{
    public ServerMapPage() => InitializeComponent();

    //
    // Picks up this machine's own server key every time the page is shown.
    //
    // Reading it once at startup was not enough: the key file appears the first time the server mod
    // runs, which is usually after this app was opened, and it changes again whenever the key is
    // rotated. Checking on show means an operator never restarts the app to see their own key.
    //
    private void Page_Loaded(object sender, RoutedEventArgs e) => AppServices.ServerMap.RefreshLocalKey();
}
