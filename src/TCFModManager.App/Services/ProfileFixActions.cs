using TCFModManager.App.Localization;
using TCFModManager.App.Views;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

// OPEN-52 N29: asks, then turns on SPT's own profile clean-up in this install's core.json.
public static class ProfileFixActions
{
    public static SptProfileFixState StateFor(string? installPath) =>
        SptProfileFixes.Read(SptProfileFixes.ConfigPath(installPath));

    // The line to show afterwards, or null when the user said no.
    public static string? AskAndTurnOn(string? installPath)
    {
        if (SptProfileFixes.ConfigPath(installPath) is not { } path) return Strings.ProfileFix_NoCoreJson;

        if (AskWindow.Show(new AskRequest
            {
                Title = Strings.ProfileFix_Title,
                Lead = Strings.ProfileFix_Lead,
                Detail = [LocalizationService.Text(Strings.ProfileFix_DetailFormat, path, path + SptProfileFixes.BackupSuffix)],
                First = Strings.ProfileFix_TurnOn,
                Default = AskChoice.Cancel,
            }) != AskChoice.First)
        {
            return null;
        }

        return SptProfileFixes.TurnOn(path) switch
        {
            SptProfileFixOutcome.TurnedOn => Strings.ProfileFix_Done,
            SptProfileFixOutcome.NotChangeable => Strings.ProfileFix_NotChangeable,
            _ => Strings.ProfileFix_Failed,
        };
    }
}
