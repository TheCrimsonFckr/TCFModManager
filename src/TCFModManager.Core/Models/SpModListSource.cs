namespace TCFModManager.Core.Models;

//
// Where an imported list came from on sp-mod, and the choices made when it was imported, so a
// refresh can read the same page again and make the same choices without asking.
//
// Item references are written "mod/<id>" or "addon/<id>", the same shape as sp-mod's own
// addresses, because mod and addon ids are separate sequences.
//
public sealed class SpModListSource
{
    public required int ListId { get; init; }

    //
    // The address the list was read from. Kept with its query string in this install's store,
    // since a private list's share token lives there and a refresh needs it. ModListFile strips the
    // query on export, so the token never travels with a shared file.
    //
    public required string Url { get; init; }

    // When the page was last read.
    public required DateTimeOffset ReadAt { get; init; }

    // The list's own "updated" time on sp-mod at that read.
    public DateTimeOffset? PageUpdatedAt { get; init; }

    // Entries were re-pinned to versions for this install's SPT rather than the list's target.
    public bool Retargeted { get; init; }

    // Missing dependencies were added as entries.
    public bool DependenciesAdded { get; init; }

    // Page items the user unticked. A refresh leaves them out again rather than offering them as new.
    public List<string> Excluded { get; init; } = [];

    //
    // Entries the user added that aren't on the page - a missing parent mod, a dependency. A refresh
    // keeps them rather than reporting them as removed.
    //
    public List<string> Added { get; init; } = [];

    //
    // Entries whose scope has been read off their installed files (ModListScopeLearning), whatever it
    // came out as. Needed because "Server + Client + Headless" is stored as no scope at all, so
    // without it a mod found to have both halves would look exactly like one nobody has looked at.
    //
    public List<string> ScopesChecked { get; init; } = [];

    // Whether this entry's scope is still only the default, because its files haven't been seen yet.
    public bool IsScopeUnchecked(ModListEntry entry) =>
        entry.Scope is null && RefFor(entry) is { } key && !ScopesChecked.Contains(key, StringComparer.OrdinalIgnoreCase);

    public static string RefFor(bool isAddon, int id) => (isAddon ? "addon/" : "mod/") + id;

    public static string? RefFor(ModListEntry entry) => entry.ModId is { } id ? RefFor(entry.IsAddon, id) : null;
}
