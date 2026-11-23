using System.Net.Http;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.SpModApi;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class BrowseViewModel
{
    // An author's page, from the "by" line on a card (OPEN-12 A4).
    [RelayCommand]
    private void OpenAuthor(ModCardViewModel? card)
    {
        if (card?.Mod.Owner is { } owner) AppNavigation.ShowAuthor(owner.Id, owner.Name);
    }

    /// <summary>Queues <paramref name="card"/>'s mod for download and install. Picks the newest version
    /// that targets the installed SPT version when known, otherwise the newest version overall. The
    /// version's download link is resolved lazily once the queue reaches this item, so clicking
    /// Install never waits on a network call. Gated on ReadModPageConfirmationWindow first; declining
    /// leaves the card alone.</summary>
    [RelayCommand]
    private void Install(ModCardViewModel? card) =>
        QueueForDownload(card, DownloadAction.Install, AppServices.ModPageGate.DownloadOnlyFor(alternate: false));

    // The small button beside Install: the opposite of Monitor mode's setting, for this one mod.
    [RelayCommand]
    private void InstallAlternate(ModCardViewModel? card) =>
        QueueForDownload(card, DownloadAction.Install, AppServices.ModPageGate.DownloadOnlyFor(alternate: true));

    /// <summary>Re-queues an already-installed mod's currently displayed version - the same pick
    /// Install would make - for a fresh download and reinstall. Shown on the card in Install's place
    /// once a mod is installed, e.g. to recover from corrupted or hand-edited files.</summary>
    [RelayCommand]
    private void Redownload(ModCardViewModel? card) =>
        QueueForDownload(card, DownloadAction.Redownload, AppServices.ModPageGate.DownloadOnlyFor(alternate: false));

    [RelayCommand]
    private void RedownloadAlternate(ModCardViewModel? card) =>
        QueueForDownload(card, DownloadAction.Redownload, AppServices.ModPageGate.DownloadOnlyFor(alternate: true));

    [RelayCommand]
    private void MenuInstall(ModCardViewModel? card) => QueueForDownload(card, MenuAction(card), downloadOnly: false);

    [RelayCommand]
    private void MenuDownloadOnly(ModCardViewModel? card) => QueueForDownload(card, MenuAction(card), downloadOnly: true);

    private static DownloadAction MenuAction(ModCardViewModel? card) =>
        card?.IsInstalled == true ? DownloadAction.Redownload : DownloadAction.Install;

    [RelayCommand]
    private async Task MenuDetailsAsync(ModCardViewModel? card)
    {
        if (card is not null) await LoadDetailsAsync(card.Mod);
    }

    [RelayCommand]
    private void MenuOpenModPage(ModCardViewModel? card)
    {
        if (card?.Mod.DetailUrl is { Length: > 0 } url) MarkupActions.OpenInBrowser(url);
    }

    [RelayCommand]
    private void MenuIgnore(ModCardViewModel? card)
    {
        if (card is null) return;
        AppServices.Ignored.Set(card.Mod.Id, card.Mod.Name, ignore: true);
        StatusMessage = Text(Strings.Browse_IgnoredFormat, card.Mod.Name, Strings.Filter_More, Strings.Filter_IgnoredOnly);
    }

    [RelayCommand]
    private void MenuStopIgnoring(ModCardViewModel? card)
    {
        if (card is null) return;
        AppServices.Ignored.Set(card.Mod.Id, card.Mod.Name, ignore: false);
        StatusMessage = Text(Strings.Browse_UnignoredFormat, card.Mod.Name);
    }

    [RelayCommand]
    private void MenuCopyLink(ModCardViewModel? card)
    {
        if (card?.Mod.DetailUrl is { Length: > 0 } url)
            CopyToClipboard(url, Text(Strings.Browse_LinkCopiedFormat, card.Mod.Name));
    }

    [RelayCommand]
    private void MenuCopyName(ModCardViewModel? card) => Behaviors.CopyText.Copy(card?.Name);

    [RelayCommand]
    private void MenuCopyVersion(ModCardViewModel? card) => Behaviors.CopyText.Copy(card?.DisplayReleaseVersion);

    [RelayCommand]
    private void MenuCopyGuid(ModCardViewModel? card)
    {
        if (card?.Guid is { Length: > 0 } guid)
            CopyToClipboard(guid, Text(Strings.Browse_GuidCopiedFormat, card.Mod.Name));
    }

    private void CopyToClipboard(string text, string copiedMessage)
    {
        try
        {
            System.Windows.Clipboard.SetText(text);
            StatusMessage = copiedMessage;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            StatusMessage = Strings.Browse_CopyFailed;
        }
    }

    // Which of the two buttons asked, rather than the word one of them is labelled with: the
    // cancellation message is a whole sentence per action, not a verb dropped into a shared one.
    private enum DownloadAction
    {
        Install,
        Redownload,
    }

    private void QueueForDownload(ModCardViewModel? card, DownloadAction action, bool downloadOnly)
    {
        if (card is null) return;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        var mod = card.Mod;

        if (!ReadModPageConfirmationWindow.Confirm(mod.Name ?? Strings.Browse_ThisMod, mod.DetailUrl))
        {
            StatusMessage = Text(
                action == DownloadAction.Install
                    ? Strings.Browse_InstallCancelledFormat
                    : Strings.Browse_RedownloadCancelledFormat,
                mod.Name);
            return;
        }

        var installedSptVersion = AppServices.SptEnvironment.InstalledVersion;

        // Same pick the card displays, so the queued version is the one it advertised.
        var chosen = ModCardViewModel.PickDisplayVersion(mod, installedSptVersion);

        if (chosen?.Version is null)
        {
            StatusMessage = Text(Strings.Browse_NoVersionFormat, mod.Name);
            return;
        }

        // PickDisplayVersion falls back to the newest version when none supports the installed SPT -
        // the card says so in red, and installing it is asked about rather than done silently.
        if (SptCompatibility.IsIncompatible(chosen.SptVersionConstraint)
            && !SptCompatibility.ConfirmAnyway(
                [SptCompatibility.Line(mod.Name ?? Strings.Browse_ThisMod, chosen.Version, chosen.SptVersionConstraint)]))
        {
            StatusMessage = Text(Strings.Install_IncompatibleNotInstalledFormat, mod.Name, installedSptVersion);
            return;
        }

        var chosenVersion = chosen.Version;
        AppServices.DownloadQueue.Enqueue(
            InstallTarget.For(mod), chosenVersion, installPath, () => _spModApi.FindModVersionAsync(mod.Id.ToString(), chosenVersion),
            downloadOnly: downloadOnly);
        StatusMessage = Text(Strings.Browse_QueuedFormat, mod.Name, chosenVersion);
    }

    public static string? SearchTextForLink(string? text)
    {
        if (SpModPageLink.Parse(text) is not { Kind: not SpModLinkKind.List } link) return null;

        var name = link.Kind == SpModLinkKind.Mod
            ? AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == link.Id)?.Name
            : AppServices.Addons.AllAddons.FirstOrDefault(a => a.Id == link.Id)?.Name;

        if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
        return link.Slug?.Replace('-', ' ');
    }

    // OPEN-52 N21: a mod or addon from a pasted or dropped sp-mod link. False when the catalog has no such listing.
    public async Task<bool> OpenFromLinkAsync(bool isAddon, int id)
    {
        await AppServices.ModCache.EnsureLoadedAsync();

        if (!isAddon)
        {
            if (AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == id) is not { } mod) return false;
            await LoadDetailsAsync(mod);
            return true;
        }

        await AppServices.Addons.EnsureLoadedAsync();
        if (AppServices.Addons.AllAddons.FirstOrDefault(a => a.Id == id) is not { } addon) return false;

        if (addon.ModId is { } parentId && AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == parentId) is { } parent)
        {
            AppServices.ModDetailsOverlay.ShowAddon(addon, parent, FindInstalledMatch(parent)?.InstalledVersion);
            return true;
        }

        if (string.IsNullOrWhiteSpace(addon.DetailUrl)) return false;
        MarkupActions.OpenInBrowser(addon.DetailUrl);
        return true;
    }

    public async Task LoadDetailsAsync(Mod mod)
    {
        try
        {
            var details = await _spModApi.GetModAsync(mod.Id.ToString(), include: "versions,license,category");

            // The installed version is what this mod's addons check their own constraints against.
            AppServices.ModDetailsOverlay.Show(details, FindInstalledMatch(mod)?.InstalledVersion);
        }
        catch (SpModApiException ex)
        {
            StatusMessage = Text(Strings.Browse_DetailsFailedFormat, mod.Name, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = Text(Strings.Browse_DetailsNetworkFormat, mod.Name, ex.Message);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Text(Strings.Browse_DetailsTimedOutFormat, mod.Name);
        }
        catch (Exception ex)
        {
            // Last-resort catch-all so a failure here doesn't silently look like a no-op click.
            StatusMessage = Text(Strings.Browse_DetailsUnexpectedFormat, mod.Name, ex.Message);
        }
    }
}
