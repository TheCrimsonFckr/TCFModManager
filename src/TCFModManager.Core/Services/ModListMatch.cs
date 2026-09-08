using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Which installed mod a list entry refers to.
//
// Its own class because two things ask the question and they must not answer it differently: the
// planner, deciding what to install and what to leave alone, and the refresh that re-reads installed
// versions into a list. A second implementation of these join keys would show up as a list that
// plans one mod correctly and refreshes a different one - the sort of disagreement nobody looks for
// because both halves individually look right.
//
public sealed class ModListMatch
{
    // Keyed on the (id, IsAddon) pair - addon ids and mod ids are separate sequences on sp-mod.com,
    // so a bare id dictionary would let a list entry for addon 116 claim mod 116.
    private readonly Dictionary<(int Id, bool IsAddon), int> _byModId = [];
    private readonly Dictionary<string, int> _byGuid = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _byFolder = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _byName = new(StringComparer.OrdinalIgnoreCase);

    public ModListMatch(IReadOnlyList<ModListCandidate> candidates)
    {
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];

            if (candidate.ModId is { } id) _byModId.TryAdd((id, candidate.IsAddon), index);

            if (!string.IsNullOrWhiteSpace(candidate.Guid)) _byGuid.TryAdd(candidate.Guid.Trim(), index);

            foreach (var folder in candidate.Folders.Where(f => !string.IsNullOrWhiteSpace(f)))
                _byFolder.TryAdd(folder.Trim(), index);

            _byName.TryAdd(candidate.Name.Trim(), index);
        }
    }

    //
    // The index of the installed mod this entry refers to, or -1 when it isn't installed.
    //
    // Mod id first, then plugin GUID, then folder name, then display name - most reliable join key
    // down to the loosest. The order is the point: a mod renamed on The Forge still matches on its
    // id, and one the catalog has never heard of still matches on the folder it dropped into.
    //
    public int IndexOf(ModListEntry entry)
    {
        if (entry.ModId is { } id && _byModId.TryGetValue((id, entry.IsAddon), out var byId)) return byId;

        if (!string.IsNullOrWhiteSpace(entry.Guid) && _byGuid.TryGetValue(entry.Guid.Trim(), out var guidMatch))
            return guidMatch;

        foreach (var folder in entry.Folders.Where(f => !string.IsNullOrWhiteSpace(f)))
            if (_byFolder.TryGetValue(folder.Trim(), out var folderMatch))
                return folderMatch;

        return _byName.TryGetValue(entry.Name.Trim(), out var nameMatch) ? nameMatch : -1;
    }
}
