using TCFModManager.App.ViewModels;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// Turns the Installed page's cards into the shape every Core mod-list piece takes.
//
// Kept apart from ModListService, which needs WPF for the dispatcher: this half references no UI
// type, so it compiles into a plain console app and can be exercised headlessly - and it is the
// half most worth proving, since a wrong folder name or a dropped Entries list only shows up when
// an apply moves the wrong thing.
//
public static class ModListCandidates
{
    public static List<ModListCandidate> From(IEnumerable<InstalledModCardViewModel> cards) =>
        [.. cards.Select(From)];

    //
    // The cards, plus any addon the manifest knows about that no card covers.
    //
    // An addon usually installs into its parent mod's folder, so the scanner sees one folder and
    // the Installed page shows one card - the parent's. Without this, a list would silently leave
    // those addons out, which is the whole thing lists carrying addons was meant to avoid. They go
    // in as install/update-only: there is no folder of their own to move, so nothing can disable
    // one on its own, and disabling the parent takes it with it regardless.
    //
    public static List<ModListCandidate> From(
        IEnumerable<InstalledModCardViewModel> cards,
        IReadOnlyList<InstalledModRecord> records)
    {
        var candidates = From(cards);

        var covered = candidates
            .Where(c => c is { IsAddon: true, ModId: not null })
            .Select(c => c.ModId!.Value)
            .ToHashSet();

        foreach (var record in records.Where(r => r.IsAddon && !covered.Contains(r.ModId)))
        {
            candidates.Add(new ModListCandidate
            {
                Name = record.Name,
                ModId = record.ModId,
                IsAddon = true,
                Version = record.Version,
                CanBeDisabled = false,

                // Deliberately no folders: the ones this record names are its parent's, and putting
                // them on the addon would let a list entry match the parent by folder name when the
                // id tier missed. An addon always has an id, so the id tier always applies.
                Folders = [],
            });
        }

        return candidates;
    }

    public static ModListCandidate From(InstalledModCardViewModel card) => new()
    {
        Name = card.Name,
        ModId = card.ModId,
        IsAddon = card.IsAddon,
        Version = card.InstalledVersion,
        Guid = card.Guid,
        IsDisabled = card.IsDisabled,
        Folders = FoldersOf(card),
        Entries = card.Entries,
    };

    private static List<string> FoldersOf(InstalledModCardViewModel card)
    {
        var folders = new[] { card.ClientFolderName, card.ServerFolderName }
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f!.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        // A card that reported neither still needs something for a list entry with no mod id to be
        // matched on, and its own name is what the scanner named the folder after.
        if (folders.Count == 0) folders.Add(card.Name.Trim().ToLowerInvariant());

        return folders;
    }
}
