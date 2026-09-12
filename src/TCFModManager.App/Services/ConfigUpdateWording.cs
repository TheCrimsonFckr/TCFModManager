using System.IO;
using TCFModManager.Core.Models;

namespace TCFModManager.App.Services;

//
// The sentence a user reads after an update touched a mod's own config files. Core reports what
// happened (ConfigUpdateReport); the words live here - see feedback-core-no-user-prose.
//
public static class ConfigUpdateWording
{
    //
    // One line for the Downloads queue, or null when there is nothing worth saying.
    //
    // Nothing worth saying covers most updates: a file nobody had edited, or one the update left
    // byte-identical, is not news. Only a file that lost something the user had, or one the update
    // deliberately left alone, gets a sentence.
    //
    public static string? Summary(ConfigUpdateReport report)
    {
        var replaced = Of(report, ConfigOutcomeKind.Replaced);
        var notUpdated = Of(report, ConfigOutcomeKind.NotUpdated);
        var removed = Of(report, ConfigOutcomeKind.Removed);

        if (notUpdated.Count > 0)
        {
            return $"{Describe(notUpdated)} couldn't be copied aside, so your version was left in place "
                + "and the new one wasn't installed.";
        }

        if (replaced.Count > 0)
        {
            var firstTime = replaced.All(f => f.Reason == ConfigReplaceReason.NoBaseline);

            return $"{Describe(replaced)} replaced by this version's defaults{Where(report)}."
                + (firstTime
                    ? " The app hadn't recorded what the last version shipped, so it couldn't tell your changes from the mod's."
                    : string.Empty);
        }

        if (removed.Count > 0)
            return $"{Describe(removed)} no longer part of the mod{Where(report)}.";

        return null;
    }

    // "config.jsonc was" / "3 config files were" - so the sentence above reads either way.
    private static string Describe(List<ConfigFileOutcome> files) =>
        files.Count == 1
            ? $"Your {Path.GetFileName(files[0].Path)} was"
            : $"{files.Count} of your config files were";

    // Where the copies went, when there are any.
    private static string Where(ConfigUpdateReport report) =>
        report.ArchiveFolder is { } folder
            ? $" - your copy is in Data\\LegacyConfigs\\{Path.GetFileName(folder)}"
            : string.Empty;

    private static List<ConfigFileOutcome> Of(ConfigUpdateReport report, ConfigOutcomeKind kind) =>
        [.. report.Files.Where(f => f.Kind == kind)];
}
