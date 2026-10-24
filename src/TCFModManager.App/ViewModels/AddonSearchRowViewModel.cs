using TCFModManager.App.Localization;
using TCFModManager.Core.Models;

namespace TCFModManager.App.ViewModels;

// One addon in Browse's Addons strip while searching (OPEN-36 D2). Parent is null for a detached addon.
public sealed class AddonSearchRowViewModel(Addon addon, Mod? parent)
{
    public Addon Addon => addon;

    public Mod? Parent => parent;

    public string Name => addon.Name ?? LocalizationService.Text(Strings.Addon_NameFormat, addon.Id);

    public string? Thumbnail => string.IsNullOrWhiteSpace(addon.Thumbnail) ? null : addon.Thumbnail;

    public string Subtitle => parent is null
        ? Strings.Browse_AddonDetached
        : LocalizationService.Text(Strings.Browse_AddonForFormat, parent.Name);
}
