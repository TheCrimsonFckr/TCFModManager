using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.ServerMap;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

/// <summary>One profile in the Play card's picker.</summary>
public sealed class PlayProfileItem(SptMiniProfile profile)
{
    public SptMiniProfile Profile { get; } = profile;

    public string Title => Profile.Nickname.Length > 0 ? Profile.Nickname : Profile.Username;

    public string Detail => Profile.Wipe || Profile.Nickname.Length == 0
        ? LocalizationService.Text(Strings.Play_ProfileNewFormat, Profile.Username, Profile.Edition)
        : LocalizationService.Text(Strings.Play_ProfileDetailFormat, Profile.Level, Profile.Side.ToUpperInvariant(), Profile.Edition);

    // For screen readers and anything that shows the item as text.
    public override string ToString() => Title;

    // Same profile, same details: the picker is left alone (no flicker, no lost selection).
    public bool SameAs(PlayProfileItem other) => Profile == other.Profile;
}

//
// OPEN-12 F20, experimental: Play on its own card - the server, the profile, SPT's launcher steps
// and the game, with SPT's launcher never opened (SptDirectLaunch). Off unless switched on in
// Options; on an SPT version it was not made for (4.0, 4.1.0-4.1.2, a later line) the card says so
// and the launcher card below works as before.
//
public partial class PlayViewModel
{
    public ObservableCollection<PlayProfileItem> Profiles { get; } = [];

    public bool HasNoProfiles => IsDirectLaunchOn && Profiles.Count == 0 && RemoteProblem is null;

    // Server Map has a server set, so Play can use it instead of this install's own.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemoteSwitchText))]
    private bool _canPlayRemote;

    // The switch: on plays on the Server Map server, off on this install's own.
    [ObservableProperty]
    private bool _playOnRemote;

    // The switch is on and there is a Server Map server to use.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WipeWarning))]
    [NotifyPropertyChangedFor(nameof(DeleteProfileWarning))]
    [NotifyPropertyChangedFor(nameof(PlaySummary))]
    private bool _isRemotePlay;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemoteSwitchText))]
    [NotifyPropertyChangedFor(nameof(PlaySummary))]
    private string? _remoteAddress;

    // Why the Server Map server's profiles couldn't be listed.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoProfiles))]
    [NotifyPropertyChangedFor(nameof(StatsRemoteProblem))]
    private string? _remoteProblem;

    public string RemoteSwitchText => Text(Strings.Play_RemoteSwitchFormat, RemoteAddress);

    public string PlaySummary => IsRemotePlay ? Text(Strings.Play_RemoteSummaryFormat, RemoteAddress) : Strings.Play_DirectSummary;

    private bool _settingRemote;
    private DateTime _remoteProfilesAt;
    private bool _readingRemote;

    // The connected server's profile rules for this machine. Null: none (or a mod too old to have any).
    private ServerMapProfileRules? _remoteRules;

    // The host doesn't let other machines delete or wipe this profile, and this machine didn't make it.
    public bool SelectedProfileIsProtected =>
        IsRemotePlay && SelectedProfile is { } p && _remoteRules is { ProtectOtherProfiles: true } rules && !rules.Owns(p.Profile.ProfileId);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPlay))]
    [NotifyPropertyChangedFor(nameof(WipeWarning))]
    [NotifyPropertyChangedFor(nameof(SelectedProfileIsProtected))]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    private PlayProfileItem? _selectedProfile;

    // Switched on in Options: the Play card is on the page, working or saying why it can't.
    [ObservableProperty]
    private bool _isDirectLaunchEnabled;

    // The direct start is on, and this SPT version takes it.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPlay))]
    [NotifyPropertyChangedFor(nameof(HasNoProfiles))]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    private bool _isDirectLaunchOn;

    // On in Options, but not for this version: said on the launcher's card.
    [ObservableProperty]
    private string? _directLaunchUnavailable;

    // A later 4.1 than the ones read: allowed, and said.
    [ObservableProperty]
    private string? _directLaunchUntested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPlay))]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelPlayCommand))]
    private bool _isPlaying;

    [ObservableProperty]
    private string? _playStatus;

    [ObservableProperty]
    private bool _playStatusIsError;

    [ObservableProperty]
    private bool _offerServerLog;

    // 0-100 while bundles download; null shows the bar running.
    [ObservableProperty]
    private double? _playProgress;

    // The next Play wipes the profile first (a new character, from the edition's start).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WipeWarning))]
    private bool _wipeNextStart;

    public string? WipeWarning => WipeNextStart && SelectedProfile is { } p
        ? LocalizationService.Text(IsRemotePlay ? Strings.Play_RemoteWipeArmedFormat : Strings.Play_WipeArmedFormat, p.Title)
        : null;

    public bool CanPlay => IsDirectLaunchOn && !IsPlaying && SelectedProfile is not null && !IsGameRunning && Server?.Exists == true;

    private CancellationTokenSource? _playCts;
    private bool _readingProfile;
    private WindowState? _stateBeforeGame;

    partial void OnIsGameRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanPlay));
        PlayCommand.NotifyCanExecuteChanged();
        if (value || IsPlaying) return;

        // The game has gone: "Playing as ..." no longer holds, and the window comes back if Play hid it.
        if (!PlayStatusIsError) PlayStatus = null;
        if (_stateBeforeGame is { } before && Application.Current?.MainWindow is { WindowState: WindowState.Minimized } window)
            window.WindowState = before;
        _stateBeforeGame = null;
    }

    partial void OnSelectedProfileChanged(PlayProfileItem? value)
    {
        WipeNextStart = false;
        ConfirmingDeleteProfile = false;
        if (_readingProfile) return;

        RefreshStats(AppServices.SptEnvironment.InstallPath);
        if (value is null) return;

        var settings = new SettingsService().Load();
        var id = value.Profile.ProfileId;
        if (IsRemotePlay)
        {
            if (settings.RemotePlayProfileId == id) return;
            settings.RemotePlayProfileId = id;
        }
        else
        {
            if (settings.DirectLaunchProfileId == id) return;
            settings.DirectLaunchProfileId = id;
        }

        new SettingsService().Save(settings);
    }

    partial void OnPlayOnRemoteChanged(bool value)
    {
        if (_settingRemote) return;

        var settings = new SettingsService().Load();
        if (settings.PlayOnServerMapHost == value) return;
        settings.PlayOnServerMapHost = value;
        new SettingsService().Save(settings);
        AppLog.Info("DirectLaunch", value ? $"playing on the Server Map server {RemoteAddress}" : "playing on this install's server");

        ConfirmingDeleteProfile = false;
        ShowNewProfile = false;
        PlayStatus = null;
        Refresh();
    }

    // From Refresh: whether the card is the direct one, and the profiles on disk.
    private void RefreshDirect(string? installPath, AppSettings settings, bool readProfiles = true)
    {
        var support = SptDirectLaunch.Support(AppServices.SptEnvironment.InstalledVersion);

        var map = settings.ServerMap;
        CanPlayRemote = map.IsConfigured;
        RemoteAddress = map.IsConfigured ? SptLauncherApi.AddressOf(map.Host!, map.Port) : null;
        _settingRemote = true;
        PlayOnRemote = settings.PlayOnServerMapHost;
        _settingRemote = false;

        var remote = settings.PlayOnServerMapHost && map.IsConfigured;
        if (remote != IsRemotePlay)
        {
            IsRemotePlay = remote;
            RemoteProblem = null;
            _remoteRules = null;
            ClearRemoteStats();
            WipeNextStart = false;
            _remoteProfilesAt = default;
            _readingProfile = true;
            try
            {
                Profiles.Clear();
                SelectedProfile = null;
            }
            finally
            {
                _readingProfile = false;
            }
        }

        IsDirectLaunchEnabled = settings.DirectLaunch;
        IsDirectLaunchOn = settings.DirectLaunch && support != DirectLaunchSupport.Unsupported && Server?.Exists == true;

        // Said only for an install whose SPT version is known and is not one this can start: with no
        // install, or no server found, the page already says that.
        DirectLaunchUnavailable = settings.DirectLaunch
            && support == DirectLaunchSupport.Unsupported
            && Server?.Exists == true
            && AppServices.SptEnvironment.InstalledVersion is { Length: > 0 } installedVersion
                ? Text(Strings.Play_DirectUnavailableFormat, installedVersion)
                : null;
        DirectLaunchUntested = IsDirectLaunchOn && support == DirectLaunchSupport.NewerUntested
            ? Text(Strings.Play_DirectUntestedFormat, AppServices.SptEnvironment.InstalledVersion, SptDirectLaunch.TestedUpTo)
            : null;

        if (!readProfiles) return;

        if (remote)
        {
            _ = ReadRemoteProfilesAsync(force: false);
            return;
        }

        ApplyProfiles(SptLocalProfiles.Read(installPath), () => settings.DirectLaunchProfileId ?? LauncherPreferredProfile(installPath));
    }

    private void ApplyProfiles(IReadOnlyList<SptMiniProfile> profiles, Func<string?> preferred)
    {
        var found = profiles.Select(p => new PlayProfileItem(p)).ToList();
        if (found.Count == Profiles.Count && found.Zip(Profiles).All(pair => pair.First.SameAs(pair.Second))) return;

        var keep = SelectedProfile?.Profile.ProfileId ?? preferred();

        _readingProfile = true;
        try
        {
            Profiles.Clear();
            foreach (var item in found) Profiles.Add(item);
            SelectedProfile = Profiles.FirstOrDefault(p => p.Profile.ProfileId == keep) ?? Profiles.FirstOrDefault();
        }
        finally
        {
            _readingProfile = false;
            OnPropertyChanged(nameof(HasNoProfiles));
        }
    }

    // The Server Map server's profiles, asked for at most every 15 seconds unless forced.
    private async Task ReadRemoteProfilesAsync(bool force)
    {
        if (!force && (_readingRemote || DateTime.UtcNow - _remoteProfilesAt < ServerProfilesEvery)) return;

        if (RemoteApi() is not { } api)
        {
            RemoteProblem = Strings.Play_RemoteNotPinned;
            return;
        }

        _readingRemote = true;
        _remoteProfilesAt = DateTime.UtcNow;
        try
        {
            using (api)
            {
                var profiles = await api.ProfilesAsync();
                if (!IsRemotePlay) return;

                await ReadRemoteRulesAsync();
                if (!IsRemotePlay) return;

                RemoteProblem = null;
                var hideOthers = _remoteRules is { HideOtherProfiles: true };
                var listed = profiles
                    .Where(p => p.ProfileId.Length > 0 && p.Username.Length > 0)
                    .Where(p => !hideOthers || _remoteRules!.Owns(p.ProfileId))
                    .OrderBy(p => p.Nickname.Length == 0 ? p.Username : p.Nickname, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                ApplyProfiles(listed, () => new SettingsService().Load().RemotePlayProfileId);
                RefreshStats(AppServices.SptEnvironment.InstallPath);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidDataException)
        {
            AppLog.Debug("DirectLaunch", $"the Server Map server didn't list its profiles: {ex.Message}");
            if (IsRemotePlay) RemoteProblem = Text(Strings.Play_RemoteUnreachableFormat, RemoteAddress);
        }
        finally
        {
            _readingRemote = false;
        }
    }

    private async Task ReadRemoteRulesAsync()
    {
        using var client = RemoteMapClient();
        if (client is null)
        {
            _remoteRules = null;
        }
        else
        {
            var result = await client.ProfileRulesAsync(RemoteClientId());
            if (result.Succeeded) _remoteRules = result.Rules;
            else if (result.Problem == ServerMapProblem.ProfileUnsupported) _remoteRules = null;
            else AppLog.Debug("DirectLaunch", $"the Server Map server didn't send its profile rules: {result.Problem} {result.Error?.Message}");
        }

        OnPropertyChanged(nameof(SelectedProfileIsProtected));
    }

    // True when the new profile is this machine's, or the server has no profile rules to need it.
    private async Task<bool> ClaimNewProfileAsync(SptLauncherApi api, string username, CancellationToken ct)
    {
        using var client = RemoteMapClient();
        if (client is null) return true;

        var made = (await api.ProfilesAsync(ct)).FirstOrDefault(p => string.Equals(p.Username, username, StringComparison.Ordinal));
        if (made is null || made.ProfileId.Length == 0) return false;

        var result = await client.ClaimAsync(RemoteClientId(), made.ProfileId, ct);
        if (result.Succeeded && result.Claimed) return true;
        if (result.Problem == ServerMapProblem.ProfileUnsupported) return true;

        AppLog.Warn("DirectLaunch", $"couldn't claim {username} on the Server Map server: {result.Problem} {result.Refusal} {result.Error?.Message}");
        return false;
    }

    private static ServerMapClient? RemoteMapClient()
    {
        var map = new SettingsService().Load().ServerMap;
        if (!map.IsConfigured || string.IsNullOrWhiteSpace(map.PinnedThumbprint)) return null;
        return ServerMapClient.TryCreate(map.ToEndpoint());
    }

    // This machine's Server Map identity, made the first time it is needed.
    private static string RemoteClientId()
    {
        var service = new SettingsService();
        var settings = service.Load();
        var before = settings.ServerMap.ClientId;
        var id = ServerMapReporting.EnsureClientId(settings.ServerMap);
        if (before != id) service.Save(settings);
        return id;
    }

    // Null when Server Map has no server, or hasn't checked its certificate yet.
    private static SptLauncherApi? RemoteApi()
    {
        var map = new SettingsService().Load().ServerMap;
        if (!map.IsConfigured || string.IsNullOrWhiteSpace(map.PinnedThumbprint)) return null;
        return SptLauncherApi.Pinned(SptLauncherApi.AddressOf(map.Host!, map.Port), map.PinnedThumbprint);
    }

    private SptLauncherApi? PlayApi(string serverExe) =>
        IsRemotePlay ? RemoteApi() : new SptLauncherApi(SptDirectLaunch.ServerAddress(serverExe));

    private static string? LauncherPreferredProfile(string? installPath) =>
        ServerRootOf(installPath) is { } root ? SptLauncherSettings.Read(root).PreferredProfileId : null;

    private static string? ServerRootOf(string? installPath) =>
        SptInstallationService.TryGetServerRoot(installPath, out var relative)
            ? System.IO.Path.GetFullPath(System.IO.Path.Combine(installPath!, relative))
            : null;

    // ---- Play ----------------------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task PlayAsync()
    {
        if (SelectedProfile is not { } item) return;

        var installPath = AppServices.SptEnvironment.InstallPath;
        var gameRoot = SptInstallationService.ToGameRoot(installPath);
        if (gameRoot is null || ServerRootOf(installPath) is not { } serverRoot || Server?.ExePath is not { } serverExe) return;

        _playCts?.Dispose();
        var cts = _playCts = new CancellationTokenSource();
        IsPlaying = true;
        PlayStatusIsError = false;
        PlayProgress = null;
        var gameStarted = false;
        ConfirmingDeleteProfile = false;
        ShowNewProfile = false;

        try
        {
            using var api = PlayApi(serverExe);
            if (api is null)
            {
                Fail(Strings.Play_RemoteNotPinned);
                return;
            }

            if (!await EnsureServerAsync(api, installPath, cts.Token)) return;

            var wipe = WipeNextStart;
            if (wipe && !IsRemotePlay && !AppServices.ProfileBackups.EnsureBackupBefore(installPath!, ProfileBackups.BeforeWipe))
            {
                Fail(Strings.Play_DirectBackupFailed);
                return;
            }

            var launcherSettings = SptLauncherSettings.Read(serverRoot);
            var launch = new SptDirectLaunch(gameRoot, serverRoot, api);
            var progress = new Progress<DirectLaunchProgress>(ShowProgress);

            var result = await launch.RunAsync(item.Profile, new DirectLaunchOptions
            {
                Wipe = wipe,
                ClearCache = launcherSettings.ClearCacheOnLaunch,
                ExcludeFromCleanup = launcherSettings.ExcludeFromCleanup,
                KeepGameLogsIn = LogFiles.KeptGameLogsFolder(gameRoot),
            }, progress, cts.Token);

            if (!result.Started)
            {
                Fail(DescribeProblem(result));
                return;
            }

            WipeNextStart = false;
            gameStarted = true;
            PlayProgress = null;
            PlayStatus = Text(Strings.Play_DirectStartingGameFormat, item.Title);

            // The game takes a few seconds to show up; watched for a minute and a half.
            var settings = new SettingsService().Load();
            for (var i = 0; i < 90 && !cts.IsCancellationRequested; i++)
            {
                if (SptLaunchService.IsGameRunning(installPath, GameCloser.SparesWindowless(settings, installPath))) break;
                await Task.Delay(1000, cts.Token);
            }

            Refresh();
            PlayStatus = IsGameRunning ? Text(Strings.Play_DirectPlayingFormat, item.Title) : Strings.Play_DirectGameNotSeen;

            if (IsGameRunning && settings.MinimizeWhilePlaying && Application.Current?.MainWindow is { } window)
            {
                _stateBeforeGame = window.WindowState;
                window.WindowState = WindowState.Minimized;
            }
        }
        catch (OperationCanceledException)
        {
            // Once the game was started, Cancel only stops the watching for it.
            PlayStatus = gameStarted ? Text(Strings.Play_DirectStartingGameFormat, item.Title) : Strings.Play_DirectCancelled;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or UriFormatException)
        {
            // Something on disk or in SPT's config this app couldn't use: said on the card, not as a crash.
            AppLog.Warn("DirectLaunch", $"Play stopped: {ex}");
            Fail(Text(Strings.Play_DirectErrorFormat, ex.Message));
        }
        finally
        {
            IsPlaying = false;
            PlayProgress = null;
            if (ReferenceEquals(_playCts, cts)) _playCts = null;
            cts.Dispose();
            Refresh();
        }
    }

    [RelayCommand]
    private void ShowServerLogOnServer()
    {
        ShowServerLog = true;
        AppNavigation.Navigate(typeof(Views.ServerPage));
    }

    private bool CanCancelPlay() => IsPlaying;

    [RelayCommand(CanExecute = nameof(CanCancelPlay))]
    private void CancelPlay() => _playCts?.Cancel();

    private void Fail(string text)
    {
        PlayStatusIsError = true;
        PlayStatus = text;
        AppLog.Warn("DirectLaunch", text);
    }

    private void ShowProgress(DirectLaunchProgress p)
    {
        PlayProgress = null;
        PlayStatus = p.Stage switch
        {
            DirectLaunchStage.CheckingVersion => Strings.Play_DirectStageChecking,
            DirectLaunchStage.CleaningUp => Strings.Play_DirectStageCleaning,
            DirectLaunchStage.Wiping => Strings.Play_DirectStageWiping,
            DirectLaunchStage.ClearingCache => Strings.Play_DirectStageCache,
            DirectLaunchStage.Patching => Strings.Play_DirectStagePatching,
            DirectLaunchStage.Bundles => Text(Strings.Play_DirectStageBundlesFormat, p.Done, p.Total, p.Bytes / (1024d * 1024d), p.TotalBytes / (1024d * 1024d)),
            _ => Strings.Play_DirectStageStarting,
        };

        if (p.Stage == DirectLaunchStage.Bundles && p.TotalBytes > 0) PlayProgress = Math.Clamp(100d * p.Bytes / p.TotalBytes, 0, 100);
    }

    private static string DescribeProblem(DirectLaunchResult result) => result.Problem switch
    {
        DirectLaunchProblem.NoGameExe => Strings.Play_DirectNoGame,
        DirectLaunchProblem.ServerUnreachable => Text(Strings.Play_DirectServerUnreachableFormat, result.Error?.Message),
        DirectLaunchProblem.CoreDllMissing => Strings.Play_DirectNoCoreDll,
        DirectLaunchProblem.VersionMismatch => Text(Strings.Play_DirectVersionMismatchFormat, result.ServerVersion, result.DllVersion),
        DirectLaunchProblem.NoSuchProfile => Strings.Play_DirectNoSuchProfile,
        DirectLaunchProblem.WipeRefused => Strings.Play_DirectWipeRefused,
        DirectLaunchProblem.ProfileInvalid => Strings.Play_DirectProfileInvalid,
        DirectLaunchProblem.PatchFailed => Text(Strings.Play_DirectPatchFailedFormat, result.Detail, result.Error?.Message),
        DirectLaunchProblem.BundlesFailed when result.Detail?.Split('|', 2) is [var count, var name] =>
            Text(Strings.Play_DirectBundlesFailedFormat, count, name),
        DirectLaunchProblem.StartFailed => Text(Strings.Play_DirectStartFailedFormat, result.Error?.Message),
        _ => Strings.Play_DirectFailed,
    };

    //
    // The server answering the launcher routes - started first when it is not running, with or
    // without its window as Options has it, and waited for (a modded server takes a while to load).
    //
    private async Task<bool> EnsureServerAsync(SptLauncherApi api, string? installPath, CancellationToken ct)
    {
        OfferServerLog = false;
        if (await PingAsync(api, ct)) return true;

        if (IsRemotePlay)
        {
            Fail(Text(Strings.Play_RemoteUnreachableFormat, api.Address));
            return false;
        }

        if (!SptLaunchService.Describe(installPath, SptLaunchTarget.Server).IsRunning)
        {
            PlayStatus = Strings.Play_DirectStartingServer;
            var hidden = new SettingsService().Load().HideServerWindow;
            var started = SptLaunchService.Launch(installPath, SptLaunchTarget.Server, HeadlessOverride(), hidden);
            Refresh();
            if (!started.Started)
            {
                Fail(SptLaunchProblems.Describe(started));
                return false;
            }
        }

        var since = DateTime.UtcNow;
        while (DateTime.UtcNow - since < TimeSpan.FromMinutes(3))
        {
            PlayStatus = Text(Strings.Play_DirectWaitingForServerFormat, (int)(DateTime.UtcNow - since).TotalSeconds);
            await Task.Delay(1000, ct);

            if (await PingAsync(api, ct)) return true;

            // The first seconds are given regardless: the new process may not be listed yet.
            if (DateTime.UtcNow - since > TimeSpan.FromSeconds(15)
                && !SptLaunchService.Describe(installPath, SptLaunchTarget.Server).IsRunning)
            {
                Fail(Strings.Play_DirectServerStopped);
                OfferServerLog = true;
                return false;
            }
        }

        Fail(Text(Strings.Play_DirectServerTimeoutFormat, api.Address));
        OfferServerLog = true;
        return false;
    }

    private static async Task<bool> PingAsync(SptLauncherApi api, CancellationToken ct)
    {
        using var once = CancellationTokenSource.CreateLinkedTokenSource(ct);
        once.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            return await api.PingAsync(once.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
        {
            return false;
        }
    }

    // ---- the card's menu -----------------------------------------------------------------------

    [RelayCommand]
    private void ToggleWipe()
    {
        if (SelectedProfile is null) return;
        if (!WipeNextStart && SelectedProfileIsProtected)
        {
            Fail(Strings.Play_RemoteProtected);
            return;
        }

        WipeNextStart = !WipeNextStart;
    }

    [RelayCommand]
    private void ClearGameCacheNow()
    {
        if (ServerRootOf(AppServices.SptEnvironment.InstallPath) is not { } root) return;
        if (IsGameRunning)
        {
            Fail(Strings.Play_DirectCacheWhileRunning);
            return;
        }

        SptDirectLaunch.ClearCache(root);
        PlayStatusIsError = false;
        PlayStatus = Strings.Play_DirectCacheCleared;
    }

    // New profile: a name and an edition (the server's list), created on the server.
    [ObservableProperty]
    private bool _showNewProfile;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateProfileCommand))]
    private string _newProfileName = "";

    public ObservableCollection<KeyValuePair<string, string>> Editions { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateProfileCommand))]
    private KeyValuePair<string, string>? _selectedEdition;

    [RelayCommand]
    private async Task NewProfileAsync()
    {
        if (IsPlaying) return;
        ConfirmingDeleteProfile = false;

        await WithServerAsync(async (api, ct) =>
        {
            var types = await api.TypesAsync(ct);
            Editions.Clear();
            foreach (var type in types.OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase)) Editions.Add(type);
            SelectedEdition = Editions.Count == 0
                ? null
                : Editions.FirstOrDefault(e => e.Key.Equals("Standard", StringComparison.OrdinalIgnoreCase)) is { Key: not null } standard
                    ? standard
                    : Editions[0];
            NewProfileName = "";
            ShowNewProfile = true;
            PlayStatus = null;
        });
    }

    private bool CanCreateProfile() => !string.IsNullOrWhiteSpace(NewProfileName) && SelectedEdition is not null;

    [RelayCommand(CanExecute = nameof(CanCreateProfile))]
    private async Task CreateProfileAsync()
    {
        if (SelectedEdition is not { } edition) return;
        var name = NewProfileName.Trim();

        await WithServerAsync(async (api, ct) =>
        {
            if (!await api.RegisterAsync(name, edition.Key, ct))
            {
                Fail(Text(Strings.Play_DirectProfileNotCreatedFormat, name));
                return;
            }

            var claimed = !IsRemotePlay || await ClaimNewProfileAsync(api, name, ct);

            ShowNewProfile = false;
            Refresh();
            if (IsRemotePlay) await ReadRemoteProfilesAsync(force: true);
            SelectedProfile = Profiles.FirstOrDefault(p => p.Profile.Username == name) ?? SelectedProfile;
            PlayStatusIsError = !claimed;
            PlayStatus = Text(claimed ? Strings.Play_DirectProfileCreatedFormat : Strings.Play_RemoteNotClaimedFormat, name);
        });
    }

    [RelayCommand]
    private void CancelNewProfile() => ShowNewProfile = false;

    // Delete: asked on the card, a copy of the profiles taken first.
    [ObservableProperty]
    private bool _confirmingDeleteProfile;

    public string? DeleteProfileWarning => SelectedProfile is { } p
        ? Text(IsRemotePlay ? Strings.Play_RemoteDeleteWarningFormat : Strings.Play_DirectDeleteWarningFormat, p.Title)
        : null;

    [RelayCommand]
    private void AskDeleteProfile()
    {
        if (SelectedProfile is null || IsPlaying) return;
        ShowNewProfile = false;
        PlayStatus = null;
        if (SelectedProfileIsProtected)
        {
            Fail(Strings.Play_RemoteProtected);
            return;
        }

        OnPropertyChanged(nameof(DeleteProfileWarning));
        ConfirmingDeleteProfile = true;
    }

    [RelayCommand]
    private void CancelDeleteProfile() => ConfirmingDeleteProfile = false;

    [RelayCommand]
    private async Task ConfirmDeleteProfileAsync()
    {
        if (!ConfirmingDeleteProfile || SelectedProfile is not { } item) return;
        ConfirmingDeleteProfile = false;

        if (SelectedProfileIsProtected)
        {
            Fail(Strings.Play_RemoteProtected);
            return;
        }

        if (IsGameRunning)
        {
            Fail(Strings.Play_DirectDeleteWhileRunning);
            return;
        }

        await WithServerAsync(async (api, ct) =>
        {
            if (!IsRemotePlay && !AppServices.ProfileBackups.EnsureBackupBefore(AppServices.SptEnvironment.InstallPath!, ProfileBackups.BeforeProfileDelete))
            {
                Fail(Strings.Play_DirectBackupFailed);
                return;
            }

            if (!await api.RemoveAsync(item.Profile.Username, ct))
            {
                Fail(Text(Strings.Play_DirectProfileNotDeletedFormat, item.Title));
                return;
            }

            AppLog.Info("DirectLaunch", IsRemotePlay
                ? $"profile {item.Profile.Username} deleted on {api.Address}"
                : $"profile {item.Profile.Username} deleted (a copy was kept)");
            Refresh();
            if (IsRemotePlay) await ReadRemoteProfilesAsync(force: true);
            PlayStatusIsError = false;
            PlayStatus = Text(Strings.Play_DirectProfileDeletedFormat, item.Title);
        });
    }

    // Runs an action against the server, starting it first when it is down.
    private async Task WithServerAsync(Func<SptLauncherApi, CancellationToken, Task> action)
    {
        var exe = Server?.ExePath;
        if (exe is null && !IsRemotePlay) return;

        IsPlaying = true;
        PlayStatusIsError = false;
        var cts = _playCts = new CancellationTokenSource();
        try
        {
            using var api = PlayApi(exe ?? "");
            if (api is null)
            {
                Fail(Strings.Play_RemoteNotPinned);
                return;
            }

            if (!await EnsureServerAsync(api, AppServices.SptEnvironment.InstallPath, cts.Token)) return;
            PlayStatus = null;
            await action(api, cts.Token);
        }
        catch (OperationCanceledException)
        {
            PlayStatus = Strings.Play_DirectCancelled;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            Fail(Text(Strings.Play_DirectServerUnreachableFormat, ex.Message));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or UriFormatException)
        {
            AppLog.Warn("DirectLaunch", $"profile action stopped: {ex}");
            Fail(Text(Strings.Play_DirectErrorFormat, ex.Message));
        }
        finally
        {
            IsPlaying = false;
            if (ReferenceEquals(_playCts, cts)) _playCts = null;
            cts.Dispose();
        }
    }
}
