using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;

namespace TCFModManager.App.ViewModels;

//
// OPEN-45 in Options - Starting SPT: the same switch and delay as the server card (ServerStopper).
//
public partial class OptionsViewModel
{
    private bool _readingServerStop;

    private bool _serverStopHooked;

    [ObservableProperty]
    private bool _stopServerWithGame;

    [ObservableProperty]
    private double? _stopServerDelay = ServerStopper.DefaultSeconds;

    [ObservableProperty]
    private bool _stopServerIgnoresHeadless;

    // Only on an install with a headless, by the Play page's HasHeadless rule.
    public bool ShowStopServerHeadlessOverride => HasHeadlessLauncher || RunsHeadlessClient;

    public double StopServerMaxSeconds => ServerStopper.MaxSeconds;

    private void HookServerStop(AppSettings settings)
    {
        ReadServerStop(settings);

        if (_serverStopHooked) return;
        _serverStopHooked = true;

        ServerStopper.SettingChanged += (_, _) => ReadServerStop(_settings.Load());
    }

    private void ReadServerStop(AppSettings settings)
    {
        _readingServerStop = true;
        StopServerWithGame = settings.StopServerWithGame;
        StopServerDelay = ServerStopper.Clamp(settings.StopServerAfterGameSeconds);
        StopServerIgnoresHeadless = settings.StopServerIgnoresHeadless;
        _readingServerStop = false;
    }

    partial void OnStopServerWithGameChanged(bool value)
    {
        if (!_readingServerStop) ServerStopper.SetEnabled(value);
    }

    partial void OnStopServerIgnoresHeadlessChanged(bool value)
    {
        if (!_readingServerStop) ServerStopper.SetIgnoresHeadless(value);
    }

    partial void OnStopServerDelayChanged(double? value)
    {
        if (_readingServerStop) return;

        var seconds = ServerStopper.Clamp((int)Math.Round(value ?? ServerStopper.DefaultSeconds));
        ServerStopper.SetSeconds(seconds);

        if (value != seconds)
        {
            _readingServerStop = true;
            StopServerDelay = seconds;
            _readingServerStop = false;
        }
    }
}
