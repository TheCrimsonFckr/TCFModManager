using System.IO;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// One copy in a conflict, as the Dependencies and Conflicts page lists it. Card and Entry are what
// Keep this one acts on: the mod the copy belongs to, and the folder that clashes.
//
public sealed class ConflictMemberRow
{
    public required string ModName { get; init; }

    public required string Location { get; init; }

    public string? Detail { get; init; }

    public required string FullPath { get; init; }

    public required InstalledModCardViewModel Card { get; init; }

    public required InstalledMod Entry { get; init; }

    // Only for the same mod installed twice (C1, C2). Different copies of a file are information
    // only (D7): neither mod is the wrong one.
    public bool CanKeep { get; init; }

    public ConflictItemViewModel Owner { get; internal set; } = null!;
}

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
        ModConflict conflict, IReadOnlyList<InstalledModCardViewModel> cards, string installPath)
    {
        var canKeep = conflict.Kind is ModConflictKind.DuplicatePlugin or ModConflictKind.DuplicateServerMod;

        var item = new ConflictItemViewModel
        {
            Title = ModConflicts.Title(conflict.Kind),
            Explanation = ModConflicts.Explanation(conflict),
            Members =
            [
                .. conflict.Members.Select(m => new ConflictMemberRow
                {
                    ModName = cards[m.ModIndex].DisplayTitle,
                    Location = Relative(installPath, m.Entry.FolderPath),
                    Detail = m.Assembly is { } copy ? Describe(copy) : null,
                    FullPath = m.Assembly is null ? m.Entry.FolderPath : ModConflictFinder.FullPath(m),
                    Card = cards[m.ModIndex],
                    Entry = m.Entry,
                    CanKeep = canKeep,
                }),
            ],
        };

        foreach (var row in item.Members) row.Owner = item;
        return item;
    }

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
