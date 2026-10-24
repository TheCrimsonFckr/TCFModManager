using TCFModManager.Core.Models;

namespace TCFModManager.App.ViewModels;

// What the details dialog is being asked to show: the mod, and the version of it currently
// installed. The installed version is what the mod's addons measure their own constraints against,
// so it travels with the request rather than being looked up again inside the dialog.
public sealed record ModDetailsRequest(Mod Mod, string? InstalledVersion);

// One addon on its own (OPEN-36 D3): the addon, its mod, and the version of that mod installed.
public sealed record AddonDetailsRequest(Addon Addon, Mod Parent, string? ParentInstalledVersion);

// Shared, app-lifetime signal for showing mod details. MainWindow subscribes to Requested and displays the details dialog.
public sealed class ModDetailsOverlayViewModel
{
    public event EventHandler<ModDetailsRequest>? Requested;

    public void Show(Mod mod, string? installedVersion = null) =>
        Requested?.Invoke(this, new ModDetailsRequest(mod, installedVersion));

    public event EventHandler<AddonDetailsRequest>? AddonRequested;

    public void ShowAddon(Addon addon, Mod parent, string? parentInstalledVersion) =>
        AddonRequested?.Invoke(this, new AddonDetailsRequest(addon, parent, parentInstalledVersion));
}
