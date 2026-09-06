using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Services;
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
    // Whether this install has a headless launcher at all. Only a setup running a headless client
    // has one, so on every other install the whole card stays off the page rather than showing a
    // dead button for something that was never installed.
    //
    public bool HasHeadless => Headless?.Exists == true;

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
