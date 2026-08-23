using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.ViewModels;

// Backs ModUpdateContentDialog, showing mod details and letting the user pick a published version to install. Fetches the full version history for the installed mod.
public partial class ModUpdateDialogViewModel : ObservableObject
{
    private readonly SpModApiClient _spModApi = AppServices.SpModApi;
    private readonly InstalledModCardViewModel _mod;

    // Looked up once in LoadAsync from the cached catalog.
    private Mod? _catalogMod;

    public ModUpdateDialogViewModel(InstalledModCardViewModel mod)
    {
        _mod = mod;
    }

    public string ModTitle => _mod.DisplayTitle;
    public string? InstalledVersionText => _mod.InstalledVersion;

    // The matched catalog listing's sp-mod.com page; null until LoadAsync resolves it, or if no match was found.
    public string? ModPageUrl => _catalogMod?.DetailUrl;

    // Whether the dialog's Update button should be shown.
    public bool ShowUpdateButton => _mod.UpdateAvailable == true;

    // Whether the "manage installed version" controls should be shown - only meaningful once a
    // catalog mod is known, since confirming/overriding a version needs a mod to record it against.
    public bool CanManageVersion => _catalogMod is not null;

    // Whether this mod's current InstalledVersion came from a manual override, so "Clear override"
    // has something to undo.
    public bool IsManualOverride => _mod.IsManualOverride;

    public ObservableCollection<ModVersionRowViewModel> Versions { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(RedownloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmSelectedAsInstalledCommand))]
    private ModVersionRowViewModel? _selectedVersion;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SetCustomVersionCommand))]
    private string _customVersionText = string.Empty;

    // Loads the mod's version history. Called once by ModUpdateContentDialog's constructor.
    public async Task LoadAsync()
    {
        if (_mod.ModId is not { } modId)
        {
            StatusMessage = $"{_mod.DisplayTitle} isn't matched to a sp-mod.com listing, so there's nothing to check for updates against.";
            IsLoading = false;
            return;
        }

        _catalogMod = AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == modId);
        OnPropertyChanged(nameof(ModPageUrl));
        OnPropertyChanged(nameof(CanManageVersion));
        ConfirmSelectedAsInstalledCommand.NotifyCanExecuteChanged();
        MarkUpToDateCommand.NotifyCanExecuteChanged();
        SetCustomVersionCommand.NotifyCanExecuteChanged();

        IsLoading = true;
        StatusMessage = null;
        try
        {
            var installedSptVersion = AppServices.SptEnvironment.InstalledVersion;
            var result = await _spModApi.GetModVersionsAsync(
                modId.ToString(),
                new ModVersionsQuery { Sort = "-published_at", PerPage = 20 });

            Versions.Clear();
            foreach (var v in result.Data)
            {
                Versions.Add(new ModVersionRowViewModel
                {
                    Raw = v,
                    IsInstalled = _mod.InstalledVersion is not null
                        && string.Equals(v.Version, _mod.InstalledVersion, StringComparison.OrdinalIgnoreCase),
                    IsCompatible = SptVersionMatcher.IsSatisfiedBy(v.SptVersionConstraint, installedSptVersion),
                });
            }

            // Versions is fetched newest-first, so index 0 is the latest.
            if (Versions.Count > 0) Versions[0].IsLatest = true;

            // Pre-select the newest compatible version, falling back to the newest overall.
            SelectedVersion = Versions.FirstOrDefault(v => v.IsCompatible == true) ?? Versions.FirstOrDefault();
            MarkUpToDateCommand.NotifyCanExecuteChanged();

            if (Versions.Count == 0)
                StatusMessage = $"{_mod.DisplayTitle} has no published versions on sp-mod.com.";
        }
        catch (SpModApiRateLimitedException ex)
        {
            StatusMessage = $"Rate limited by sp-mod.com - try again in {ex.RetryAfter?.TotalSeconds ?? 30:N0}s.";
        }
        catch (SpModApiException ex)
        {
            StatusMessage = $"sp-mod.com error: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = $"Network error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanUpdate() => SelectedVersion is not null;

    // Queues the currently selected version for download and install.
    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private void Update() => EnqueueSelectedVersion("Update");

    // Re-queues the currently selected version (defaulting to whatever's already installed, when
    // there's no newer one) for a fresh download and reinstall. Shown in Update's place once the
    // mod is up to date, e.g. to recover from corrupted or hand-edited files.
    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private void Redownload() => EnqueueSelectedVersion("Redownload");

    private void EnqueueSelectedVersion(string verb)
    {
        if (SelectedVersion is null) return;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            StatusMessage = "No SPT install folder set - configure it on the Options page first.";
            return;
        }

        if (_catalogMod is not { } catalogMod)
        {
            StatusMessage = $"Couldn't find {_mod.DisplayTitle} in the cached catalog - try Rescan.";
            return;
        }

        if (!_mod.IsAppManaged && !Confirm(
                $"{verb} {_mod.DisplayTitle}?",
                "This mod wasn't installed through this app, so there's no record of exactly which files its current " +
                $"version placed - {verb.ToLowerInvariant()}ing installs the new version's files on top of what's already there rather " +
                "than cleanly removing the old version first. You may end up with leftover files from the old version."))
        {
            return;
        }

        // Require the mod's page to be confirmed as read before installing.
        if (!ReadModPageConfirmationWindow.Confirm(_mod.DisplayTitle, ModPageUrl))
        {
            StatusMessage = $"{verb} cancelled - {_mod.DisplayTitle}'s page wasn't confirmed as read.";
            return;
        }

        var selectedVersion = SelectedVersion;
        AppServices.DownloadQueue.Enqueue(catalogMod, selectedVersion.VersionText, installPath, () => Task.FromResult<ModVersion?>(selectedVersion.Raw));
        StatusMessage = $"Queued {_mod.DisplayTitle} {selectedVersion.VersionText} - see the Downloads page for progress.";
    }

    private static bool Confirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    // Opens ModPageUrl in the OS's default browser.
    [RelayCommand]
    private void OpenModPage()
    {
        if (string.IsNullOrWhiteSpace(ModPageUrl)) return;

        Process.Start(new ProcessStartInfo(ModPageUrl) { UseShellExecute = true });
    }

    private bool CanManageSelectedVersion() => CanManageVersion && SelectedVersion is not null;

    // Records the version already selected below as what's actually installed, without touching any
    // files - for a mod whose auto-detected version is wrong, or that has none at all.
    [RelayCommand(CanExecute = nameof(CanManageSelectedVersion))]
    private void ConfirmSelectedAsInstalled()
    {
        if (SelectedVersion is not { } selected) return;

        ApplyManualVersion(selected.VersionText, selected.Raw.Id);
        StatusMessage = $"Recorded {_mod.DisplayTitle} {selected.VersionText} as installed.";
    }

    private bool CanMarkUpToDate() => CanManageVersion && Versions.Count > 0;

    // Records the newest published version as installed, regardless of what's selected below - a
    // one-click way to clear a false "update available" without picking the version by hand.
    [RelayCommand(CanExecute = nameof(CanMarkUpToDate))]
    private void MarkUpToDate()
    {
        var latest = Versions.FirstOrDefault(v => v.IsLatest) ?? Versions.FirstOrDefault();
        if (latest is null) return;

        ApplyManualVersion(latest.VersionText, latest.Raw.Id);
        StatusMessage = $"Marked {_mod.DisplayTitle} up to date ({latest.VersionText}).";
    }

    private bool CanSetCustomVersion() => CanManageVersion && !string.IsNullOrWhiteSpace(CustomVersionText);

    // Records a free-typed version as installed - for a version that isn't in the cached list above
    // (e.g. a beta or dev build the author never published normally).
    [RelayCommand(CanExecute = nameof(CanSetCustomVersion))]
    private void SetCustomVersion()
    {
        var version = CustomVersionText.Trim();
        ApplyManualVersion(version, versionId: null);
        StatusMessage = $"Recorded {_mod.DisplayTitle} {version} as installed.";
        CustomVersionText = string.Empty;
    }

    private void ApplyManualVersion(string version, int? versionId)
    {
        if (_catalogMod is not { } catalogMod) return;

        var folders = new[] { _mod.ClientFolderName, _mod.ServerFolderName }
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        AppServices.InstallManifest.SetManualVersion(
            catalogMod.Id, catalogMod.Guid, catalogMod.Name ?? _mod.DisplayTitle, version, versionId, folders);
    }

    private bool CanClearOverride() => _mod.IsManualOverride;

    // Undoes a previous manual override, going back to auto-detecting the version from the files on
    // disk.
    [RelayCommand(CanExecute = nameof(CanClearOverride))]
    private void ClearOverride()
    {
        if (_mod.ModId is not { } modId) return;

        AppServices.InstallManifest.ClearManualVersion(modId);
        StatusMessage = $"Cleared the manual override for {_mod.DisplayTitle} - it'll go back to auto-detecting from the files on disk.";
    }
}
