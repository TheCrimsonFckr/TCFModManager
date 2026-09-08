using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// One entry the refresh changed, and what it changed from.
public sealed record ModListVersionRefresh(string Name, string? From, string? To);

// The refreshed entries and what moved, so the caller can say which mods changed rather than how
// many.
public sealed record ModListRefreshResult(
    IReadOnlyList<ModListEntry> Entries,
    IReadOnlyList<ModListVersionRefresh> Changed,
    int NotInstalled);

//
// Brings a list's pinned versions back in line with what is installed right now.
//
// The problem it removes: a list entry records the version that was installed when the list was
// made. Update that mod afterwards and the list still names the old build, so applying it offers to
// put the old one back. The only way to correct it was to take the mod off the list and add it
// again, which works and is absurd - and on a seventy-mod list after an update round, it is absurd
// seventy times.
//
// WHAT IT DOES NOT TOUCH is the interesting half:
//
//   Scope       - a deliberate decision. Re-inferring it from where the files live would quietly
//                 undo every "server only" an operator set by hand, and a published list would
//                 start sending clients after fika-server the next time it was refreshed.
//   Name        - the join key an unresolved entry has and nothing else. Rewriting it to whatever
//                 this machine calls the folder would break the match on someone else's.
//   Entries not installed here - a list can name mods this machine does not have, on purpose.
//                 Those are left exactly as written rather than dropped.
//
// So this is not a re-capture. A re-capture answers "what is installed"; this answers "what version
// is the thing this entry already names", which is the only question that was being asked.
//
public static class ModListRefresh
{
    public static ModListRefreshResult Build(
        IEnumerable<ModListEntry> entries,
        IEnumerable<ModListCandidate> installed,
        ModListCapture.VersionLookup? versions = null,
        ModListCapture.AddonVersionLookup? addonVersions = null)
    {
        var candidates = installed.ToList();
        var match = new ModListMatch(candidates);

        var refreshed = new List<ModListEntry>();
        var changed = new List<ModListVersionRefresh>();
        var notInstalled = 0;

        foreach (var entry in entries)
        {
            var found = match.IndexOf(entry);

            if (found < 0)
            {
                notInstalled++;
                refreshed.Add(entry);
                continue;
            }

            var candidate = candidates[found];
            var version = string.IsNullOrWhiteSpace(candidate.Version) ? null : candidate.Version.Trim();

            //
            // A disabled mod's version is still the version this install holds, so it refreshes like
            // any other. Whether the list should have it enabled is a different question, and one
            // applying the list already answers.
            //
            var versionId = ModListCapture.ResolveVersionId(candidate, versions, addonVersions);

            var folders = Folders(candidate, entry);

            var same = string.Equals(entry.Version?.Trim(), version, StringComparison.OrdinalIgnoreCase)
                && entry.VersionId == versionId
                && folders.SequenceEqual(entry.Folders, StringComparer.OrdinalIgnoreCase);

            if (same)
            {
                refreshed.Add(entry);
                continue;
            }

            // ModListEntry is a class with init-only properties, not a record, so this is a rebuild
            // rather than a `with`. Every field is carried across deliberately.
            refreshed.Add(new ModListEntry
            {
                Name = entry.Name,
                ModId = entry.ModId ?? candidate.ModId,
                IsAddon = entry.ModId is null ? candidate.IsAddon : entry.IsAddon,
                VersionId = versionId,
                Version = version,

                // Filled in when the entry has none and the install does: an entry that gains a GUID
                // matches more reliably from then on. Never replaced, because the one already there
                // was written by whoever made the list.
                Guid = string.IsNullOrWhiteSpace(entry.Guid)
                    ? (string.IsNullOrWhiteSpace(candidate.Guid) ? null : candidate.Guid.Trim())
                    : entry.Guid,

                Scope = entry.Scope,
                Folders = folders,
            });

            if (!string.Equals(entry.Version?.Trim(), version, StringComparison.OrdinalIgnoreCase))
                changed.Add(new ModListVersionRefresh(entry.Name, entry.Version, version));
        }

        return new ModListRefreshResult(refreshed, changed, notInstalled);
    }

    //
    // The folders this mod occupies now. Refreshed because a mod can rename its folder between
    // versions, and a stale folder name is a match that silently stops working - but only when the
    // install actually reports some, so an entry never loses the folders it has to a scan that
    // could not see them.
    //
    private static List<string> Folders(ModListCandidate candidate, ModListEntry entry)
    {
        var folders = candidate.Folders
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        return folders.Count > 0 ? folders : [.. entry.Folders];
    }
}
