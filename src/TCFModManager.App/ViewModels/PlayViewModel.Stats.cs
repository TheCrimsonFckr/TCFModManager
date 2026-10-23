using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public bool HasProfileStatsCard => Profiles.Count > 0;

    private SptProfileStats? _shownStats;
    private SptLevelProgress? _shownProgress;
    private string _shownLanguage = "";
    private IReadOnlyDictionary<string, SptMiniProfile> _serverProfiles = new Dictionary<string, SptMiniProfile>();
    private DateTime _serverProfilesAt;
    private bool _askingServer;

    // From Refresh, and when the picked profile changes.
    private void RefreshStats(string? installPath)
    {
        OnPropertyChanged(nameof(HasProfileStatsCard));

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
        ProfileStats = stats is null ? null : ProfileStatsView.Build(stats, fallbackName, progress, levelFromServer);
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
