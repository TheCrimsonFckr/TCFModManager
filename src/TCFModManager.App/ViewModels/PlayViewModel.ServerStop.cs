using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;

namespace TCFModManager.App.ViewModels;

//
// OPEN-45 on the server card: the countdown line while ServerStopper is about to stop the server,
// and Keep it running. The setting itself is in Options - Starting SPT.
//
public partial class PlayViewModel
{
    private bool _serverStopperHooked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasServerStopCountdown))]
    private string? _serverStopCountdown;

    public bool HasServerStopCountdown => ServerStopCountdown is not null;

    private void RefreshServerStop()
    {
        if (!_serverStopperHooked)
        {
            _serverStopperHooked = true;
            ServerStopper.Changed += (_, _) => UpdateServerStopCountdown();
            ServerStopper.Stopped += (_, e) =>
            {
                HasError = e.IsError;
                Message = e.Message;
                Refresh();
            };
        }

        UpdateServerStopCountdown();
    }

    private void UpdateServerStopCountdown()
    {
        if (ServerStopper.WaitingForHeadless)
        {
            ServerStopCountdown = Strings.Play_ServerStopWaitsForHeadless;
            return;
        }

        if (ServerStopper.StopsAt is not { } at)
        {
            ServerStopCountdown = null;
            return;
        }

        var left = Math.Max(0, (int)Math.Ceiling((at - DateTime.UtcNow).TotalSeconds));
        ServerStopCountdown = Text(Strings.Play_ServerStopCountdownFormat, left);
    }

    [RelayCommand]
    private static void KeepServerRunning() => ServerStopper.KeepRunning();
}
