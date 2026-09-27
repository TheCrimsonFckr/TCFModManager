using System.Windows;
using TCFModManager.App.Views;
using Wpf.Ui.Controls;

namespace TCFModManager.App;

//
// Getting to a page from outside the window - today, only an update notification's Open (§6),
// which goes to Installed with the Show filter on "Updates available".
//
// The request can arrive before there is anywhere to go: a click on a toast after the app has
// quit starts it, and the toast's arguments can land before the main window has loaded. So the
// request is held as a flag - the window opens on Installed when it's set, and the Installed page
// takes it and sets its filter, whether that page already exists or is built by this navigation.
//
internal static class AppNavigation
{
    private static NavigationView? _navigation;

    private static bool _showUpdatesPending;

    // Raised on the UI thread when an open Installed page should switch its filter to updates.
    public static event EventHandler? ShowUpdatesRequested;

    // True when the window should open on Installed rather than Browse.
    public static bool StartOnInstalled => _showUpdatesPending;

    // Called once the main window's navigation exists (MainWindow's Loaded).
    public static void Attach(NavigationView navigation) => _navigation = navigation;

    //
    // Must be called on the UI thread. Sets the request first, so an Installed page built by the
    // Navigate below reads it in its constructor; an existing one hears the event instead.
    //
    public static void ShowInstalledUpdates()
    {
        _showUpdatesPending = true;
        ShowUpdatesRequested?.Invoke(null, EventArgs.Empty);

        if (Application.Current?.MainWindow is { } window) WindowActivation.BringForward(window);

        _navigation?.Navigate(typeof(InstalledPage));
    }

    // Consumed by the Installed page: true once per request.
    public static bool TakeShowUpdates()
    {
        var pending = _showUpdatesPending;
        _showUpdatesPending = false;
        return pending;
    }
}
