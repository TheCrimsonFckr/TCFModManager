using System.Globalization;
using System.IO;
using TCFModManager.App.Localization;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// The sentence a user reads after an update touched a mod's own config files. Core reports what
// happened (ConfigUpdateReport); the words live here - see feedback-core-no-user-prose.
//
// Every sentence here is one key, start to finish. This file used to assemble them from pieces -
// a clause carrying its own verb agreement ("Your config.jsonc was" / "3 of your config files
// were"), a trailing "- your copy is in ..." glued on where the archive folder existed, a list
// joined with " and " - which is the shape D8 rules out: the halves cannot be reordered, the verb
// cannot agree, and a translator is handed sentence fragments with nothing to attach them to.
// One key per whole sentence costs more keys and is the only thing that can be translated.
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
            return notUpdated.Count == 1
                ? Format(Strings.ConfigUpdate_NotUpdatedNamedFormat, FileName(notUpdated))
                : Format(Strings.ConfigUpdate_NotUpdatedCountFormat, notUpdated.Count);
        }

        var merged = Of(report, ConfigOutcomeKind.Merged);

        if (merged.Count > 0 && replaced.Count == 0)
        {
            var carried = merged.Sum(f => f.Carried.Count);
            var lost = merged.Sum(f => f.Dropped.Count + f.UserAdded.Count);

            return lost > 0
                ? Format(Strings.ConfigUpdate_MergedWithLostFormat, carried, lost)
                : Format(Strings.ConfigUpdate_MergedFormat, carried);
        }

        if (replaced.Count > 0)
        {
            var sentence = Archive(report) is { } folder
                ? replaced.Count == 1
                    ? Format(Strings.ConfigUpdate_ReplacedOneWhereFormat, FileName(replaced), folder)
                    : Format(Strings.ConfigUpdate_ReplacedManyWhereFormat, replaced.Count, folder)
                : replaced.Count == 1
                    ? Format(Strings.ConfigUpdate_ReplacedNamedFormat, FileName(replaced))
                    : Format(Strings.ConfigUpdate_ReplacedCountFormat, replaced.Count);

            // A second sentence rather than a clause: appending one finished sentence to another is
            // the one kind of assembly that survives translation.
            return replaced.All(f => f.Reason == ConfigReplaceReason.NoBaseline)
                ? Then(sentence, Strings.ConfigUpdate_ReplacedFirstTime)
                : sentence;
        }

        if (removed.Count > 0)
        {
            return Archive(report) is { } folder
                ? removed.Count == 1
                    ? Format(Strings.ConfigUpdate_RemovedOneWhereFormat, FileName(removed), folder)
                    : Format(Strings.ConfigUpdate_RemovedManyWhereFormat, removed.Count, folder)
                : removed.Count == 1
                    ? Format(Strings.ConfigUpdate_RemovedNamedFormat, FileName(removed))
                    : Format(Strings.ConfigUpdate_RemovedCountFormat, removed.Count);
        }

        return null;
    }

    //
    // What the last update did to one file, for the Configs page - read days later, so it names the
    // versions rather than saying "the last update".
    //
    public static string LastUpdateNote(ConfigUpdateReport report, ConfigFileOutcome outcome)
    {
        var move = report.FromVersion is { } from
            ? Format(Strings.ConfigUpdate_VersionRangeFormat, from, report.ToVersion)
            : report.ToVersion;

        return outcome.Kind switch
        {
            ConfigOutcomeKind.Merged => Then(
                Strings.ConfigUpdate_NoteMerged(outcome.Carried.Count, move, outcome.Carried.Count),
                Also(outcome)),

            ConfigOutcomeKind.Replaced => Then(
                Archive(report) is { } folder
                    ? Format(Strings.ConfigUpdate_NoteReplacedWhereFormat, move, folder)
                    : Format(Strings.ConfigUpdate_NoteReplacedFormat, move),
                Reason(outcome.Reason)),

            ConfigOutcomeKind.KeptMine => Format(Strings.ConfigUpdate_NoteKeptMineFormat, move),
            ConfigOutcomeKind.DefaultsUpdated => Format(Strings.ConfigUpdate_NoteDefaultsUpdatedFormat, move),
            ConfigOutcomeKind.Preserved => Format(Strings.ConfigUpdate_NotePreservedFormat, move),
            ConfigOutcomeKind.NotUpdated => Format(Strings.ConfigUpdate_NoteNotUpdatedFormat, move),
            ConfigOutcomeKind.Added => Format(Strings.ConfigUpdate_NoteAddedFormat, report.ToVersion),
            _ => Format(Strings.ConfigUpdate_NoteUnchangedFormat, move),
        };
    }

    // What each policy means, in the one line under the dropdown.
    public static string PolicyNote(ModConfigPolicy policy) => policy switch
    {
        ModConfigPolicy.KeepMine => Strings.ConfigUpdate_PolicyKeepMine,
        ModConfigPolicy.TakeNew => Strings.ConfigUpdate_PolicyTakeNew,
        _ => Strings.ConfigUpdate_PolicyMerge,
    };

    //
    // The rest of a merge's story, only when there is one. Three whole sentences rather than a
    // clause built by joining parts with " and ": which conjunction, where it goes and what
    // punctuation surrounds it are all per-language.
    //
    private static string? Also(ConfigFileOutcome outcome) =>
        (outcome.Dropped.Count, outcome.UserAdded.Count) switch
        {
            ( > 0, > 0) => Format(
                Strings.ConfigUpdate_AlsoBothFormat, outcome.Dropped.Count, outcome.UserAdded.Count),
            ( > 0, _) => Format(Strings.ConfigUpdate_AlsoDroppedFormat, outcome.Dropped.Count),
            (_, > 0) => Format(Strings.ConfigUpdate_AlsoAddedFormat, outcome.UserAdded.Count),
            _ => null,
        };

    // Nullable: a replacement that recorded no reason falls through to no second sentence.
    private static string? Reason(ConfigReplaceReason? reason) => reason switch
    {
        ConfigReplaceReason.NoBaseline => Strings.ConfigUpdate_ReasonNoBaseline,
        ConfigReplaceReason.TakeNewPolicy => Strings.ConfigUpdate_ReasonTakeNew,
        ConfigReplaceReason.NotMergeable => Strings.ConfigUpdate_ReasonNotMergeable,
        ConfigReplaceReason.TooLarge => Strings.ConfigUpdate_ReasonTooLarge,
        _ => null,
    };

    // Sentence, space, sentence - and nothing at all when there is no second one.
    private static string Then(string sentence, string? next) =>
        string.IsNullOrEmpty(next) ? sentence : sentence + " " + next;

    private static string Format(string format, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, format, values);

    private static string FileName(List<ConfigFileOutcome> files) => Path.GetFileName(files[0].Path);

    // Only the leaf name: the rest of the path is already in the sentence.
    private static string? Archive(ConfigUpdateReport report) =>
        report.ArchiveFolder is { } folder ? Path.GetFileName(folder) : null;

    private static List<ConfigFileOutcome> Of(ConfigUpdateReport report, ConfigOutcomeKind kind) =>
        [.. report.Files.Where(f => f.Kind == kind)];
}
