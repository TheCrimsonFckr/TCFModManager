using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// Quick play (OPEN-43): from anywhere, the server and the last used profile. With a usable Play card
// it is PLAY for the picked profile; without one, the server and then the SPT launcher, which logs
// into its own last profile.
//
public partial class PlayViewModel
{
    private readonly DispatcherTimer _quickPoll = new() { Interval = TimeSpan.FromSeconds(5) };

    private bool _quickStarted;

    private static readonly HashSet<string> QuickInputs =
    [
        nameof(IsDirectLaunchOn), nameof(SelectedProfile), nameof(Server), nameof(Client), nameof(IsPlaying),
        nameof(IsWaitingForServer), nameof(IsGameRunning), nameof(PlayStatus), nameof(Message),
    ];

    public bool QuickPlaysDirect => IsDirectLaunchOn && SelectedProfile is not null;

    public string QuickPlayLabel => QuickPlaysDirect
        ? Text(Strings.QuickPlay_PlayAsFormat, SelectedProfile!.Title)
        : Strings.QuickPlay_ServerAndLauncher;

    public bool CanQuickPlay =>
        Server?.Exists == true && !IsPlaying && !IsWaitingForServer && !IsGameRunning
        && (QuickPlaysDirect || Client?.CanLaunch == true);

    // What a quick play is doing, for a button away from the Play card. Null when it is not running.
    public string? QuickPlayStatus => IsPlaying ? PlayStatus : IsWaitingForServer ? Message : null;

    // The button's second line: what it is doing, or what pressing it will do.
    public string QuickPlayLine => QuickPlayStatus ?? QuickPlayLabel;

    //
    // Called once by whatever shows the button. Keeps the state the button reads current while no
    // page that shows this is on screen - the server, the launcher and the game only, with the
    // profiles read once; the pages' own poll does the rest while one is open.
    //
    public void StartQuickPolling()
    {
        if (_quickStarted) return;
        _quickStarted = true;

        _quickPoll.Tick += (_, _) =>
        {
            if (_pollUsers == 0) RefreshQuick(readProfiles: Profiles.Count == 0);
        };

        RefreshQuick(readProfiles: true);
        _quickPoll.Start();
    }

    private void RefreshQuick(bool readProfiles)
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        var settings = new SettingsService().Load();

        Server = SptLaunchService.Describe(installPath, SptLaunchTarget.Server);
        Client = SptLaunchService.Describe(installPath, SptLaunchTarget.Client);

        RefreshGame(installPath, settings);
        RefreshDirect(installPath, settings, readProfiles);
    }

    [RelayCommand(CanExecute = nameof(CanQuickPlay))]
    private async Task QuickPlayAsync()
    {
        Refresh();
        if (!CanQuickPlay) return;

        if (QuickPlaysDirect)
        {
            AppLog.Info("Launch", $"quick play as {SelectedProfile!.Title}");
            await PlayAsync();
            if (PlayStatusIsError) AppNavigation.ShowPlayProblem();
            return;
        }

        AppLog.Info("Launch", "quick play: server and launcher");

        if (Server?.IsRunning == true) Start(SptLaunchTarget.Client);
        else await StartServerAndWaitAsync(new SettingsService().Load(), openLauncher: true);

        if (HasError) AppNavigation.ShowPlayProblem();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName is not { } name || !QuickInputs.Contains(name)) return;

        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(QuickPlaysDirect)));
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(QuickPlayLabel)));
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(CanQuickPlay)));
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(QuickPlayStatus)));
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(QuickPlayLine)));
        QuickPlayCommand.NotifyCanExecuteChanged();
    }
}
