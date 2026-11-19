using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Views;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class InstalledViewModel
{
    // Nothing matched it to sp-mod, it has folders of its own, and no install record names them.
    public static bool CanLinkToPage(InstalledModCardViewModel mod) =>
        mod is { ModId: null, IsAppManaged: false, SharedFolderName: null } && mod.Entries.Count > 0 && !mod.Entries.Any(e => e.IsShare);

    [RelayCommand]
    private async Task LinkToPageAsync(InstalledModCardViewModel? mod)
    {
        if (mod is null || !CanLinkToPage(mod)) return;
        if (ModListNameWindow.AskForPageLink(mod.DisplayTitle) is not { Length: > 0 } text) return;

        if (SpModPageLink.Parse(text) is not { Kind: SpModLinkKind.Mod or SpModLinkKind.Addon } link)
        {
            StatusMessage = Strings.ModLink_NotAPage;
            return;
        }

        var isAddon = link.Kind == SpModLinkKind.Addon;
        var name = isAddon
            ? AppServices.Addons.ById(link.Id)?.Name
            : AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == link.Id)?.Name;
        if (name is null)
        {
            StatusMessage = Strings.ModLink_NotInCatalog;
            return;
        }

        AppServices.ModLinks.Link(mod.Entries.Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase), link.Id, isAddon);
        AppLog.Info("ModLinks", $"{mod.DisplayTitle} linked to {(isAddon ? "addon" : "mod")} {link.Id} ({name})");

        await ScanAsync();
        StatusMessage = Text(Strings.ModLink_LinkedFormat, mod.DisplayTitle, name);
    }
}
