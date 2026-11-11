using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// OPEN-45 on the server card: the switch and delay for stopping the server after the game closes
// (ServerStopper), the countdown line while it runs, and Keep it running.
//
public partial class PlayViewModel
{
    // Set from settings without saving them back.
    private bool _readingServerStopSetting;

    private bool _serverStopperHooked;

    [ObservableProperty]
    private bool _stopServerWithGame;

    [ObservableProperty]
    private double? _stopServerDelay = ServerStopper.DefaultSeconds;

    [ObservableProperty]
    private bool _stopServerIgnoresHeadless;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasServerStopCountdown))]
    private string? _serverStopCountdown;

    public bool HasServerStopCountdown => ServerStopCountdown is not null;

    public double StopServerMaxSeconds => ServerStopper.MaxSeconds;

    private void RefreshServerStop(AppSettings settings)
    {
        if (!_serverStopperHooked)
        {
            _serverStopperHooked = true;
            ServerStopper.Changed += (_, _) => UpdateServerStopCountdown();
            ServerStopper.SettingChanged += (_, _) => RefreshServerStop(new SettingsService().Load());
            ServerStopper.Stopped += (_, e) =>
            {
                HasError = e.IsError;
                Message = e.Message;
                Refresh();
            };
        }

        _readingServerStopSetting = true;
        StopServerWithGame = settings.StopServerWithGame;
        StopServerDelay = ServerStopper.Clamp(settings.StopServerAfterGameSeconds);
        StopServerIgnoresHeadless = settings.StopServerIgnoresHeadless;
        _readingServerStopSetting = false;

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

    partial void OnStopServerWithGameChanged(bool value)
    {
        if (!_readingServerStopSetting) ServerStopper.SetEnabled(value);
    }

    partial void OnStopServerIgnoresHeadlessChanged(bool value)
    {
        if (!_readingServerStopSetting) ServerStopper.SetIgnoresHeadless(value);
    }

    partial void OnStopServerDelayChanged(double? value)
    {
        if (_readingServerStopSetting) return;

        var seconds = ServerStopper.Clamp((int)Math.Round(value ?? ServerStopper.DefaultSeconds));
        ServerStopper.SetSeconds(seconds);

        if (value != seconds)
        {
            _readingServerStopSetting = true;
            StopServerDelay = seconds;
            _readingServerStopSetting = false;
        }
    }

    [RelayCommand]
    private static void KeepServerRunning() => ServerStopper.KeepRunning();
}
