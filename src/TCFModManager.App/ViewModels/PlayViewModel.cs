using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// The Play page: start this install's server, its launcher, and - on a headless setup - its Fika
// headless launcher, and say which of them is already up. Nothing here stops anything - see
// SptLaunchService.
//
public partial class PlayViewModel : ObservableObject
{
    //
    // All three targets are started outside this app, so there is nothing to await and no event to
    // subscribe to - a poll is the only way the buttons can tell that the server came up, or that
    // the game was closed from somewhere else. Runs only while the page is on screen.
    //
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(2) };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerState))]
    [NotifyPropertyChangedFor(nameof(ServerPath))]
    [NotifyPropertyChangedFor(nameof(CanStartServer))]
    private SptLaunchTargetInfo? _server;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClientState))]
    [NotifyPropertyChangedFor(nameof(ClientPath))]
    [NotifyPropertyChangedFor(nameof(CanStartClient))]
    private SptLaunchTargetInfo? _client;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeadlessState))]
    [NotifyPropertyChangedFor(nameof(HeadlessPath))]
    [NotifyPropertyChangedFor(nameof(CanStartHeadless))]
    [NotifyPropertyChangedFor(nameof(HasHeadless))]
    private SptLaunchTargetInfo? _headless;

    // The result of the last button press, cleared the next time one is pressed.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = "";

    // Named to match the shared ErrorText style in App.xaml, which binds to it.
    [ObservableProperty]
    private bool _hasError;

    public PlayViewModel()
    {
        _poll.Tick += (_, _) => Refresh();

        // The folder is set on the Options page, and this page has to notice when it changes
        // rather than showing what it found the first time it was opened.
        AppServices.SptEnvironment.PropertyChanged += OnEnvironmentChanged;

        Refresh();
    }

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    public string ServerState => Server is null ? "" : SptLaunchProblems.DescribeState(Server);

    public string ClientState => Client is null ? "" : SptLaunchProblems.DescribeState(Client);

    public string HeadlessState => Headless is null ? "" : SptLaunchProblems.DescribeState(Headless);

    public string ServerPath => Server?.ExePath ?? "";

    public string ClientPath => Client?.ExePath ?? "";

    public string HeadlessPath => Headless?.ExePath ?? "";

    public bool CanStartServer => Server?.CanLaunch == true;

    public bool CanStartClient => Client?.CanLaunch == true;

    public bool CanStartHeadless => Headless?.CanLaunch == true;

    //
    // Whether the headless card belongs on this page.
    //
    // The launcher on disk is the usual answer - only a setup running a headless client has one, so
    // on every other install the card stays off the page rather than showing a dead button for
    // something that was never installed. The setting is the second way in, for a machine that
    // starts its headless some other way: it has told the app what it is, and hiding the card would
    // contradict that.
    //
    public bool HasHeadless => Headless?.Exists == true || RunsHeadlessClient;

    // What Options has been told this machine is - see AppSettings.PlaysHere / RunsHeadlessClient.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHeadless))]
    [NotifyPropertyChangedFor(nameof(RoleSummary))]
    [NotifyPropertyChangedFor(nameof(NeedsRoleAnswer))]
    private bool _runsHeadlessClient;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoleSummary))]
    private bool _playsHere = true;

    //
    // Shown on the card because it is the thing that decides what a served mod list brings to this
    // machine, and the Play page is where somebody stands when that matters.
    //
    public string RoleSummary => (PlaysHere, RunsHeadlessClient) switch
    {
        (false, true) => "Set up as a dedicated headless: a mod list from a server arrives without"
            + " the mods only a player would need.",
        (true, true) => "Set up as a machine that both plays and hosts, so a served mod list arrives"
            + " whole.",
        _ => "",
    };

    //
    // A launcher is here but nobody has said what the machine is, so a served list is being filtered
    // as though this were an ordinary player - which is the safe reading, and probably not the right
    // one on a box with a headless launcher in it.
    //
    // The prompt for this fires when the install folder is set, which someone who set theirs months
    // ago will never do again. This is how they find out there is a question to answer.
    //
    public bool NeedsRoleAnswer => Headless?.Exists == true && !RunsHeadlessClient;

    // Called by the page, so the poll only runs while it is the visible page.
    public void StartPolling()
    {
        Refresh();
        _poll.Start();
    }

    public void StopPolling() => _poll.Stop();

    [RelayCommand]
    private void Refresh()
    {
        var installPath = AppServices.SptEnvironment.InstallPath;

        Server = SptLaunchService.Describe(installPath, SptLaunchTarget.Server);
        Client = SptLaunchService.Describe(installPath, SptLaunchTarget.Client);
        Headless = SptLaunchService.Describe(installPath, SptLaunchTarget.Headless);

        // Re-read rather than cached, because Options can change it while this page is open and the
        // poll is already running.
        var roles = new SettingsService().Load().Roles;
        PlaysHere = roles.HasFlag(InstallRoles.Player);
        RunsHeadlessClient = roles.HasFlag(InstallRoles.Headless);
    }

    [RelayCommand]
    private void StartServer() => Start(SptLaunchTarget.Server);

    [RelayCommand]
    private void StartClient() => Start(SptLaunchTarget.Client);

    [RelayCommand]
    private void StartHeadless() => Start(SptLaunchTarget.Headless);

    private void Start(SptLaunchTarget target)
    {
        var result = SptLaunchService.Launch(AppServices.SptEnvironment.InstallPath, target);

        HasError = !result.Started;
        Message = result.Started
            ? $"Started {result.Info.ProcessName}."
            : SptLaunchProblems.Describe(result);

        Refresh();
    }

    private void OnEnvironmentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SptEnvironmentViewModel.InstallPath)) Refresh();
    }
}
