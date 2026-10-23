using System.IO;
using System.Text.RegularExpressions;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public sealed partial class InstalledModCardViewModel
{
    //
    // Folds together the name-groups of a mod that installed more than one folder of the same kind
    // - a zip that drops two plugin folders arrives here as two groups, and both resolve to the one
    // catalog listing the install record names, so both would build a full card for the same mod.
    // The install manifest is what makes this exact rather than a guess: only folders one record
    // placed are joined, never two folders that merely resolved alike.
    //
    // Runs before the patcher and client/server passes, which both look for exactly one partner
    // group per mod and would otherwise be looking at two.
    //
    private static List<(string Key, List<InstalledMod> Entries, Mod? Match)> MergeRecordFolders(
        List<(string Key, List<InstalledMod> Entries, Mod? Match)> groups,
        Dictionary<string, InstalledModRecord> recordsByFolder,
        Dictionary<int, InstalledModRecord> recordsByModId,
        CatalogIndex index)
    {
        var byRecord = new Dictionary<int, List<int>>();

        for (var i = 0; i < groups.Count; i++)
        {
            if (RecordOf(groups[i].Entries, groups[i].Key, recordsByFolder, recordsByModId) is not { } record) continue;
            if (SpeaksForAnotherMod(groups[i], record, index)) continue;

            if (!byRecord.TryGetValue(record.ModId, out var members)) byRecord[record.ModId] = members = [];
            members.Add(i);
        }

        var joins = byRecord.Values.Where(m => m.Count > 1).ToList();
        if (joins.Count == 0) return groups;

        var absorbed = new Dictionary<int, List<int>>();
        var consumed = new HashSet<int>();

        foreach (var members in joins)
        {
            var record = RecordOf(groups[members[0]].Entries, groups[members[0]].Key, recordsByFolder, recordsByModId)!;
            var ordered = members
                .OrderBy(i => CarriesRecordGuid(groups[i].Entries, record) ? 0 : 1)
                .ThenBy(i => PlacementRank(groups[i].Key, record))
                .ToList();

            absorbed[ordered[0]] = ordered.Skip(1).ToList();
            foreach (var member in ordered.Skip(1)) consumed.Add(member);
        }

        var results = new List<(string Key, List<InstalledMod> Entries, Mod? Match)>(groups.Count - consumed.Count);

        for (var i = 0; i < groups.Count; i++)
        {
            if (consumed.Contains(i)) continue;

            var (key, entries, match) = groups[i];
            results.Add(absorbed.TryGetValue(i, out var extra)
                ? (key, entries.Concat(extra.SelectMany(j => groups[j].Entries)).ToList(), match)
                : (key, entries, match));
        }

        return results;
    }

    // The record a name-group belongs to: a shared folder's mod by the owner the scan found, any
    // other group by the folder its record placed.
    private static InstalledModRecord? RecordOf(
        List<InstalledMod> entries,
        string key,
        Dictionary<string, InstalledModRecord> recordsByFolder,
        Dictionary<int, InstalledModRecord> recordsByModId)
    {
        if (entries.Any(m => m.IsShare))
        {
            return entries.Where(m => !m.ShareOwnerIsAddon).Select(m => m.ShareOwnerModId).FirstOrDefault(id => id is not null) is { } owner
                ? recordsByModId.GetValueOrDefault(owner)
                : null;
        }

        return recordsByFolder.GetValueOrDefault(key);
    }

    //
    // Fills SharedFolderName and SharedWith on every card holding a share of a folder (OPEN-28), from
    // the other cards holding shares of the same folder - live or .disabled, it is one folder.
    //
    private static void NoteSharedFolders(List<InstalledModCardViewModel> cards)
    {
        var byFolder = cards
            .SelectMany(c => c.Entries.Where(e => e.IsShare).Select(e => (Card: c, Folder: LiveFolderOf(e.FolderPath))))
            .GroupBy(x => x.Folder, StringComparer.OrdinalIgnoreCase);

        foreach (var folder in byFolder)
        {
            var members = folder.Select(x => x.Card).Distinct().ToList();

            foreach (var card in members)
            {
                if (!card.IsAddon && card.ModId is { } id && members.Count > 1
                    && members.All(m => m == card || (m.IsAddon && m.ParentModId == id)))
                    continue;

                card.SharedFolderName = Path.GetFileName(folder.Key);
                card.SharedWith = [.. members.Where(m => m != card).Select(m => m.DisplayTitle)];
            }
        }
    }

    private static string LiveFolderOf(string folderPath)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(folderPath);
        return DisabledModPaths.IsModDisabled(trimmed) && DisabledModPaths.Counterpart(trimmed) is { } live
            ? live
            : trimmed;
    }

    //
    // Whether what is in the folder now identifies itself as a different mod from the one whose
    // record names it - a folder this app installed into, emptied by hand, and refilled with
    // something else. Enough to leave the group standing on its own, since a wrongly absorbed
    // folder would take a real mod off the page.
    //
    // A foreign GUID is deliberately NOT that signal for a folder whose files are still in place: a
    // mod that repacks another one ships its plugin, GUID and all, in a second folder of its own,
    // and disowning that folder is what put the mod on the page twice.
    //
    private static bool SpeaksForAnotherMod(
        (string Key, List<InstalledMod> Entries, Mod? Match) group,
        InstalledModRecord record,
        CatalogIndex index)
    {
        if (group.Match is { } match && match.Id != record.ModId) return true;

        // What the record placed there is the better evidence, when there is any: the files this
        // app wrote are either still in the folder or they are not.
        var placed = InstalledModFolders.PlacedFilesUnder(record, group.Key);
        if (placed.Count > 0) return !PlacedFilesStillPresent(group.Entries, placed);

        // A manually-confirmed record places no files, so the GUID is all there is to go on.
        var guid = group.Entries
            .OrderBy(m => m.Target == InstalledModTarget.Client ? 0 : 1)
            .SelectMany(m => m.AllGuids)
            .FirstOrDefault(g => !string.IsNullOrWhiteSpace(g));

        return guid is not null
            && index.ByGuid.TryGetValue(guid, out var byGuid)
            && byGuid.Id != record.ModId;
    }

    // Whether any file the record placed in this folder is still there. One hit settles it, so this
    // is a handful of File.Exists calls per app-installed folder, on the scan's background thread.
    private static bool PlacedFilesStillPresent(List<InstalledMod> entries, IReadOnlyList<string> placed)
    {
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.FolderPath)) continue;

            // A loose DLL's "folder" is the file itself.
            if (File.Exists(entry.FolderPath)) return true;

            foreach (var relative in placed)
            {
                var path = Path.Combine(entry.FolderPath, relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path)) return true;
            }
        }

        return false;
    }

    // The folder carrying the plugin the record was installed for, which is the one whose name and
    // version the merged card leads with.
    private static bool CarriesRecordGuid(List<InstalledMod> entries, InstalledModRecord record) =>
        !string.IsNullOrWhiteSpace(record.Guid)
        && entries.Any(m => m.AllGuids.Any(g => string.Equals(g, record.Guid, StringComparison.OrdinalIgnoreCase)));

    // Where a folder sits in the order the install placed it, so the first folder written leads when
    // no GUID settles it. Anything the record does not name sorts last.
    private static int PlacementRank(string folderName, InstalledModRecord record)
    {
        var folders = InstalledModFolders.Resolve(record);

        for (var i = 0; i < folders.Count; i++)
        {
            if (string.Equals(folders[i], folderName, StringComparison.OrdinalIgnoreCase)) return i;
        }

        return int.MaxValue;
    }

    //
    // Folds a patcher-only name-group into the mod it belongs to. A mod that ships a BepInEx
    // preloader patcher puts it in its own folder under BepInEx\patchers, usually named differently
    // from its plugin folder ("SomeMod" and "SomeMod.Preloader"), so the two arrive here as separate
    // name-groups - and the patcher half, having no [BepInPlugin] GUID to identify it, would show as
    // its own card with nothing filled in but a folder name. Two groups that resolved to the same
    // catalog listing are the same mod by definition, so the patcher joins it and the mod is one
    // card again, with the patcher's folder among its Entries where the disable/remove commands can
    // see it.
    //
    // A patcher that neither tier below can place is left standing on its own rather than attached
    // to a guess - it still reads as a patcher rather than an unidentified mod, since TargetSummary
    // says so.
    //
    private static List<(List<InstalledMod> Entries, Mod? Match)> MergePatcherFolders(
        List<(List<InstalledMod> Entries, Mod? Match)> groups)
    {
        // Keyed by the index of the group being joined, so a mod with more than one patcher folder
        // collects all of them.
        var absorbed = new Dictionary<int, List<InstalledMod>>();
        var consumed = new HashSet<int>();

        for (var i = 0; i < groups.Count; i++)
        {
            if (!groups[i].Entries.All(m => m.IsPatcher)) continue;

            var host = FindPatcherHost(groups, i);
            if (host is not { } index) continue;

            if (!absorbed.TryGetValue(index, out var into)) absorbed[index] = into = [];
            into.AddRange(groups[i].Entries);
            consumed.Add(i);
        }

        if (consumed.Count == 0) return groups;

        var results = new List<(List<InstalledMod> Entries, Mod? Match)>(groups.Count - consumed.Count);

        for (var i = 0; i < groups.Count; i++)
        {
            if (consumed.Contains(i)) continue;

            var (entries, match) = groups[i];
            results.Add(absorbed.TryGetValue(i, out var extra)
                ? (entries.Concat(extra).ToList(), match)
                : (entries, match));
        }

        return results;
    }

    //
    // The group a patcher-only group belongs to, or null when it can't be placed with confidence.
    //
    // Two tiers. The catalog listing comes first and is the exact one: two groups that resolved to
    // the same listing are the same mod, whether that came from the install manifest or from a
    // plugin's own GUID. Where a mod has both a plugin and a server half, the patcher goes with the
    // plugin - it's a BepInEx file, and it's the plugin's version and name the card leads with.
    //
    // The second tier is the naming convention, for a mod installed by hand whose patcher folder
    // resolves to nothing on its own (it has no GUID to match on, and "SomeMod.Preloader" isn't
    // going to match a listing called "Some Mod"). Stripping the patcher word off the end has to
    // leave exactly one other mod's folder name, matched in full - a substring or fuzzy rule here
    // would start attaching patchers to whatever mod happened to share a prefix.
    //
    private static int? FindPatcherHost(List<(List<InstalledMod> Entries, Mod? Match)> groups, int patcherIndex)
    {
        var candidates = Enumerable.Range(0, groups.Count)
            .Where(j => j != patcherIndex && groups[j].Entries.Any(m => !m.IsPatcher))
            .ToList();

        if (groups[patcherIndex].Match is { } match)
        {
            var byMatch = candidates.Where(j => groups[j].Match?.Id == match.Id).ToList();
            var withPlugin = byMatch
                .Where(j => groups[j].Entries.Any(m => m is { Target: InstalledModTarget.Client, IsPatcher: false }))
                .ToList();

            var preferred = withPlugin.Count > 0 ? withPlugin : byMatch;
            if (preferred.Count == 1) return preferred[0];
        }

        var patcherName = groups[patcherIndex].Entries[0].Name;
        var stem = PatcherNameSuffix.Replace(patcherName, "");
        if (stem.Length == 0 || stem.Length == patcherName.Length) return null;

        var byName = candidates
            .Where(j => string.Equals(
                groups[j].Entries.First(m => !m.IsPatcher).Name,
                stem,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        return byName.Count == 1 ? byName[0] : null;
    }

    //
    // The word a patcher folder is conventionally named after its mod plus - "SomeMod.Preloader",
    // "SomeMod-Patcher", "SomeModPrepatch". Only stripped from the end, and only used to look for an
    // exact match on what's left.
    //
    [GeneratedRegex(@"[\s._-]*(preloader|prepatcher|prepatch|patchers|patcher|patch)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PatcherNameSuffix { get; }

    // Joins a client-only name-group to a server-only name-group when they're the only two that
    // independently resolved to the same catalog Mod. Groups that already have both targets, matched
    // nothing, or have an ambiguous match pass through unmerged.
    private static List<(List<InstalledMod> Entries, Mod? Match)> MergeSplitClientServerHalves(
        List<(List<InstalledMod> Entries, Mod? Match)> groups)
    {
        var results = new List<(List<InstalledMod> Entries, Mod? Match)>();
        var consumed = new HashSet<int>();

        for (var i = 0; i < groups.Count; i++)
        {
            if (consumed.Contains(i)) continue;

            var (entries, match) = groups[i];
            var hasClient = entries.Any(m => m.Target == InstalledModTarget.Client);
            var hasServer = entries.Any(m => m.Target == InstalledModTarget.Server);

            if (match is not null && hasClient != hasServer)
            {
                var partners = Enumerable.Range(i + 1, groups.Count - i - 1)
                    .Where(j => !consumed.Contains(j) && groups[j].Match?.Id == match.Id)
                    .Where(j =>
                    {
                        var otherHasClient = groups[j].Entries.Any(m => m.Target == InstalledModTarget.Client);
                        var otherHasServer = groups[j].Entries.Any(m => m.Target == InstalledModTarget.Server);
                        return otherHasClient != otherHasServer && otherHasClient != hasClient;
                    })
                    .ToList();

                if (partners.Count == 1)
                {
                    consumed.Add(partners[0]);
                    results.Add((entries.Concat(groups[partners[0]].Entries).ToList(), match));
                    continue;
                }
            }

            results.Add((entries, match));
        }

        return results;
    }
}
