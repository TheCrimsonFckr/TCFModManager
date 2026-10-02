using System.IO;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

// One copy in a conflict, as the Dependencies and Conflicts page lists it.
public sealed record ConflictMemberRow(string ModName, string Location, string? Detail, string FullPath);

//
// One conflict on the Dependencies and Conflicts page (OPEN-11): what kind, why it matters, and
// every mod involved with the folder it sits in. Built once from a ModConflict; nothing here changes
// after that, so it is a plain object rather than an observable one.
//
public sealed class ConflictItemViewModel
{
    public required string Title { get; init; }

    public required string Explanation { get; init; }

    public required IReadOnlyList<ConflictMemberRow> Members { get; init; }

    public static ConflictItemViewModel From(
        ModConflict conflict, IReadOnlyList<InstalledModCardViewModel> cards, string installPath) => new()
    {
        Title = ModConflicts.Title(conflict.Kind),
        Explanation = ModConflicts.Explanation(conflict),
        Members =
        [
            .. conflict.Members.Select(m => new ConflictMemberRow(
                cards[m.ModIndex].DisplayTitle,
                Relative(installPath, m.Entry.FolderPath),
                m.Assembly is { } copy ? Describe(copy) : null,
                m.Assembly is null ? m.Entry.FolderPath : ModConflictFinder.FullPath(m))),
        ],
    };

    private static string Describe(ModAssembly copy)
    {
        var path = copy.RelativePath.Replace('/', '\\');
        return copy.AssemblyVersion is { } version
            ? LocalizationService.Text(Strings.Conflicts_CopyFormat, path, version)
            : path;
    }

    private static string Relative(string installPath, string path)
    {
        try { return Path.GetRelativePath(installPath, path); }
        catch (ArgumentException) { return path; }
    }
}
