using System.Net.Http;
using System.Windows;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

using Wpf.Ui.Controls;

namespace TCFModManager.App.ViewModels;

//
// App-lifetime state for "is there a newer version of this app on sp-mod.com, and does the user
// want it". Backs both the banner in MainWindow and the App update page.
//
// The whole feature runs through sp-mod.com's public API and sp-mod.com's own download link for
// this app's listing - the same file, from the same place, as clicking Download on the mod page.
// It doesn't bypass the mod page either: the existing ReadModPageConfirmationWindow gate applies
// here exactly as it does to installing any other mod, so the page is always opened first.
//
public partial class AppUpdateViewModel : ObservableObject
{
    private readonly AppUpdateService _updates = new(AppServices.SpModApi);
    private readonly AppUpdateInstaller _installer = new(AppServices.Downloads);
    private readonly SettingsService _settings = new();

    private CancellationTokenSource? _installCts;

    public string CurrentVersion => AppVersion.Current;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateAvailable))]
    [NotifyPropertyChangedFor(nameof(ShowUpToDate))]
    [NotifyPropertyChangedFor(nameof(LatestVersion))]
    [NotifyPropertyChangedFor(nameof(Changelog))]
    [NotifyPropertyChangedFor(nameof(ChangeTitle))]
    [NotifyPropertyChangedFor(nameof(ChangeSummary))]
    [NotifyPropertyChangedFor(nameof(BannerTitle))]
    [NotifyPropertyChangedFor(nameof(BannerSeverity))]
    [NotifyPropertyChangedFor(nameof(BadgeSeverity))]
    [NotifyPropertyChangedFor(nameof(DownloadSizeText))]
    [NotifyPropertyChangedFor(nameof(PublishedText))]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenModPageCommand))]
    private AppUpdateInfo? _update;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    private bool _isChecking;

    // True once a check has run, so the page can tell "not checked yet" apart from "nothing new".
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUpToDate))]
    private bool _hasChecked;

    // Why the last check couldn't complete. Null when it did.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUpToDate))]
    private string? _checkError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelInstallCommand))]
    private bool _isInstalling;

    [ObservableProperty]
    private double _installProgress;

    [ObservableProperty]
    private string? _installStatus;

    [ObservableProperty]
    private string? _installError;

    // Drives the banner in MainWindow. Two-way bound to the InfoBar, so the user closing it is a
    // dismissal - see OnIsBannerOpenChanged.
    [ObservableProperty]
    private bool _isBannerOpen;

    public bool UpdateAvailable => Update?.IsUpdate == true;

    // Deliberately requires a completed, successful check: a page that hasn't looked yet, or whose
    // look failed, must not claim the app is up to date.
    public bool ShowUpToDate => HasChecked && CheckError is null && !UpdateAvailable;

    public string? LatestVersion => Update?.LatestVersion;

    public string? Changelog => Update?.Changelog;

    // ---- What kind of update this is ----------------------------------------------------------
    //
    // Which of the three numbers moved is the most useful thing to tell someone deciding whether to
    // update now or later, so it's stated plainly rather than left for them to infer from the
    // version string.

    public string ChangeTitle => Update?.ChangeKind switch
    {
        VersionChangeKind.Patch => "Bug fix update",
        VersionChangeKind.Minor => "Feature update",
        VersionChangeKind.Major => "Major update",
        _ => "Update available",
    };

    public string ChangeSummary => Update is null
        ? string.Empty
        : Update.ChangeKind switch
        {
            VersionChangeKind.Patch =>
                $"{Update.LatestVersion} only changes the last number (x.x.●), so it's fixes to how the "
                + "current version already works - nothing new to learn. Safe to skip if nothing is broken for you.",

            VersionChangeKind.Minor =>
                $"{Update.LatestVersion} changes the middle number (x.●.x), so it adds features or "
                + "changes how something works. Worth reading the notes below before updating.",

            VersionChangeKind.Major =>
                $"{Update.LatestVersion} changes the first number (●.x.x), so this is a major update - "
                + "expect significant changes. Read the notes below and the mod page before updating.",

            _ =>
                $"sp-mod.com lists {Update.LatestVersion}, but it isn't numbered in a way this app can compare "
                + $"against {Update.CurrentVersion}. Check the mod page to see what changed.",
        };

    public InfoBarSeverity BannerSeverity => Update?.ChangeKind switch
    {
        VersionChangeKind.Minor => InfoBarSeverity.Success,
        VersionChangeKind.Major => InfoBarSeverity.Warning,
        _ => InfoBarSeverity.Informational,
    };

    public InfoBadgeSeverity BadgeSeverity => Update?.ChangeKind switch
    {
        VersionChangeKind.Minor => InfoBadgeSeverity.Success,
        VersionChangeKind.Major => InfoBadgeSeverity.Attention,
        _ => InfoBadgeSeverity.Informational,
    };

    public string BannerTitle => $"{ChangeTitle}: {LatestVersion}";

    public string? DownloadSizeText => Update?.DownloadSizeBytes is > 0
        ? $"{Update.DownloadSizeBytes.Value / (1024d * 1024d):N0} MB download"
        : null;

    public string? PublishedText => Update?.PublishedAt is { } published
        ? $"Published {published.ToLocalTime():d MMMM yyyy}"
        : null;

    // ---- Checking ------------------------------------------------------------------------------

    //
    // The check MainWindow fires once on launch. Anything that goes wrong is logged and shown on
    // the update page rather than interrupting startup - not being able to reach sp-mod.com is not
    // a reason to put a dialog in front of someone who just opened the app.
    //
    public async Task CheckOnStartupAsync()
    {
        // Lets the catalog fetch get its first requests away before adding two more. sp-mod.com
        // rate limits at the edge, and the catalog is what the user is actually waiting to see.
        await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(true);

        await RunCheckAsync(announce: true).ConfigureAwait(true);
    }

    private bool CanCheckForUpdates() => !IsChecking && !IsInstalling;

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private Task CheckForUpdatesAsync() => RunCheckAsync(announce: false);

    //
    // <param name="announce">Whether a found update should raise the banner. True for the automatic
    // startup check; false for the user pressing "Check now", who is already looking at the page and
    // doesn't need a banner over the top of it.</param>
    //
    private async Task RunCheckAsync(bool announce)
    {
        IsChecking = true;
        CheckError = null;

        try
        {
            Update = await _updates.CheckAsync().ConfigureAwait(true);
            HasChecked = true;

            if (!UpdateAvailable) return;

            // A version the user has already closed the banner on stays closed until something
            // newer than it is published.
            var dismissed = _settings.Load().DismissedAppUpdateVersion;
            if (announce && !string.Equals(dismissed, Update!.LatestVersion, StringComparison.OrdinalIgnoreCase))
                IsBannerOpen = true;
        }
        catch (SpModApiRateLimitedException)
        {
            CheckError = "sp-mod.com is rate limiting right now. Try again in a minute.";
        }
        catch (SpModApiException ex)
        {
            CheckError = $"sp-mod.com error: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            CheckError = $"Couldn't reach sp-mod.com: {ex.Message}";
        }
        catch (OperationCanceledException)
        {
            // HttpClient surfaces a request timeout as this rather than HttpRequestException.
            CheckError = "Timed out reaching sp-mod.com - check your connection and try again.";
        }
        catch (Exception ex)
        {
            CheckError = $"Unexpected error checking for updates: {ex.Message}";
            AppLog.Error("AppUpdate", "update check failed", ex);
        }
        finally
        {
            IsChecking = false;
            if (CheckError is not null) AppLog.Warn("AppUpdate", CheckError);
        }
    }

    // ---- Acting on it ---------------------------------------------------------------------------

    private bool CanOpenModPage() => !string.IsNullOrWhiteSpace(Update?.ModPageUrl);

    [RelayCommand(CanExecute = nameof(CanOpenModPage))]
    private void OpenModPage()
    {
        if (Update?.ModPageUrl is not { } url) return;

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }

    private bool CanInstallUpdate() => Update?.CanInstall == true && !IsInstalling;

    [RelayCommand(CanExecute = nameof(CanInstallUpdate))]
    private async Task InstallUpdateAsync()
    {
        if (Update is not { CanInstall: true } update) return;

        // The same gate every other install in this app goes through, for the same reason: the
        // mod page gets opened first, so nothing is downloaded without the author's page - and
        // whatever they've written on it - having been in front of the user.
        //
        // allowSkip: false because this one ignores the "skip mod pages" option. Skipping a mod's
        // page costs you its install notes; skipping this app's page costs you the release notes
        // for the build about to replace the one you are running.
        if (!ReadModPageConfirmationWindow.Confirm(SelfMod.Name, update.ModPageUrl, allowSkip: false))
        {
            AppLog.Info("AppUpdate", "update cancelled at the mod page gate");
            return;
        }

        _installCts?.Dispose();
        _installCts = new CancellationTokenSource();

        IsInstalling = true;
        InstallError = null;
        InstallProgress = 0;
        InstallStatus = $"Downloading {update.LatestVersion} from sp-mod.com...";

        try
        {
            var progress = new Progress<double>(fraction =>
            {
                InstallProgress = fraction * 100;
                InstallStatus = fraction < 0.85
                    ? $"Downloading {update.LatestVersion} from sp-mod.com... {fraction / 0.85:P0}"
                    : "Unpacking the new version...";
            });

            await _installer.PrepareAsync(update, progress, _installCts.Token).ConfigureAwait(true);

            InstallStatus = "Closing and restarting to finish the update...";
            AppUpdateInstaller.LaunchApplyScript();

            // The script is already waiting on this process. Shutdown (rather than Environment.Exit)
            // so App.OnExit still runs and the log is flushed before the swap happens.
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            AppUpdateInstaller.ClearWorkingFiles();
            InstallStatus = null;
            InstallError = "Update cancelled. Nothing was changed.";
        }
        catch (AppUpdateException ex)
        {
            AppUpdateInstaller.ClearWorkingFiles();
            InstallStatus = null;
            InstallError = ex.Message;
            AppLog.Error("AppUpdate", "update could not be applied", ex);
        }
        catch (Exception ex)
        {
            AppUpdateInstaller.ClearWorkingFiles();
            InstallStatus = null;
            InstallError = $"The update failed: {ex.Message}. Nothing was changed - "
                + "download it from the mod page and replace this folder by hand if it keeps happening.";
            AppLog.Error("AppUpdate", "update failed", ex);
        }
        finally
        {
            IsInstalling = false;
        }
    }

    private bool CanCancelInstall() => IsInstalling;

    [RelayCommand(CanExecute = nameof(CanCancelInstall))]
    private void CancelInstall() => _installCts?.Cancel();

    // Closing the banner is how a release gets skipped. Persisted, so a bug-fix update someone has
    // decided against doesn't reappear on every launch - anything published after it still will.
    partial void OnIsBannerOpenChanged(bool value)
    {
        if (value || Update?.LatestVersion is not { } version) return;

        var settings = _settings.Load();
        if (string.Equals(settings.DismissedAppUpdateVersion, version, StringComparison.OrdinalIgnoreCase)) return;

        settings.DismissedAppUpdateVersion = version;
        _settings.Save(settings);
        AppLog.Info("AppUpdate", $"banner dismissed for {version}");
    }
}
