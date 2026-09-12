using System.IO;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

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

        var merged = Of(report, ConfigOutcomeKind.Merged);

        if (merged.Count > 0 && replaced.Count == 0)
        {
            var carried = merged.Sum(f => f.Carried.Count);
            var lost = merged.Sum(f => f.Dropped.Count + f.UserAdded.Count);

            return $"Your settings were carried into the new defaults ({carried} of them)"
                + (lost > 0 ? $"; {lost} could not be and are listed on the Configs page." : ".");
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

    //
    // What the last update did to one file, for the Configs page - read days later, so it names the
    // versions rather than saying "the last update".
    //
    public static string LastUpdateNote(ConfigUpdateReport report, ConfigFileOutcome outcome)
    {
        var move = report.FromVersion is { } from ? $"{from} to {report.ToVersion}" : report.ToVersion;

        return outcome.Kind switch
        {
            ConfigOutcomeKind.Merged =>
                $"Updating to {move} kept {Settings(outcome.Carried.Count)} of yours in this file"
                + Also(outcome) + ".",
            ConfigOutcomeKind.Replaced =>
                $"Updating to {move} replaced this file with the version's defaults{Where(report)}."
                + (outcome.Reason == ConfigReplaceReason.NoBaseline
                    ? " The app had nothing to compare it against yet; the next update can carry your changes."
                    : outcome.Reason == ConfigReplaceReason.TakeNewPolicy
                        ? " This mod is set to take the new file."
                        : outcome.Reason == ConfigReplaceReason.NotMergeable
                            ? " Its format is not one the merge can read - set this mod to keep yours if you edit it."
                            : outcome.Reason == ConfigReplaceReason.TooLarge
                                ? " It holds too much to be settings, so it was left as the mod shipped it."
                                : string.Empty),
            ConfigOutcomeKind.KeptMine => $"Updating to {move} left this file exactly as it was, as you asked.",
            ConfigOutcomeKind.DefaultsUpdated => $"Updating to {move} brought this file's defaults with it. Nothing of yours was in it.",
            ConfigOutcomeKind.NotUpdated => $"Updating to {move} could not copy this file aside, so it was left alone.",
            ConfigOutcomeKind.Added => $"This file arrived with {report.ToVersion}.",
            _ => $"Updating to {move} left this file unchanged.",
        };
    }

    // What each policy means, in the one line under the dropdown.
    public static string PolicyNote(ModConfigPolicy policy) => policy switch
    {
        ModConfigPolicy.KeepMine =>
            "Your file is left exactly as it is. A setting the new version adds will be missing from it, "
            + "which some mods handle and some don't.",
        ModConfigPolicy.TakeNew =>
            "The new version's file wins. Your copy is kept in Data\\LegacyConfigs so you can put values back by hand.",
        _ =>
            "The new version's file is used, with the settings you changed carried into it. A setting the update "
            + "adds arrives at its default; one it removes is reported and dropped.",
    };

    private static string Settings(int count) => count == 1 ? "1 setting" : $"{count} settings";

    // The rest of a merge's story, only when there is one.
    private static string Also(ConfigFileOutcome outcome)
    {
        var parts = new List<string>();
        if (outcome.Dropped.Count > 0) parts.Add($"{outcome.Dropped.Count} the update no longer has");
        if (outcome.UserAdded.Count > 0) parts.Add($"{outcome.UserAdded.Count} you added yourself, which it could not place");

        return parts.Count == 0 ? string.Empty : $", and reported {string.Join(" and ", parts)}";
    }

    private static List<ConfigFileOutcome> Of(ConfigUpdateReport report, ConfigOutcomeKind kind) =>
        [.. report.Files.Where(f => f.Kind == kind)];
}
