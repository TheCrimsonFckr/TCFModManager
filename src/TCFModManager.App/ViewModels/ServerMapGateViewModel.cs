using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.ServerMap;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// Where the Server Map server is, whether it answered, and what it said.
//
// A shared singleton for the same reason FootprintGateViewModel is one: the sidebar item, the
// Options section and the page itself are three views of one thing, and flicking the switch has to
// move the nav item at that moment rather than at the next launch.
//
// It carries two separate things, and keeping them apart is the point: whether the page is SHOWN
// (a stored switch the user owns, exactly like Mod footprint) and whether the server ANSWERED (the
// last handshake). Tying the sidebar to the handshake instead was the first cut, and it was wrong -
// a page that comes and goes with a server being up is a page nobody can find on purpose.
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
    [NotifyPropertyChangedFor(nameof(KeyDescription))]
    [NotifyCanExecuteChangedFor(nameof(TrustNewCertificateCommand))]
    private ServerHelloProbe? _probe;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinDescription))]
    [NotifyCanExecuteChangedFor(nameof(ForgetPinCommand))]
    private string? _pinnedThumbprint;

    //
    // The server's shared key. Saved on connect alongside the address, because the two are one
    // thing an operator hands out together and there is nothing to gain from making them two steps.
    //
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyDescription))]
    [NotifyPropertyChangedFor(nameof(CanUseLocalKey))]
    private string? _keyInput;

    //
    // The key belonging to a Server Map server running on THIS machine, when there is one. Null on
    // any machine that is not the server, which is most of them.
    //
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsServerOwner))]
    [NotifyPropertyChangedFor(nameof(LocalKeyDescription))]
    [NotifyPropertyChangedFor(nameof(CanUseLocalKey))]
    [NotifyCanExecuteChangedFor(nameof(UseLocalKeyCommand))]
    private string? _localKey;

    // The list this server publishes, as last fetched. Also in the user's mod lists - this is the
    // page's own handle on it, not a second copy of the truth.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasList))]
    [NotifyPropertyChangedFor(nameof(ListSummary))]
    [NotifyCanExecuteChangedFor(nameof(FetchListCommand))]
    private ModList? _list;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasListStatus))]
    private string _listStatus = "";

    //
    // Whether the Server map item is in the sidebar. Off by default and stored - see
    // ServerMapSettings.
    //
    // Deliberately NOT tied to whether the server is up. The page is worth reaching when the server
    // is down too, because that is exactly when someone wants to look at it, and a sidebar item that
    // appears and disappears on its own is one nobody can rely on finding.
    //
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SettingToolTip))]
    private bool _isPageEnabled;

    // Suppresses the save while the constructor is putting the switch where the stored value already
    // is, so starting the app doesn't count as flicking it.
    private readonly bool _loaded;

    public ServerMapGateViewModel()
    {
        var settings = _settings.Load();
        var stored = settings.ServerMap;

        _isPageEnabled = stored.ShowPage;
        _hostInput = stored.Host;
        _portInput = stored.Port.ToString();
        _pinnedThumbprint = stored.PinnedThumbprint;
        _keyInput = stored.SharedKey;

        RefreshLocalKey(settings.SptInstallPath);

        //
        // Filled in only when the box is empty. Someone running the app on their own server should
        // not have to go and find a file to paste back into the window in front of them - but a key
        // they typed themselves is never overwritten, because it may be for a different server.
        //
        if (string.IsNullOrWhiteSpace(_keyInput) && _localKey is not null) _keyInput = _localKey;

        _loaded = true;
    }

    //
    // Whether this machine is running a Server Map server. Not inferred from the address: 127.0.0.1,
    // a LAN address, a hostname and an external IP can all reach the same box, and the app cannot
    // tell. The presence of the key file is the honest test, and it is also the only one that
    // matters - it means the person at this keyboard already owns that file.
    //
    public bool IsServerOwner => LocalKey is not null;

    public bool CanUseLocalKey =>
        LocalKey is not null && !string.Equals(LocalKey, KeyInput?.Trim(), StringComparison.Ordinal);

    public string LocalKeyDescription => LocalKey is null
        ? ""
        : $"This machine runs a Server Map server. Its key is {LocalKey} - send it to whoever "
          + "should be able to see what this server publishes, along with the address and port.";

    // Re-read on demand: the file appears the first time the server mod starts, which is usually
    // after this app was opened.
    public void RefreshLocalKey(string? sptInstallPath = null) =>
        LocalKey = ServerMapKeyFile.TryReadLocal(sptInstallPath ?? _settings.Load().SptInstallPath);

    //
    // Puts this machine's own key in the box. Offered rather than forced, because the address might
    // legitimately point at somebody else's server from a machine that also runs one.
    //
    [RelayCommand(CanExecute = nameof(CanUseLocalKey))]
    private void UseLocalKey()
    {
        if (LocalKey is not null) KeyInput = LocalKey;
    }

    //
    // The switch's tooltip. Says what the page needs to be useful, because that is the part someone
    // deciding whether to switch it on cannot tell from the name: the mod goes on the SERVER, and
    // without it this page has nothing to show.
    //
    public string SettingToolTip => IsPageEnabled
        ? "The Server map page is in the sidebar. It only has anything to show once you point it at "
          + "an SPT server whose operator has installed the Server Map mod."
        : "Adds a page that connects to an SPT server running the Server Map mod and shows what it "
          + "runs, so you can compare it against your own install. Needs the mod on the server - "
          + "installing this app is not enough on its own.";

    //
    // No confirmation either way. Nothing is at stake in showing or hiding a page, and turning it
    // off does not throw away the address or the recorded certificate.
    //
    partial void OnIsPageEnabledChanged(bool value)
    {
        if (!_loaded) return;

        var settings = _settings.Load();
        settings.ServerMap.ShowPage = value;
        _settings.Save(settings);

        AppLog.Info("ServerMap", value ? "page shown" : "page hidden");
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

    public string KeyDescription =>
        ServerMapProblems.DescribeKey(!string.IsNullOrWhiteSpace(KeyInput), Probe?.Hello?.RequiresKey ?? false);

    public bool HasList => List is not null;

    public bool HasListStatus => !string.IsNullOrEmpty(ListStatus);

    //
    // What the held list is, in one line. Reads from the stored list rather than the handshake, so
    // it keeps saying something true after the server goes down.
    //
    public string ListSummary => List is null
        ? ""
        : $"{List.Name} - revision {List.Revision}, {List.Entries.Count} "
          + (List.Entries.Count == 1 ? "mod" : "mods")
          + (List.Unresolved.Any() ? $", {List.Unresolved.Count()} not on The Forge" : "");

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
    // bearing on the app opening, and a server that is off just leaves the page saying so.
    //
    public async Task ConnectOnStartupAsync()
    {
        // Switched off means switched off: no page, and no request to somebody's server either.
        if (!IsPageEnabled || !IsConfigured) return;

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

            AppLog.Info("ServerMap", probe.Found
                ? $"connected to {endpoint.Host}:{endpoint.Port}"
                : $"{endpoint.Host}:{endpoint.Port} - {probe.Problem}");

            if (probe.Hello is { } hello) await SyncListAsync(client, endpoint, hello, cancellationToken: default);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanConnect() => !IsBusy;

    //
    // Fetches the published list when it is worth fetching, and stores it.
    //
    // The handshake carries the revision precisely so this can be skipped: a list that has not moved
    // since the last fetch is not asked for again. Which revision is held comes from the stored
    // lists themselves, matched on the server address they carry as their Source - there is no
    // separate record to get out of step with what is actually in the store.
    //
    // Storing is not applying. A served list lands in the user's mod lists read-only and does
    // nothing until they apply it from that page, which shows what would change first. That
    // separation is the whole reason this feature serves a list rather than files.
    //
    private async Task SyncListAsync(ServerMapClient client, ServerMapEndpoint endpoint, ServerHello hello,
        CancellationToken cancellationToken)
    {
        var held = HeldListFor(endpoint);

        if (!hello.HasList)
        {
            List = held;
            ListStatus = ServerMapProblems.DescribeList(
                new ServerMapListResult { Endpoint = endpoint, Problem = ServerMapProblem.NoList }, held);
            return;
        }

        if (held is not null && hello.ListRevision is { } revision && revision <= held.Revision)
        {
            List = held;
            ListStatus = ServerMapProblems.DescribeList(
                new ServerMapListResult { Endpoint = endpoint, List = held }, held);
            return;
        }

        var result = await client.ListAsync(cancellationToken);

        if (result.List is { } fetched)
        {
            // Upserts by Id, so a newer revision of a list already held replaces it rather than
            // leaving two - see ModListStore.Add.
            AppServices.ModLists.Add(fetched);
            List = fetched;

            AppLog.Info("ServerMap",
                $"fetched list \"{fetched.Name}\" revision {fetched.Revision} ({fetched.Entries.Count} entries)");
        }
        else
        {
            // A list that would not come back does not throw away the one already held.
            List = held;
            AppLog.Info("ServerMap", $"list not fetched - {result.Problem}");
        }

        ListStatus = ServerMapProblems.DescribeList(result, held);
    }

    //
    // The newest list this install already holds from this server. Matched on Source, which a served
    // list carries as "host:port" - see ModListFile.Read.
    //
    private static ModList? HeldListFor(ServerMapEndpoint endpoint)
    {
        var source = $"{endpoint.Host}:{endpoint.Port}";

        return AppServices.ModLists.Load().Lists
            .Where(l => l.Origin == ModListOrigin.Server
                        && string.Equals(l.Source, source, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(l => l.Revision)
            .FirstOrDefault();
    }

    //
    // Fetches the list again whether or not the revision moved. For the case the automatic path
    // deliberately does not cover: the held copy was edited, deleted, or is simply doubted.
    //
    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task FetchListAsync()
    {
        IsBusy = true;

        try
        {
            var endpoint = SaveAndBuildEndpoint();

            using var client = ServerMapClient.TryCreate(endpoint);
            if (client is null) return;

            var result = await client.ListAsync();

            if (result.List is { } fetched)
            {
                AppServices.ModLists.Add(fetched);
                List = fetched;
            }

            ListStatus = ServerMapProblems.DescribeList(result, HeldListFor(endpoint));
        }
        finally
        {
            IsBusy = false;
        }
    }

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

        var key = KeyInput?.Trim();

        var settings = _settings.Load();
        settings.ServerMap.Host = string.IsNullOrWhiteSpace(host) ? null : host;
        settings.ServerMap.Port = port;
        settings.ServerMap.SharedKey = string.IsNullOrWhiteSpace(key) ? null : key;
        _settings.Save(settings);

        OnPropertyChanged(nameof(IsConfigured));

        return new ServerMapEndpoint(host ?? string.Empty, port, PinnedThumbprint, key);
    }

    private void SavePin(string? thumbprint)
    {
        var settings = _settings.Load();
        settings.ServerMap.PinnedThumbprint = thumbprint;
        _settings.Save(settings);

        PinnedThumbprint = thumbprint;
    }
}
