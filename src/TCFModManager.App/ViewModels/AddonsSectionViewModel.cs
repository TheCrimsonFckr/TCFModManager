using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.Core.Models;

namespace TCFModManager.App.ViewModels;

// 
// The Addons section shown inside a mod's details dialog. Served entirely from the cached addon
// catalog - there are under a hundred addons in total, so opening a dialog never waits on a lookup
// after the first load of the session.
// 
public sealed partial class AddonsSectionViewModel : ObservableObject
{
    public ObservableCollection<AddonRowViewModel> Addons { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAddons))]
    [NotifyPropertyChangedFor(nameof(Heading))]
    private bool _isLoaded;

    public bool HasAddons => Addons.Count > 0;

    public string Heading => Addons.Count == 1 ? "1 addon" : $"{Addons.Count} addons";

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
    public async Task LoadAsync(int parentModId, string? parentModName, string? parentInstalledVersion)
    {
        await AppServices.Addons.EnsureLoadedAsync();

        var records = AppServices.InstallManifest.Load().Mods;

        Addons.Clear();
        foreach (var addon in AppServices.Addons.ForMod(parentModId))
        {
            var record = records.FirstOrDefault(r => r.IsAddon && r.ModId == addon.Id);
            Addons.Add(new AddonRowViewModel(addon, parentModName, parentInstalledVersion, record));
        }

        ParentNotice = Addons.Count > 0 && string.IsNullOrWhiteSpace(parentInstalledVersion)
            ? $"Install {parentModName ?? "this mod"} first - an addon needs its parent mod's version to know which of its own versions fit."
            : null;

        IsLoaded = true;
        OnPropertyChanged(nameof(HasAddons));
        OnPropertyChanged(nameof(Heading));
    }
}
