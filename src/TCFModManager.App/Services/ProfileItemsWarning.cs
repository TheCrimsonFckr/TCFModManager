using System.IO;
using TCFModManager.App.Localization;
using TCFModManager.App.ViewModels;
using TCFModManager.App.Views;
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
        var language = AppLanguage.Current.TwoLetterISOLanguageName;
        var list = leaving.Where(l => l.Paths.Count > 0).ToList();
        if (list.Count == 0 || string.IsNullOrWhiteSpace(installPath)) return Task.FromResult<IReadOnlyList<ProfileLoss>>([]);

        return Task.Run(() =>
        {
            try
            {
                var losses = ProfileModItems.Check(installPath, list, language);
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

    // One line per profile and mod, "Main - Some Weapon Pack: 71 items", opening to what they are.
    public static AskSection Section(IReadOnlyList<ProfileLoss> losses) =>
        AskSection.WithDetails(Strings.ProfileItems_Heading, [.. losses
            .OrderBy(l => l.ProfileName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(l => l.ModName, StringComparer.CurrentCultureIgnoreCase)
            .Select(l => new AskLine(LocalizationService.Text(Strings.ProfileItems_LineFormat, l.ProfileName, l.ModName, Counts(l)), Things(l)))]);

    private static IReadOnlyList<string> Things(ProfileLoss loss) =>
        [.. loss.Things
            .OrderBy(t => t.Kind == ProfileThingKind.Trader ? 0 : 1)
            .ThenBy(t => t.Name is null ? 1 : 0)
            .ThenBy(t => t.Name ?? t.Id, StringComparer.CurrentCultureIgnoreCase)
            .Select(Thing)];

    private static string Thing(ProfileLostThing thing)
    {
        var name = thing.Name ?? LocalizationService.Text(Strings.ProfileItems_UnnamedFormat, thing.Id);
        if (thing.Kind == ProfileThingKind.Trader) return LocalizationService.Text(Strings.ProfileItems_TraderFormat, name);
        return thing.Count > 1 ? LocalizationService.Text(Strings.ProfileItems_ItemCountFormat, name, thing.Count) : name;
    }

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
            Sections = [Section(losses)],
            Detail = detail,
            First = goAhead,
            Danger = Views.AskChoice.First,
            Default = Views.AskChoice.Cancel,
        }) == Views.AskChoice.First;
    }
}
