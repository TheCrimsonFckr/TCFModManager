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

    // Looked up once in LoadAsync from the cached catalog. Exactly one of these is ever set - an
    // installed card is either a mod or an addon, never both.
    private Mod? _catalogMod;
    private Addon? _catalogAddon;

    public ModUpdateDialogViewModel(InstalledModCardViewModel mod)
    {
        _mod = mod;
    }

    // The addons published for this mod, shown under its version list. An addon has none of its
    // own, and its id would otherwise be read as a mod id - so the section stays empty for one.
    public AddonsSectionViewModel Addons { get; } = new();

    public string ModTitle => _mod.DisplayTitle;
    public string? InstalledVersionText => _mod.InstalledVersion;

    // The matched catalog listing's sp-mod.com page; null until LoadAsync resolves it, or if no match was found.
    public string? ModPageUrl => _mod.IsAddon ? _catalogAddon?.DetailUrl : _catalogMod?.DetailUrl;

    //
    // A disabled mod's install record points at folders it no longer occupies, so updating or
    // redownloading it would place files where nothing is loading them and leave the old copy
    // behind in the ".disabled" folder. Both buttons are hidden until it's enabled again.
    //
    public bool IsModDisabled => _mod.IsDisabled;

    public string DisabledNotice =>
        $"{_mod.DisplayTitle} is disabled. Enable it on the Installed page to update or redownload it.";

    //
    // Which button is shown is decided by the SELECTED version against the installed one, not by
    // the mod's own UpdateAvailable flag.
    //
    // It used to be the flag, which meant the label described the mod rather than the action: with
    // a newer version published, picking an older one from the list and pressing the button still
    // said "Update" while installing a downgrade. The list has always let you choose any version -
    // only the wording was wrong.
    //
    private bool CanAct => SelectedVersion is not null && !_mod.IsDisabled;

    public bool ShowUpdateButton =>
        CanAct && ModVersionComparer.IsUpdateAvailable(InstalledVersionText, SelectedVersion?.VersionText) == true;

    public bool ShowDowngradeButton =>
        CanAct && ModVersionComparer.IsUpdateAvailable(SelectedVersion?.VersionText, InstalledVersionText) == true;

    //
    // The fallback: the selected version is the one installed, or the two can't be compared. Either
    // way "Redownload" is the honest word - it re-fetches whatever is selected and claims no
    // direction, which is what the app actually knows when a version string won't parse.
    //
    public bool ShowRedownloadButton => CanAct && !ShowUpdateButton && !ShowDowngradeButton;

    // Whether the "manage installed version" controls should be shown - only meaningful once a
    // catalog mod is known, since confirming/overriding a version needs a mod to record it against.
    public bool CanManageVersion => _mod.IsAddon ? _catalogAddon is not null : _catalogMod is not null;

    // Whether this mod's current InstalledVersion came from a manual override, so "Clear override"
    // has something to undo.
    public bool IsManualOverride => _mod.IsManualOverride;

    // Every version fetched. Versions is the filtered view of this - see RepopulateVersions.
    private readonly List<ModVersionRowViewModel> _allVersions = [];

    public ObservableCollection<ModVersionRowViewModel> Versions { get; } = [];

    //
    // Versions built for an SPT release you don't have are hidden by default: with 4.1 out, a 4.0
    // install looking at a mod's history sees a run of newer versions it cannot use, and the
    // obvious reading is "I'm out of date" rather than "these aren't for me".
    //
    // They are hidden rather than dropped. The count and the reason stay on screen and one click
    // brings them back, because silently shortening the list would leave someone believing the
    // newest version is the newest that exists.
    //
    [ObservableProperty]
    private bool _showIncompatibleVersions;

    public int HiddenVersionCount => _allVersions.Count - Versions.Count;

    public bool ShowVersionFilterNotice => HiddenVersionCount > 0 || ShowIncompatibleVersions;

    public string HiddenVersionsNotice => HiddenVersionCount switch
    {
        0 => "Showing every published version, including ones for other SPT releases.",
        1 => "1 version is hidden - it targets a different SPT release to the one you have.",
        _ => $"{HiddenVersionCount} versions are hidden - they target a different SPT release to the one you have.",
    };

    partial void OnShowIncompatibleVersionsChanged(bool value) => RepopulateVersions();

    //
    // An addon's rows are judged against its PARENT MOD's version, not against SPT, and the rule
    // this app already settled for addons is to show everything with the reason attached rather
    // than filter any of it out - so the hiding applies to mods only.
    //
    // The installed version is never hidden whatever it targets: you have to be able to see what
    // you are running. An unknown constraint is not proof of anything, so it stays too.
    //
    private bool IsShown(ModVersionRowViewModel row) =>
        ShowIncompatibleVersions || _mod.IsAddon || row.IsInstalled || row.IsCompatible != false;

    private void RepopulateVersions()
    {
        Versions.Clear();
        foreach (var row in _allVersions.Where(IsShown)) Versions.Add(row);

        OnPropertyChanged(nameof(HiddenVersionCount));
        OnPropertyChanged(nameof(ShowVersionFilterNotice));
        OnPropertyChanged(nameof(HiddenVersionsNotice));

        SelectedVersion = Versions.FirstOrDefault(v => v.IsCompatible == true) ?? Versions.FirstOrDefault();
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(RedownloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(DowngradeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmSelectedAsInstalledCommand))]
    [NotifyPropertyChangedFor(nameof(ShowUpdateButton))]
    [NotifyPropertyChangedFor(nameof(ShowDowngradeButton))]
    [NotifyPropertyChangedFor(nameof(ShowRedownloadButton))]
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

        if (_mod.IsAddon)
        {
            await LoadAddonAsync(modId);
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

            _allVersions.Clear();
            foreach (var v in result.Data)
            {
                _allVersions.Add(new ModVersionRowViewModel
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
            RepopulateVersions();
            MarkUpToDateCommand.NotifyCanExecuteChanged();

            if (Versions.Count == 0)
                StatusMessage = $"{_mod.DisplayTitle} has no published versions on sp-mod.com.";

            await Addons.LoadAsync(modId, _catalogMod?.Name ?? _mod.DisplayTitle, _mod.InstalledVersion);
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

    //
    // An addon's version list comes from the cached addon catalog rather than a per-addon fetch:
    // there are under a hundred addons in total, the cache already carries each one's versions with
    // their download links, and every constraint here is measured against the parent mod's
    // installed version rather than the installed SPT version.
    //
    private async Task LoadAddonAsync(int addonId)
    {
        IsLoading = true;
        StatusMessage = null;
        try
        {
            await AppServices.Addons.EnsureLoadedAsync();
            _catalogAddon = AppServices.Addons.ById(addonId);

            OnPropertyChanged(nameof(ModPageUrl));
            OnPropertyChanged(nameof(CanManageVersion));
            ConfirmSelectedAsInstalledCommand.NotifyCanExecuteChanged();
            SetCustomVersionCommand.NotifyCanExecuteChanged();

            if (_catalogAddon is null)
            {
                StatusMessage = $"{_mod.DisplayTitle} is no longer listed on sp-mod.com, so there's nothing to check for updates against.";
                return;
            }

            var parentVersion = _mod.ParentInstalledVersion;
            var parentName = _mod.ParentModName ?? "its parent mod";

            var ordered = (_catalogAddon.Versions ?? [])
                .OrderByDescending(v => v.PublishedAt ?? DateTimeOffset.MinValue)
                .ToList();

            _allVersions.Clear();
            foreach (var v in ordered)
            {
                _allVersions.Add(new ModVersionRowViewModel
                {
                    Raw = new ModVersion
                    {
                        Id = v.Id,
                        Version = v.Version,
                        Description = v.Description,
                        Link = v.Link,
                        ContentLength = v.ContentLength,
                        Downloads = v.Downloads,
                        PublishedAt = v.PublishedAt,
                    },
                    IsInstalled = _mod.InstalledVersion is not null
                        && string.Equals(v.Version, _mod.InstalledVersion, StringComparison.OrdinalIgnoreCase),
                    IsCompatible = ModVersionMatcher.IsSatisfiedBy(v.ModVersionConstraint, parentVersion),
                    ParentRequirement = $"{parentName} {v.ModVersionConstraint}",
                });
            }

            if (Versions.Count > 0) Versions[0].IsLatest = true;

            RepopulateVersions();
            MarkUpToDateCommand.NotifyCanExecuteChanged();

            if (Versions.Count == 0)
                StatusMessage = $"{_mod.DisplayTitle} has no published versions on sp-mod.com.";
            else if (string.IsNullOrWhiteSpace(parentVersion))
                StatusMessage = $"{_mod.ParentModName ?? "This addon's parent mod"} isn't installed, so none of these versions can be checked for fit.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanUpdate() => SelectedVersion is not null && !_mod.IsDisabled;

    // Queues the currently selected version for download and install.
    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private void Update() => EnqueueSelectedVersion("Update");

    // Re-queues the currently selected version (defaulting to whatever's already installed, when
    // there's no newer one) for a fresh download and reinstall. Shown in Update's place once the
    // mod is up to date, e.g. to recover from corrupted or hand-edited files.
    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private void Redownload() => EnqueueSelectedVersion("Redownload");

    //
    // Installs an older version over a newer one. Confirmed first, and separately from the
    // hand-installed warning inside EnqueueSelectedVersion, because the risk is a different one:
    // that warning is about leftover FILES, this is about DATA the newer version has already
    // written and the older one may not understand.
    //
    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private void Downgrade()
    {
        if (SelectedVersion is not { } selected) return;

        if (!Confirm(
                $"Downgrade {_mod.DisplayTitle} to {selected.VersionText}?",
                $"You have {InstalledVersionText ?? "a newer version"} installed, and this will replace it with "
                + $"{selected.VersionText}.\n\n"
                + "Replacing the files is the easy half. What this app cannot undo is anything the newer version "
                + "has already written - its own config files, and any changes it made to your SPT profile. An "
                + "older build may not read those back, and some mods change their data format between versions, "
                + "which can leave a profile the older version refuses to load.\n\n"
                + "Back up your profile before continuing if it matters to you."))
        {
            StatusMessage = "Downgrade cancelled.";
            return;
        }

        EnqueueSelectedVersion("Downgrade");
    }

    //
    // "Update" -> "updating", not "updateing". The verb is reused in this sentence, and a silent
    // trailing "e" made the old wording wrong for two of the three buttons.
    //
    private static string Gerund(string verb)
    {
        var lower = verb.ToLowerInvariant();
        return lower.EndsWith('e') ? lower[..^1] + "ing" : lower + "ing";
    }

    private void EnqueueSelectedVersion(string verb)
    {
        if (SelectedVersion is null) return;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            StatusMessage = "No SPT install folder set - configure it on the Options page first.";
            return;
        }

        var target = _mod.IsAddon
            ? _catalogAddon is { } addon ? InstallTarget.For(addon) : null
            : _catalogMod is { } catalogMod ? InstallTarget.For(catalogMod) : null;

        if (target is null)
        {
            StatusMessage = $"Couldn't find {_mod.DisplayTitle} in the cached catalog - try Rescan.";
            return;
        }

        if (!_mod.IsAppManaged && !Confirm(
                $"{verb} {_mod.DisplayTitle}?",
                "This mod wasn't installed through this app, so there's no record of exactly which files its current " +
                $"version placed - {Gerund(verb)} installs the selected version's files on top of what's already there rather " +
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
        AppServices.DownloadQueue.Enqueue(
            target, selectedVersion.VersionText, installPath, () => Task.FromResult<ModVersion?>(selectedVersion.Raw),
            totalBytes: selectedVersion.Raw.ContentLength);
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
        if (!CanManageVersion) return;

        var folders = new[] { _mod.ClientFolderName, _mod.ServerFolderName }
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (_mod.IsAddon)
        {
            if (_catalogAddon is not { } addon) return;

            AppServices.InstallManifest.SetManualVersion(
                addon.Id, guid: null, addon.Name ?? _mod.DisplayTitle, version, versionId, folders, isAddon: true);
            return;
        }

        if (_catalogMod is not { } catalogMod) return;

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

        AppServices.InstallManifest.ClearManualVersion(modId, _mod.IsAddon);
        StatusMessage = $"Cleared the manual override for {_mod.DisplayTitle} - it'll go back to auto-detecting from the files on disk.";
    }
}
