using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// The rules for a mod list's contents, for anything that changes one entry at a time.
//
// Capture builds a whole set in one pass and dedupes as it goes (ModListCapture.BuildEntries);
// adding a mod by hand asks the same question against a list that already exists. Both have to
// answer it the same way, or the same mod ends up on one list twice under two spellings of its
// name - which the planner would then match to the same installed mod twice.
//
public static class ModListEntries
{
    //
    // Whether two entries name the same mod.
    //
    // The id decides it when both carry one - and it is the id *and* the addon flag, never the
    // number alone: sp-mod.com numbers addons in their own sequence, so addon 116 and mod 116 are
    // two unrelated things. Falls back to the name, which is all an unresolved entry has, and is
    // also the only join between a hand-added catalog mod and a folder the scanner found.
    //
    public static bool SameMod(ModListEntry a, ModListEntry b)
    {
        if (a.ModId is { } left && b.ModId is { } right) return left == right && a.IsAddon == b.IsAddon;

        return string.Equals(a.Name.Trim(), b.Name.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static bool Contains(IEnumerable<ModListEntry> entries, ModListEntry entry) =>
        entries.Any(e => SameMod(e, entry));

    // The order a list holds its entries in - by name, the same order capture writes them.
    public static List<ModListEntry> Sorted(IEnumerable<ModListEntry> entries) =>
        [.. entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];

    //
    // An entry for a mod this install doesn't have, added by hand from the cached catalog.
    //
    // Deliberately not pinned to a version. A captured pin records a build somebody here is
    // actually running; a pin invented from whatever the catalog cache last saw would name a build
    // nobody has tried, and would be wrong the moment the author publishes again. Unpinned means
    // "the newest published", which is what the planner installs and what adding it meant.
    //
    public static ModListEntry ForCatalogMod(int modId, string name, string? guid = null, bool isAddon = false) => new()
    {
        Name = name.Trim(),
        ModId = modId,
        IsAddon = isAddon,
        Guid = string.IsNullOrWhiteSpace(guid) ? null : guid.Trim(),
    };
}
