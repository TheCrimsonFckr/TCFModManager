using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;

namespace TCFModManager.App.ViewModels;

// 
// The Addons section shown inside a mod's details dialog. Served entirely from the cached addon
// catalog - there are under a hundred addons in total, so opening a dialog never waits on a lookup
// after the first load of the session.
// 
public sealed partial class AddonsSectionViewModel : LocalizedViewModel
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    public ObservableCollection<AddonRowViewModel> Addons { get; } = [];

    public bool HasAddons => Addons.Count > 0;

    // OPEN-36 D3: raised once the addon a Browse search opened the dialog on is in the list.
    public event EventHandler<AddonRowViewModel>? FocusRequested;

    public string Heading => Text(Strings.Addon_HeadingFormat, Addons.Count);

    // Shown above the list when the parent isn't installed, so every disabled button on it has one
    // explanation rather than the same sentence repeated on each row.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasParentNotice))]
    private string? _parentNotice;

    public bool HasParentNotice => ParentNotice is not null;

    // 
    // Fills the section for one mod. <paramref name="parentInstalledVersion"/> is what every
    // addon version's constraint is measured against; null means the parent isn't installed, which
    // is shown once at the top rather than per row.
    // 
    public async Task LoadAsync(int parentModId, string? parentModName, string? parentInstalledVersion, int? focusAddonId = null)
    {
        await AppServices.Addons.EnsureLoadedAsync();

        var records = AppServices.InstallManifest.Load().Mods;
        var parentVersions = AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == parentModId)?.Versions;

        Addons.Clear();
        foreach (var addon in AppServices.Addons.ForMod(parentModId))
        {
            var record = records.FirstOrDefault(r => r.IsAddon && r.ModId == addon.Id);
            Addons.Add(new AddonRowViewModel(addon, parentModName, parentInstalledVersion, record, parentVersions));
        }

        ParentNotice = Addons.Count > 0 && string.IsNullOrWhiteSpace(parentInstalledVersion)
            ? Text(
                Strings.Addon_InstallParentNoticeFormat,
                parentModName ?? Strings.Addon_ThisMod)
            : null;

        OnPropertyChanged(nameof(HasAddons));
        OnPropertyChanged(nameof(Heading));

        if (focusAddonId is { } focus && Addons.FirstOrDefault(a => a.AddonId == focus) is { } row)
        {
            row.IsSearchMatch = true;
            FocusRequested?.Invoke(this, row);
        }
    }
}
