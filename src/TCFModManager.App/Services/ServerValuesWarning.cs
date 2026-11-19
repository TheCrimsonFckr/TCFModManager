using TCFModManager.App.Localization;
using TCFModManager.App.ViewModels;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

// Mods about to be turned off whose server half adds values of its own (SPT 4.1 enum prepatches).
internal static class ServerValuesWarning
{
    public static Task<IReadOnlyList<string>> AddingAsync(IEnumerable<InstalledModCardViewModel> cards)
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        var list = cards.ToList();
        if (list.Count == 0 || string.IsNullOrWhiteSpace(installPath)) return Task.FromResult<IReadOnlyList<string>>([]);

        return Task.Run(() =>
        {
            try
            {
                var folders = ServerPrepatches.Read(installPath).Select(p => p.Guid).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (folders.Count == 0) return (IReadOnlyList<string>)[];

                return list
                    .Where(c => c.Entries.Any(e => !e.IsDisabled && e.Target == InstalledModTarget.Server && e.AllGuids.Any(folders.Contains)))
                    .Select(c => c.DisplayTitle)
                    .Distinct()
                    .ToList();
            }
            catch (Exception ex)
            {
                AppLog.Warn("Profiles", $"server prepatch check failed: {ex.Message}");
                return (IReadOnlyList<string>)[];
            }
        });
    }

    public static string? Note(IReadOnlyList<string> adding) =>
        adding.Count == 0 ? null : LocalizationService.Text(Strings.ServerValues_NoteFormat, TextLists.Join(adding));

    public static bool Confirm(IReadOnlyList<string> adding) =>
        AskWindow.Show(new AskRequest
        {
            Title = Strings.ServerValues_Title,
            Warning = Note(adding),
            Lead = Strings.ProfileItems_Lead,
            First = Strings.ProfileItems_DisableAnyway,
            Danger = AskChoice.First,
            Default = AskChoice.Cancel,
        }) == AskChoice.First;
}
