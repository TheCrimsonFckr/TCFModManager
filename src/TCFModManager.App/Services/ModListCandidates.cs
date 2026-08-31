using TCFModManager.App.ViewModels;
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
