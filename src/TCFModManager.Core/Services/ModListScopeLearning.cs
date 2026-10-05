using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Gives an sp-mod list's entries the scope their installed files show.
//
// An sp-mod list is read off a web page, and nothing on the page says whether a mod is a client
// plugin, a server mod or both - only its files do. So every entry starts with no scope, which reads
// as Server + Client + Headless, and the list is read-only, so nobody can put that right by hand.
// Once a mod is on disk the scanner knows (ModListCandidate.Scope, the same rule a list made from
// this install uses), and that is learned onto the entry.
//
// Only an entry with no scope is touched, and only for a mod the scanner could place. A scope once
// learned is kept: a later refresh from sp-mod carries it across (SpModListImport.Merge), so it is
// learned once rather than every apply.
//
// sp-mod lists only. On a list made here an entry without a scope is somebody's choice - cycling
// back to Server + Client + Headless stores exactly that - and overriding it would undo the choice.
//
public static class ModListScopeLearning
{
    public static bool Learns(ModList list) => list.SpModSource is not null;

    // The entries with what was learned, or null when nothing was.
    public static List<ModListEntry>? Learn(ModList list, IReadOnlyList<ModListCandidate> candidates)
    {
        if (!Learns(list)) return null;

        var match = new ModListMatch(candidates);
        var learned = false;
        var entries = new List<ModListEntry>(list.Entries.Count);

        foreach (var entry in list.Entries)
        {
            var index = entry.Scope is null ? match.IndexOf(entry) : -1;
            var scope = index >= 0 ? candidates[index].Scope : ModListEntryScope.Everyone;

            if (scope == ModListEntryScope.Everyone)
            {
                entries.Add(entry);
                continue;
            }

            entries.Add(ModListEntries.WithScope(entry, scope));
            learned = true;
        }

        return learned ? entries : null;
    }
}
