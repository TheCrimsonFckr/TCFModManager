using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Services;
using TCFModManager.Core.ServerMap;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// Where the Server Map server is, whether it answered, and what it said.
//
// A shared singleton for the same reason FootprintGateViewModel is one: the sidebar item, the
// Options section and the page itself are three views of a single connection, and connecting from
// Options has to move the nav item at that moment rather than at the next launch.
//
// Unlike the other gates, this one is not a stored on/off switch. There is no "enable Server Map"
// toggle to get out of step with whether it works - the feature appears when a server answers and
// stays out of the way when none does.
//
public sealed partial class ServerMapGateViewModel : ObservableObject
{
    // Every write does its own Load first, so this never fights the other things that save settings.
    private readonly SettingsService _settings = new();

    [ObservableProperty]
    private string? _hostInput;

    //
    // A string rather than an int, because this is bound to a text box that is mid-edit for as long
    // as someone is typing in it. An int binding rejects the empty box and the half-typed value,
    // leaving the user fighting the control; parsing once, on connect, reports a bad port as a
    // problem like any other.
    //
    [ObservableProperty]
    private string _portInput = ServerMapEndpoint.DefaultPort.ToString();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusMessage))]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(ServerName))]
    [NotifyPropertyChangedFor(nameof(ServerDetail))]
    [NotifyPropertyChangedFor(nameof(HasCertificateChanged))]
    [NotifyCanExecuteChangedFor(nameof(TrustNewCertificateCommand))]
    private ServerHelloProbe? _probe;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinDescription))]
    [NotifyCanExecuteChangedFor(nameof(ForgetPinCommand))]
    private string? _pinnedThumbprint;

    //
    // Whether the Server Map item is in the sidebar.
    //
    // Latched on the first successful handshake and held for the rest of the session rather than
    // tracking the connection: a server that restarts mid-session would otherwise pull the page out
    // from under whoever was reading it, and "the server went away" is something the page can say
    // far better than an empty gap in the sidebar can. Cleared only when the address is, which is
    // the one case where the user has actually said they are done with it.
    //
    [ObservableProperty]
    private bool _isPageEnabled;

    public ServerMapGateViewModel()
    {
        var stored = _settings.Load().ServerMap;

        _hostInput = stored.Host;
        _portInput = stored.Port.ToString();
        _pinnedThumbprint = stored.PinnedThumbprint;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(HostInput);

    // Emptying the box has to change the resting line under it straight away, before anything is
    // clicked - otherwise it still reads "not connected yet" for an address that no longer exists.
    partial void OnHostInputChanged(string? value)
    {
        OnPropertyChanged(nameof(IsConfigured));
        OnPropertyChanged(nameof(StatusMessage));
    }

    public bool IsConnected => Probe?.Found == true;

    public string StatusMessage => ServerMapProblems.DescribeState(Probe, IsConfigured);

    // Named to match the shared ErrorText style in App.xaml, which binds to it.
    public bool HasError => Probe is not null && !Probe.Found;

    public string PinDescription => ServerMapProblems.DescribePin(PinnedThumbprint);

    // The one failure the user is asked to make a judgement about, rather than just told about.
    public bool HasCertificateChanged => Probe?.Problem == ServerMapProblem.CertificateRejected;

    public string ServerName => Probe?.Hello is null
        ? ""
        : string.IsNullOrWhiteSpace(Probe.Hello.ServerName) ? Probe.Endpoint.Host : Probe.Hello.ServerName!;

    //
    // The line under the server's name on the page. Says what the mod can currently do rather than
    // only what it is, because "no mod list published" is the difference between a map that is
    // working and one that has nothing to show.
    //
    public string ServerDetail
    {
        get
        {
            if (Probe?.Hello is not { } hello) return "";

            var parts = new List<string> { $"{Probe.Endpoint.Host}:{Probe.Endpoint.Port}" };

            if (!string.IsNullOrWhiteSpace(hello.SptVersion)) parts.Add($"SPT {hello.SptVersion}");
            if (!string.IsNullOrWhiteSpace(hello.ModVersion)) parts.Add($"Server Map {hello.ModVersion}");

            parts.Add(hello.HasList
                ? $"publishing a mod list (revision {hello.ListRevision?.ToString() ?? "unknown"})"
                : "no mod list published yet");

            return string.Join("  -  ", parts);
        }
    }

    //
    // Called from MainWindow once the window is up. Fire-and-forget: whether a server answers has no
    // bearing on the app opening, and a server that is off just leaves the item out of the sidebar.
    //
    public async Task ConnectOnStartupAsync()
    {
        if (!IsConfigured) return;

        await ConnectAsync();
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        IsBusy = true;

        try
        {
            var endpoint = SaveAndBuildEndpoint();

            if (string.IsNullOrWhiteSpace(endpoint.Host))
            {
                // Clearing the address is the one thing that takes the page back out of the sidebar.
                IsPageEnabled = false;
                Probe = new ServerHelloProbe { Endpoint = endpoint, Problem = ServerMapProblem.NoAddress };
                return;
            }

            using var client = ServerMapClient.TryCreate(endpoint);

            if (client is null)
            {
                Probe = new ServerHelloProbe { Endpoint = endpoint, Problem = ServerMapProblem.InvalidAddress };
                return;
            }

            var probe = await client.HelloAsync();

            //
            // Trust on first use, recorded the moment it happens. Saved before Probe is set so the
            // Options page never shows "connected, recorded its certificate" beside an empty pin.
            //
            if (probe.Found && probe.PinnedOnThisConnection && !string.IsNullOrWhiteSpace(probe.ActualThumbprint))
            {
                SavePin(probe.ActualThumbprint);
                AppLog.Info("ServerMap", $"pinned {endpoint.Host} to {ServerMapProblems.Short(probe.ActualThumbprint)}");
            }

            Probe = probe;

            if (probe.Found) IsPageEnabled = true;

            AppLog.Info("ServerMap", probe.Found
                ? $"connected to {endpoint.Host}:{endpoint.Port}"
                : $"{endpoint.Host}:{endpoint.Port} - {probe.Problem}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanConnect() => !IsBusy;

    //
    // Records the certificate the server is presenting now, replacing the one that was pinned, and
    // reconnects. Only reachable from the mismatch message, which names both fingerprints - this is
    // the user answering it, not a retry that quietly gives up on checking.
    //
    [RelayCommand(CanExecute = nameof(HasCertificateChanged))]
    private async Task TrustNewCertificateAsync()
    {
        if (Probe?.ActualThumbprint is not { } presented) return;

        AppLog.Info("ServerMap",
            $"certificate for {Probe.Endpoint.Host} replaced: {ServerMapProblems.Short(PinnedThumbprint)}"
            + $" -> {ServerMapProblems.Short(presented)}");

        SavePin(presented);

        await ConnectAsync();
    }

    //
    // Throws the pin away and re-arms trust-on-first-use, so the next connection records whatever
    // is there. The honest way out for someone who has moved their server rather than been attacked.
    //
    [RelayCommand(CanExecute = nameof(HasPin))]
    private void ForgetPin()
    {
        SavePin(null);
        AppLog.Info("ServerMap", "pinned certificate cleared");
    }

    private bool HasPin() => !string.IsNullOrWhiteSpace(PinnedThumbprint);

    // Persists what is in the boxes and hands back the endpoint to dial. An unparseable port is kept
    // as typed and passed through as 0, which TryGetBaseUri refuses and Options reports.
    private ServerMapEndpoint SaveAndBuildEndpoint()
    {
        var host = HostInput?.Trim();
        var port = int.TryParse(PortInput?.Trim(), out var parsed) ? parsed : 0;

        var settings = _settings.Load();
        settings.ServerMap.Host = string.IsNullOrWhiteSpace(host) ? null : host;
        settings.ServerMap.Port = port;
        _settings.Save(settings);

        OnPropertyChanged(nameof(IsConfigured));

        return new ServerMapEndpoint(host ?? string.Empty, port, PinnedThumbprint);
    }

    private void SavePin(string? thumbprint)
    {
        var settings = _settings.Load();
        settings.ServerMap.PinnedThumbprint = thumbprint;
        _settings.Save(settings);

        PinnedThumbprint = thumbprint;
    }
}
