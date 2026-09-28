using TCFModManager.Core.Models;
using TCFModManager.Core.ServerMap;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// Tells the server this machine is here, once a minute, for as long as the app runs - window open
// or hidden in the tray (R1).
//
// Only ever runs when all four hold: the Server map page is switched on, an address is set, the
// server's certificate is pinned (so the machine has actually connected to it once), and the user
// said yes to reporting to THAT server (D9). Start() re-checks all four every time it is called, so
// every place that changes one of them just calls Start() again.
//
// The inventory is read from disk at most every five minutes, or sooner when the download queue
// installs something. A heartbeat in between sends only the hash of it; the full list goes when it
// changed or the server asks for it.
//
internal sealed class ServerMapReporter
{
    private static readonly TimeSpan InventoryMaxAge = TimeSpan.FromMinutes(5);

    private readonly SettingsService _settings = new();

    private CancellationTokenSource? _loop;
    private List<ReportedMod>? _inventory;
    private DateTimeOffset _inventoryReadAt;
    private string? _sentHash;
    private string? _sentTo;
    private bool _serverWantsInventory;
    private ServerMapProblem? _lastLogged;

    // Raised on the UI thread after every attempt, successful or not.
    public event EventHandler? Reported;

    public bool IsRunning => _loop is not null;

    public ServerMapReportResult? LastResult { get; private set; }

    public DateTimeOffset? LastReportedAt { get; private set; }

    public ServerMapReporter()
    {
        AppServices.DownloadQueue.ItemInstalled += (_, _) => InventoryChanged();
    }

    // Must be called on the UI thread.
    public void Start()
    {
        Stop();

        var map = _settings.Load().ServerMap;
        var endpoint = map.ToEndpoint();

        if (!map.ShowPage || !map.IsConfigured || !endpoint.HasPin) return;
        if (ServerMapReporting.ConsentFor(map, endpoint) != ReportConsentState.Allowed) return;

        var server = ServerMapReporting.ServerKey(endpoint);
        if (!string.Equals(server, _sentTo, StringComparison.OrdinalIgnoreCase))
        {
            _sentHash = null;
            _sentTo = server;
        }

        _loop = new CancellationTokenSource();
        _ = RunAsync(_loop.Token);

        AppLog.Info("ServerMap", $"reporting to {endpoint.Host}:{endpoint.Port}");
    }

    public void Stop()
    {
        if (_loop is null) return;

        _loop.Cancel();
        _loop.Dispose();
        _loop = null;

        AppLog.Info("ServerMap", "reporting stopped");
    }

    // The next report reads the install again rather than trusting the last read.
    public void InventoryChanged() => _inventory = null;

    //
    // Takes this machine off the map of the server it is configured for. Best effort: a server that
    // is down forgets the machine on its own after 30 days, and shows it as away long before that.
    //
    public async Task<ServerMapProblem> WithdrawAsync(ServerMapEndpoint endpoint)
    {
        var clientId = _settings.Load().ServerMap.ClientId;
        if (string.IsNullOrWhiteSpace(clientId)) return ServerMapProblem.None;

        using var client = ServerMapClient.TryCreate(endpoint);
        if (client is null) return ServerMapProblem.InvalidAddress;

        var problem = await client.WithdrawAsync(clientId);

        AppLog.Info("ServerMap", problem == ServerMapProblem.None
            ? $"withdrew this machine from {endpoint.Host}:{endpoint.Port}"
            : $"couldn't withdraw from {endpoint.Host}:{endpoint.Port} - {problem}");

        _sentHash = null;
        return problem;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var interval = await ReportOnceAsync(ct);
                await Task.Delay(TimeSpan.FromSeconds(interval), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Stopped, or restarted for a new server.
        }
    }

    private async Task<int> ReportOnceAsync(CancellationToken ct)
    {
        try
        {
            var settings = _settings.Load();
            var installPath = AppServices.SptEnvironment.InstallPath;

            if (string.IsNullOrWhiteSpace(installPath)) return ServerMapMachines.DefaultIntervalSeconds;

            await RefreshInventoryAsync();
            if (_inventory is null) return ServerMapMachines.DefaultIntervalSeconds;

            var hash = ServerMapMachines.HashOf(_inventory);
            var includeInventory = _serverWantsInventory || !string.Equals(hash, _sentHash, StringComparison.Ordinal);

            // A process walk, once a minute - off the UI thread.
            var gameRunning = await Task.Run(() => ModInstallService.RunningBlockers(installPath)
                .Contains("EscapeFromTarkov.exe", StringComparer.OrdinalIgnoreCase), ct);

            //
            // The id is made the first time this install reports and must survive to the next
            // launch, or every restart would add the same machine to the map again.
            //
            var idBefore = settings.ServerMap.ClientId;
            ServerMapReporting.EnsureClientId(settings.ServerMap);
            if (idBefore != settings.ServerMap.ClientId) _settings.Save(settings);

            var report = ServerMapReporting.Build(
                settings.ServerMap,
                settings.Roles,
                hosts: ServerMapReporting.HostsServerMap(installPath),
                gameRunning,
                AppServices.SptEnvironment.InstalledVersion,
                AppVersion.Current,
                _inventory,
                includeInventory);

            using var client = ServerMapClient.TryCreate(settings.ServerMap.ToEndpoint());
            if (client is null) return ServerMapMachines.DefaultIntervalSeconds;

            var result = await client.ReportAsync(report, ct);

            LastResult = result;

            if (result.Succeeded)
            {
                LastReportedAt = DateTimeOffset.Now;
                if (includeInventory) _sentHash = hash;
                _serverWantsInventory = result.Resend;
            }

            LogOnChange(result.Problem);
            Reported?.Invoke(this, EventArgs.Empty);

            if (result.Problem == ServerMapProblem.MapUnsupported)
            {
                // Nothing will change until the server mod is updated; the next connect restarts this.
                Stop();
            }

            return result.Succeeded ? result.IntervalSeconds : ServerMapMachines.DefaultIntervalSeconds;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A report that fails for any other reason must not take the loop down with it.
            AppLog.Error("ServerMap", "report failed", ex);
            return ServerMapMachines.DefaultIntervalSeconds;
        }
    }

    private async Task RefreshInventoryAsync()
    {
        if (_inventory is not null && DateTimeOffset.Now - _inventoryReadAt < InventoryMaxAge) return;

        // An install or a list apply in progress would be read half-placed. The last read stands.
        if (AppServices.DownloadQueue.Items.Any(i => !i.IsFinished)) return;

        var install = await AppServices.ModListWorkflow.ReadInstallAsync();
        if (install is null) return;

        _inventory = ServerMapMachines.InventoryOf(install.Candidates);
        _inventoryReadAt = DateTimeOffset.Now;
    }

    private void LogOnChange(ServerMapProblem problem)
    {
        if (_lastLogged == problem) return;
        _lastLogged = problem;

        if (problem == ServerMapProblem.None) AppLog.Info("ServerMap", "reporting");
        else AppLog.Warn("ServerMap", $"report not accepted - {problem}");
    }
}
