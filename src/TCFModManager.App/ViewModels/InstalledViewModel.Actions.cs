using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class InstalledViewModel
{
    /// <summary>Opens the mod details/update dialog for whichever card was clicked. Routed through
    /// AppServices.ModUpdateOverlay since only MainWindow owns the dialog presenter. Always rescans once
    /// the dialog closes, staying on the current page.</summary>
    [RelayCommand]
    private async Task ShowDetailsAsync(InstalledModCardViewModel? mod)
    {
        if (mod is null) return;
        if (AppServices.ModUpdateOverlay.ShowAsync is not { } show) return;

        // Only when the dialog actually did something. A rescan re-reads the whole install and
        // replaces every card, which visibly rebuilds the page - not something opening a mod to
        // read its changelog should cause.
        if (await show(mod, AddonCardsOf(mod))) await ScanAsync();
    }

    //
    // Opens a mod's folder in Explorer. The path comes from the card rather than being rebuilt
    // here, so it is the folder the scan actually found - including the disabled location, if the
    // mod is currently disabled.
    //
    [RelayCommand]
    private void OpenModFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        // Checked rather than assumed: a folder removed or renamed outside the app since the last
        // scan would otherwise open an Explorer error the user has to dismiss.
        if (!Directory.Exists(path))
        {
            StatusMessage = Text(Strings.Installed_FolderGoneFormat, path);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn("Installed", $"couldn't open {path}: {ex.Message}");
            StatusMessage = Strings.Common_FolderOpenFailed;
        }
    }

    // ---- OPEN-12 A5: the right-click menu's actions that have no command of their own ------------

    // View mod page: the matched sp-mod listing (or addon), when there is one.
    // OPEN-12 A4: the author's page, from the name on a card or List row, or the right-click menu.
    [RelayCommand]
    private void OpenAuthor(InstalledModCardViewModel? mod)
    {
        if (mod?.AuthorId is { } id) AppNavigation.ShowAuthor(id, mod.Author);
    }

    // OPEN-12 A4 (R23): Follow / Unfollow the author, from the right-click menu.
    [RelayCommand]
    private void ToggleFollowAuthor(InstalledModCardViewModel? mod)
    {
        if (mod?.AuthorId is { } id) AppServices.Followed.Toggle(id, mod.Author);
    }

    public static string? ModPageUrl(InstalledModCardViewModel mod) =>
        mod.ModId is not { } id
            ? null
            : mod.IsAddon
                ? AppServices.Addons.AllAddons.FirstOrDefault(a => a.Id == id)?.DetailUrl
                : AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == id)?.DetailUrl;

    [RelayCommand]
    private void OpenModPage(InstalledModCardViewModel? mod)
    {
        if (mod is null || ModPageUrl(mod) is not { } url) return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn("Installed", $"couldn't open {url}: {ex.Message}");
        }
    }
}
