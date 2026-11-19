using System.IO;
using TCFModManager.App.Localization;
using TCFModManager.App.ViewModels;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

// OPEN-53 N7: which of this install's profiles hold items or traders from mods about to go, in words.
internal static class ProfileItemsWarning
{
    public static Task<IReadOnlyList<ProfileLoss>> ForCardsAsync(IEnumerable<InstalledModCardViewModel> cards) =>
        ForAsync(cards
            .Where(c => c.SharedFolderName is null)
            .Select(c => (c.DisplayTitle, (IReadOnlyList<string>)c.Entries.Where(e => !e.IsDisabled).Select(e => e.FolderPath).ToList())));

    public static Task<IReadOnlyList<ProfileLoss>> ForListAsync(string installPath, IEnumerable<ModListAction> disabling) =>
        ForAsync(disabling
            .Where(a => a.Installed is { CanBeDisabled: true, IsDisabled: false })
            .Select(a => (a.Name, (IReadOnlyList<string>)a.Installed!.Folders
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .SelectMany(f => DisabledModPaths.ServerContainers(installPath).Select(c => Path.Combine(c, f)))
                .Where(Directory.Exists)
                .ToList())));

    public static Task<IReadOnlyList<ProfileLoss>> ForAsync(IEnumerable<(string Mod, IReadOnlyList<string> Paths)> leaving)
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        var list = leaving.Where(l => l.Paths.Count > 0).ToList();
        if (list.Count == 0 || string.IsNullOrWhiteSpace(installPath)) return Task.FromResult<IReadOnlyList<ProfileLoss>>([]);

        return Task.Run(() =>
        {
            try
            {
                var losses = ProfileModItems.Check(installPath, list);
                if (losses.Count > 0)
                    AppLog.Info("Profiles", "profiles holding items from mods about to go: " + string.Join("; ", losses.Select(l => $"{l.ProfileName} / {l.ModName}: {l.Items} items, {l.Traders} traders")));
                return losses;
            }
            catch (Exception ex)
            {
                AppLog.Warn("Profiles", $"profile check failed: {ex.Message}");
                return (IReadOnlyList<ProfileLoss>)[];
            }
        });
    }

    // One line per profile and mod: "Main - Some Weapon Pack: 71 items".
    public static IReadOnlyList<string> Lines(IReadOnlyList<ProfileLoss> losses) =>
        [.. losses
            .OrderBy(l => l.ProfileName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(l => l.ModName, StringComparer.CurrentCultureIgnoreCase)
            .Select(l => LocalizationService.Text(Strings.ProfileItems_LineFormat, l.ProfileName, l.ModName, Counts(l)))];

    private static string Counts(ProfileLoss loss)
    {
        var parts = new List<string>();
        if (loss.Items > 0) parts.Add(Strings.ProfileItems_Items(loss.Items, loss.Items));
        if (loss.Traders > 0) parts.Add(Strings.ProfileItems_Traders(loss.Traders, loss.Traders));
        return string.Join(Strings.Common_ListSeparator, parts);
    }

    // Before a disable or a list apply: true to go ahead. Cancel is the default.
    public static bool Confirm(IReadOnlyList<ProfileLoss> losses, string goAhead, IReadOnlyList<string>? extra = null)
    {
        var detail = new List<string> { Strings.ProfileItems_Outro };
        if (extra is not null) detail.AddRange(extra);

        return Views.AskWindow.Show(new Views.AskRequest
        {
            Title = Strings.ProfileItems_Title,
            Warning = Strings.ProfileItems_Intro,
            Lead = Strings.ProfileItems_Lead,
            Sections = [new(Strings.ProfileItems_Heading, Lines(losses))],
            Detail = detail,
            First = goAhead,
            Danger = Views.AskChoice.First,
            Default = Views.AskChoice.Cancel,
        }) == Views.AskChoice.First;
    }
}
