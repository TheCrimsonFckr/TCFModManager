using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Whether publishing a list is handing out something the server was not already serving.
//
// The revision is the only thing a receiver has to tell a newer copy of a list from the one it
// holds, and the app fetches only when that number moves. But a revision counts an APPLY - editing
// the list you serve does not move it - so an operator who edits and republishes hands out different
// contents under the same number, and every client correctly declines to ask again. They sit on a
// stale copy silently and for as long as it takes somebody to notice.
//
// So publishing bumps it, and only when what is being published actually differs from the file
// already in the config folder. Bumping on every publish would work too and was rejected: a number
// that moves when nothing changed teaches people to ignore it, and republishing the same list twice
// is something an operator does while fiddling with a server.
//
public static class ModListPublication
{
    //
    // published is what is already in the config folder, read back, or null when nothing is.
    //
    // A DIFFERENT list there is not a reason to bump: a receiver compares revisions per list, so
    // this list's number has nothing to say about the one it is replacing. Nothing published at all
    // is not a reason either - the first publish of a list is that list as it stands.
    //
    public static bool NeedsNewRevision(ModList candidate, ModList? published) =>
        published is not null
        && published.Id == candidate.Id
        && !ContentsMatch(candidate, published);

    //
    // Everything a receiver can act on or read, and nothing else.
    //
    // NOT the revision, which is the thing being decided, and not the timestamps, which move on
    // their own. Name is in: it is what a client sees the list called, and renaming the list a
    // server serves is a change worth carrying.
    //
    public static bool ContentsMatch(ModList a, ModList b)
    {
        if (a.Entries.Count != b.Entries.Count) return false;

        if (!Same(a.Name, b.Name)
            || !Same(a.Description, b.Description)
            || a.Policy != b.Policy
            || !Same(a.SptVersion, b.SptVersion))
        {
            return false;
        }

        //
        // Matched by identity rather than by position, because the order entries are stored in is
        // the page's business and not the receiver's - a list re-sorted is not a list changed.
        //
        foreach (var entry in a.Entries)
        {
            var other = b.Entries.FirstOrDefault(e => ModListEntries.SameMod(e, entry));
            if (other is null || !EntriesMatch(entry, other)) return false;
        }

        return true;
    }

    //
    // Version pin, scope and the join keys. A change to any of them changes what a machine reading
    // this list would install or skip, which is the whole test.
    //
    private static bool EntriesMatch(ModListEntry a, ModListEntry b) =>
        a.ModId == b.ModId
        && a.IsAddon == b.IsAddon
        && a.VersionId == b.VersionId
        && Same(a.Version, b.Version)
        && Same(a.Guid, b.Guid)
        && a.EffectiveScope == b.EffectiveScope
        && Same(a.Name, b.Name)
        && a.Folders.Count == b.Folders.Count
        && a.Folders.All(f => b.Folders.Any(g => Same(f, g)));

    private static bool Same(string? a, string? b) =>
        string.Equals(a?.Trim() ?? "", b?.Trim() ?? "", StringComparison.OrdinalIgnoreCase);
}
