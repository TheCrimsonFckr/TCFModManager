using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.Core.ServerMap;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// OPEN-27: the picked profile's figures, as SPT's launcher shows them on its profile page - read from
// the profile file, with the level figures taken from the running server when it is up.
//
public partial class PlayViewModel
{
    private static readonly TimeSpan ServerProfilesEvery = TimeSpan.FromSeconds(15);

    [ObservableProperty]
    private ProfileStatsView? _profileStats;

    [ObservableProperty]
    private bool _statsUnreadable;

    [ObservableProperty]
    private bool _profileIsNew;

    [ObservableProperty]
    private int _statsTab;

    [ObservableProperty]
    private bool _profileIdCopied;

    // Why a profile on the Server Map server shows its level only. Null when its full figures are shown.
    [ObservableProperty]
    private string? _remoteStatsNote;

    public bool HasProfileStatsCard => Profiles.Count > 0 || (CanPlayRemote && !IsDirectLaunchOn);

    // The switch on this card instead of the Play card's, while the Play card isn't in use.
    public bool ShowsRemoteSwitchOnStats => CanPlayRemote && !IsDirectLaunchOn;

    public string? StatsRemoteProblem => ShowsRemoteSwitchOnStats ? RemoteProblem : null;

    // New and Delete on this card: the profiles are the connected server's and the Play card isn't in use.
    public bool ShowsRemoteProfileTools => ShowsRemoteSwitchOnStats && IsRemotePlay;

    private SptProfileStats? _shownStats;
    private SptMiniProfile? _shownRemote;
    private SptProfileStats? _shownRemoteFull;
    private readonly Dictionary<string, (SptProfileStats Stats, long? Written)> _remoteStats = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _remoteStatsAt = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _remoteNotes = new(StringComparer.Ordinal);
    private bool _askingRemoteStats;
    private SptLevelProgress? _shownProgress;
    private string _shownLanguage = "";
    private IReadOnlyDictionary<string, SptMiniProfile> _serverProfiles = new Dictionary<string, SptMiniProfile>();
    private DateTime _serverProfilesAt;
    private bool _askingServer;

    // From Refresh, and when the picked profile changes.
    private void RefreshStats(string? installPath)
    {
        OnPropertyChanged(nameof(HasProfileStatsCard));
        OnPropertyChanged(nameof(ShowsRemoteSwitchOnStats));
        OnPropertyChanged(nameof(StatsRemoteProblem));
        OnPropertyChanged(nameof(ShowsRemoteProfileTools));

        if (SelectedProfile is not { } item)
        {
            ShowStats(null, null, false, "");
            StatsUnreadable = false;
            ProfileIsNew = false;
            return;
        }

        ProfileIsNew = item.Profile.Wipe || item.Profile.Nickname.Length == 0;
        if (ProfileIsNew)
        {
            ShowStats(null, null, false, "");
            StatsUnreadable = false;
            return;
        }

        if (IsRemotePlay)
        {
            ShowRemoteStats(item);
            StatsUnreadable = false;
            _ = AskRemoteForStatsAsync(item.Profile.ProfileId);
            return;
        }

        var stats = SptLocalProfiles.ReadStats(installPath, item.Profile.ProfileId);
        StatsUnreadable = stats is null;

        var fromServer = _serverProfiles.TryGetValue(item.Profile.ProfileId, out var live) ? SptLevelProgress.FromServer(live) : null;
        var progress = fromServer ?? (stats is null ? null : SptLevelTable.Read(installPath)?.Progress(stats.Level, stats.Experience));
        ShowStats(stats, progress, fromServer is not null, item.Title);

        if (Server?.IsRunning == true)
            _ = AskServerForProfilesAsync();
        else if (_serverProfiles.Count > 0)
            _serverProfiles = new Dictionary<string, SptMiniProfile>();
    }

    private void ShowStats(SptProfileStats? stats, SptLevelProgress? progress, bool levelFromServer, string fallbackName)
    {
        var language = System.Globalization.CultureInfo.CurrentUICulture.Name;
        if (stats == _shownStats && progress == _shownProgress && language == _shownLanguage && (stats is null) == (ProfileStats is null)) return;

        _shownStats = stats;
        _shownProgress = progress;
        _shownLanguage = language;
        _shownRemote = null;
        _shownRemoteFull = null;
        RemoteStatsNote = null;
        ProfileStats = stats is null ? null : ProfileStatsView.Build(stats, fallbackName, progress, levelFromServer);
    }

    //
    // A profile on the Server Map server: the level and EXP from its launcher list, and the money and
    // raid record from the Server Map mod's /profile once they have arrived.
    //
    private void ShowRemoteStats(PlayProfileItem item)
    {
        var id = item.Profile.ProfileId;
        var full = _remoteStats.TryGetValue(id, out var held) ? held.Stats : null;
        RemoteStatsNote = full is null && _remoteNotes.TryGetValue(id, out var note) ? note : null;

        var language = System.Globalization.CultureInfo.CurrentUICulture.Name;
        if (item.Profile == _shownRemote && full == _shownRemoteFull && language == _shownLanguage && ProfileStats is not null) return;

        _shownRemote = item.Profile;
        _shownRemoteFull = full;
        _shownStats = null;
        _shownProgress = null;
        _shownLanguage = language;

        var progress = SptLevelProgress.FromServer(item.Profile);
        ProfileStats = full is null
            ? ProfileStatsView.FromServer(item.Profile, item.Title, progress)
            : ProfileStatsView.Build(full, item.Title, progress, levelFromServer: progress is not null);
    }

    private void ClearRemoteStats()
    {
        _remoteStats.Clear();
        _remoteStatsAt.Clear();
        _remoteNotes.Clear();
        _shownRemoteFull = null;
        RemoteStatsNote = null;
    }

    private async Task AskRemoteForStatsAsync(string profileId)
    {
        if (_askingRemoteStats) return;
        if (_remoteStatsAt.TryGetValue(profileId, out var at) && DateTime.UtcNow - at < ServerProfilesEvery) return;

        var map = new SettingsService().Load().ServerMap;
        if (!map.IsConfigured || string.IsNullOrWhiteSpace(map.PinnedThumbprint)) return;

        using var client = ServerMapClient.TryCreate(map.ToEndpoint());
        if (client is null) return;

        _askingRemoteStats = true;
        _remoteStatsAt[profileId] = DateTime.UtcNow;
        try
        {
            var since = _remoteStats.TryGetValue(profileId, out var held) ? held.Written : null;
            var result = await client.ProfileAsync(profileId, since, RemoteClientId());
            if (!IsRemotePlay) return;

            if (!result.Succeeded)
            {
                AppLog.Debug("ProfileStats", $"the Server Map server didn't send profile {profileId}: {result.Problem} {result.Error?.Message}");
                _remoteNotes[profileId] = result.Problem == ServerMapProblem.ProfileUnsupported
                    ? Strings.Profile_RemoteStatsOldMod
                    : Strings.Profile_RemoteStatsUnavailable;
            }
            else
            {
                _remoteNotes.Remove(profileId);
                switch (result.State)
                {
                    case ServerMapProfileState.Found:
                        _remoteStats[profileId] = (result.Stats!, result.Written);
                        break;
                    case ServerMapProfileState.Missing or ServerMapProfileState.Hidden:
                        _remoteStats.Remove(profileId);
                        break;
                    case ServerMapProfileState.Busy:
                        _remoteStatsAt.Remove(profileId);
                        break;
                }
            }

            RefreshStats(AppServices.SptEnvironment.InstallPath);
        }
        finally
        {
            _askingRemoteStats = false;
        }
    }

    private async Task AskServerForProfilesAsync()
    {
        if (_askingServer || DateTime.UtcNow - _serverProfilesAt < ServerProfilesEvery || Server?.ExePath is not { } serverExe) return;

        _askingServer = true;
        _serverProfilesAt = DateTime.UtcNow;
        try
        {
            using var api = new SptLauncherApi(SptDirectLaunch.ServerAddress(serverExe));
            var profiles = await api.ProfilesAsync();
            _serverProfiles = profiles.Where(p => p.ProfileId.Length > 0).GroupBy(p => p.ProfileId).ToDictionary(g => g.Key, g => g.First());
            RefreshStats(AppServices.SptEnvironment.InstallPath);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or System.IO.InvalidDataException)
        {
            AppLog.Debug("ProfileStats", $"the server didn't list its profiles: {ex.Message}");
        }
        finally
        {
            _askingServer = false;
        }
    }

    [RelayCommand]
    private async Task CopyProfileIdAsync()
    {
        if (ProfileStats is not { } stats) return;

        try
        {
            Clipboard.SetText(stats.ProfileId);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            AppLog.Debug("ProfileStats", $"couldn't copy the profile id: {ex.Message}");
            return;
        }

        ProfileIdCopied = true;
        await Task.Delay(1500);
        ProfileIdCopied = false;
    }
}
