using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Gives an sp-mod list's entries the scope their installed files show.
//
// An sp-mod list is read off a web page, and nothing on the page says whether a mod is a client
// plugin, a server mod or both - only its files do. So every entry starts unchecked, and the list is
// read-only, so nobody can put that right by hand. Once a mod is on disk the scanner knows
// (ModListCandidate.Scope, the same rule a list made from this install uses), and that is learned
// onto the entry and the entry marked checked in SpModSource.ScopesChecked - the mark is what tells
// "found to have both halves" apart from "not looked at", since both store no scope.
//
// Only an unchecked entry is touched, and only once its mod is installed. A checked one is kept: a
// later refresh from sp-mod carries both the scope and the mark across (SpModListImport.Merge).
//
// sp-mod lists only. On a list made here an entry without a scope is somebody's choice - cycling
// back to Server + Client + Headless stores exactly that - and overriding it would undo the choice.
//
public sealed record ModListScopesLearned(List<ModListEntry> Entries, IReadOnlyList<string> Checked);

public static class ModListScopeLearning
{
    public static bool Learns(ModList list) => list.SpModSource is not null;

    // The entries with what was learned and the refs newly checked, or null when nothing was.
    public static ModListScopesLearned? Learn(ModList list, IReadOnlyList<ModListCandidate> candidates)
    {
        if (list.SpModSource is not { } source) return null;

        var match = new ModListMatch(candidates);
        var entries = new List<ModListEntry>(list.Entries.Count);
        var checkedNow = new List<string>();

        foreach (var entry in list.Entries)
        {
            var index = source.IsScopeUnchecked(entry) ? match.IndexOf(entry) : -1;

            if (index < 0)
            {
                entries.Add(entry);
                continue;
            }

            checkedNow.Add(SpModListSource.RefFor(entry)!);

            var scope = candidates[index].Scope;
            entries.Add(scope == ModListEntryScope.Everyone ? entry : ModListEntries.WithScope(entry, scope));
        }

        return checkedNow.Count > 0 ? new ModListScopesLearned(entries, checkedNow) : null;
    }
}
