using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.Core.ServerMap;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.ViewModels;

//
// The Server Map mod on the App update page. It is installed by hand on the machine running the
// server, so the app can only say that it is behind - never update it.
//
// Two ways to know what is running:
//   - this machine runs the server: the payload and stub on disk, which also catches a stub left
//     older than its payload (LAN-only then refuses everyone);
//   - this machine joins one: the version that server's /hello reports. Its operator has to update
//     it, so the page says who to ask.
//
public partial class AppUpdateViewModel
{
    private readonly ServerMapModUpdateService _serverMapMod = new(AppServices.SpModApi);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerMapLatestVersion))]
    private ServerMapModRelease? _serverMapRelease;

    [ObservableProperty]
    private InstalledServerMapMod? _serverMapInstalled;

    [ObservableProperty]
    private bool _serverMapCheckFailed;

    private bool _serverMapHooked;

    // Which SPT line's addon the check and the link use. Set with the files on disk.
    private ServerMapSptLine _serverMapLine = ServerMapSptLine.Spt41;

    public string? ServerMapLatestVersion => ServerMapRelease?.LatestVersion;

    // The version the connected server reports. Only used when this machine runs no server itself:
    // on the server's own machine the files on disk are the better answer, and usually the same one.
    // The mod reports its informational version, which can carry a "+commit" suffix; that is dropped.
    private string? ConnectedServerMapVersion =>
        ServerMapInstalled is null && AppServices.ServerMap.IsConnected
        && AppServices.ServerMap.Probe?.Hello?.ModVersion is { Length: > 0 } reported
            ? reported.Split('+')[0].Trim()
            : null;

    public bool ShowServerMapMod => ServerMapInstalled is not null || ConnectedServerMapVersion is not null;

    public bool HasServerMapLocal => ServerMapInstalled is not null;

    public bool HasServerMapConnected => ConnectedServerMapVersion is not null;

    public string ServerMapLocalVersion => ServerMapInstalled?.PayloadVersion ?? Strings.Common_Unknown;

    public string ServerMapConnectedVersion => ConnectedServerMapVersion ?? Strings.Common_Unknown;

    public bool ServerMapLocalBehind =>
        ServerMapInstalled is { } installed && ServerMapModVersions.IsBehind(installed.PayloadVersion, ServerMapLatestVersion);

    public bool ServerMapStubBehind => ServerMapInstalled?.StubBehindPayload == true;

    public bool ServerMapConnectedBehind => ServerMapModVersions.IsBehind(ConnectedServerMapVersion, ServerMapLatestVersion);

    // What lights the sidebar badge alongside an app update.
    public bool ServerMapModBehind => ServerMapLocalBehind || ServerMapStubBehind || ServerMapConnectedBehind;

    public bool ShowServerMapUpToDate =>
        ShowServerMapMod && ServerMapRelease is not null && !ServerMapModBehind;

    public bool ShowUpdateBadge => UpdateAvailable || ServerMapModBehind;

    public string ServerMapLocalBehindMessage =>
        Text(Strings.AppUpdate_ServerMapLocalBehindFormat, ServerMapLatestVersion, ServerMapInstalled?.PayloadVersion);

    public string ServerMapStubBehindMessage =>
        Text(Strings.AppUpdate_ServerMapStubBehindFormat, ServerMapInstalled?.StubVersion, ServerMapInstalled?.PayloadVersion);

    public string ServerMapConnectedBehindMessage =>
        Text(Strings.AppUpdate_ServerMapConnectedBehindFormat, ConnectedServerMapVersion, ServerMapLatestVersion);

    [RelayCommand]
    private void OpenServerMapModPage() =>
        Process.Start(new ProcessStartInfo(ServerMapAddon.PageUrl(_serverMapLine)) { UseShellExecute = true });

    // The files on disk, read again. Cheap, so the page does it every time it is shown.
    public void RefreshServerMapInstall()
    {
        HookServerMap();
        var installPath = _settings.Load().SptInstallPath;
        var installed = ServerMapModVersions.FindInstalled(installPath);

        _serverMapLine = ServerMapModVersions.LineFor(installed, installPath);
        ServerMapInstalled = installed;
    }

    //
    // Asks sp-mod.com for the newest Server Map mod. Its own failure stays its own: the app's check
    // above has already said whether sp-mod.com could be reached at all.
    //
    private async Task CheckServerMapModAsync()
    {
        RefreshServerMapInstall();

        try
        {
            ServerMapRelease = await _serverMapMod.LatestAsync(_serverMapLine).ConfigureAwait(true);
            ServerMapCheckFailed = false;
        }
        catch (Exception ex) when (ex is SpModApiException or HttpRequestException or OperationCanceledException)
        {
            ServerMapCheckFailed = true;
            AppLog.Warn("ServerMapMod", $"couldn't check for a newer Server Map mod: {ex.Message}");
        }
    }

    partial void OnServerMapReleaseChanged(ServerMapModRelease? value) => NotifyServerMap();

    partial void OnServerMapInstalledChanged(InstalledServerMapMod? value) => NotifyServerMap();

    //
    // Subscribed on first use, not in the constructor: this object is built before the Server Map
    // connection in AppServices.
    //
    private void HookServerMap()
    {
        if (_serverMapHooked) return;
        _serverMapHooked = true;

        AppServices.ServerMap.PropertyChanged += OnServerMapConnectionChanged;
    }

    private void OnServerMapConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ServerMapGateViewModel.Probe) or nameof(ServerMapGateViewModel.IsConnected))
            NotifyServerMap();
    }

    private void NotifyServerMap()
    {
        OnPropertyChanged(nameof(ShowServerMapMod));
        OnPropertyChanged(nameof(HasServerMapLocal));
        OnPropertyChanged(nameof(HasServerMapConnected));
        OnPropertyChanged(nameof(ServerMapLocalVersion));
        OnPropertyChanged(nameof(ServerMapConnectedVersion));
        OnPropertyChanged(nameof(ServerMapLocalBehind));
        OnPropertyChanged(nameof(ServerMapStubBehind));
        OnPropertyChanged(nameof(ServerMapConnectedBehind));
        OnPropertyChanged(nameof(ServerMapModBehind));
        OnPropertyChanged(nameof(ShowServerMapUpToDate));
        OnPropertyChanged(nameof(ShowUpdateBadge));
        OnPropertyChanged(nameof(BadgeSeverity));
        OnPropertyChanged(nameof(ServerMapLocalBehindMessage));
        OnPropertyChanged(nameof(ServerMapStubBehindMessage));
        OnPropertyChanged(nameof(ServerMapConnectedBehindMessage));
    }
}
