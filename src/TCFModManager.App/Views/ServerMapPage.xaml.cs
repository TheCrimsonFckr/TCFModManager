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
}
