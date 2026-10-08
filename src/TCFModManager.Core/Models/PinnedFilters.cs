namespace TCFModManager.Core.Models;

//
// The pinned Filters panel sections (OPEN-24) as they are kept in settings.json: section names,
// because the section enums live in TCFModManager.App and the file is offered for hand-editing.
//
public static class PinnedFilters
{
    //
    // What a stored list means for a page whose sections are `known`, in panel order. The result
    // always follows `known`'s order whatever order the names were stored in (R5), drops a name that
    // matches no section - a section removed later must never stop a page opening - and drops
    // repeats. Matching ignores case, since the file can be hand-edited; the names returned are the
    // page's own spelling.
    //
    public static List<string> Normalise(IEnumerable<string>? stored, IReadOnlyList<string> known)
    {
        if (stored is null) return [];

        var wanted = new HashSet<string>(stored.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);
        return known.Where(wanted.Contains).ToList();
    }
}
