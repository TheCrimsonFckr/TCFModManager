using System.Windows;
using Wpf.Ui.Controls;
using TCFModManager.App.Localization;

namespace TCFModManager.App.Views;

public enum ServerRunningChoice
{
    // Closing: don't close after all. Opening: not offered.
    Cancel,

    // Leave the server up - close anyway, or carry on with it running.
    Leave,

    // Stop the server (as Stop on the Play page does), then carry on.
    Stop,

    // Opening only: go to the Play page, where its log and Stop are.
    ShowPlay,
}

//
// This install's SPT server is running - asked as the app closes (it keeps running without the app,
// and with no window of its own nothing else says so), and said as the app opens on a server that
// is already up: left running last time, or started some other way.
//
public partial class ServerStillRunningWindow : FluentWindow
{
    private readonly bool _closing;

    private ServerStillRunningWindow(bool closing, string serverPath, bool hidden, bool canCancel)
    {
        _closing = closing;
        InitializeComponent();
        DialogFit.Attach(this);

        // Quitting from the tray, or the app ending some other way, can't be called off.
        if (closing && !canCancel)
        {
            CancelButton.Visibility = Visibility.Collapsed;
            CancelButton.IsCancel = false;
        }

        PathText.Text = serverPath;

        if (closing)
        {
            MessageText.Text = hidden ? Strings.ServerRunning_ClosingHiddenAsk : Strings.ServerRunning_ClosingAsk;
            StopButton.Content = Strings.ServerRunning_StopAndClose;
            LeaveButton.Content = Strings.ServerRunning_LeaveAndClose;
        }
        else
        {
            MessageText.Text = Strings.ServerRunning_AlreadyRunning;
            StopButton.Content = Strings.ServerRunning_Stop;
            LeaveButton.Content = Strings.ServerRunning_ShowPlay;
            CancelButton.Content = Strings.ServerRunning_LeaveRunning;
        }

        var owner = Application.Current?.MainWindow;
        Owner = owner is { IsVisible: true } ? owner : null;
        WindowStartupLocation = Owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;

        // Asked from the tray, with the main window hidden: on top, and in the taskbar, so it
        // can't open unseen behind whatever is in front.
        if (Owner is null)
        {
            Topmost = true;
            ShowInTaskbar = true;
        }
    }

    /// <param name="closing">True as the app closes, false as it opens.</param>
    /// <param name="hidden">The server has no window of its own (Options, Starting SPT).</param>
    /// <param name="canCancel">Closing only: whether Cancel is offered.</param>
    public static ServerRunningChoice Ask(bool closing, string serverPath, bool hidden, bool canCancel = true)
    {
        var window = new ServerStillRunningWindow(closing, serverPath, hidden, canCancel);
        window.ShowDialog();
        return window.Choice;
    }

    private ServerRunningChoice Choice { get; set; } = ServerRunningChoice.Cancel;

    // Opening: "Leave it running" sits where Cancel does, and is what closing the window means.
    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        Close(_closing ? ServerRunningChoice.Cancel : ServerRunningChoice.Leave);

    private void Stop_Click(object sender, RoutedEventArgs e) => Close(ServerRunningChoice.Stop);

    private void Leave_Click(object sender, RoutedEventArgs e) =>
        Close(_closing ? ServerRunningChoice.Leave : ServerRunningChoice.ShowPlay);

    private void Close(ServerRunningChoice choice)
    {
        Choice = choice;
        DialogResult = true;
    }
}
