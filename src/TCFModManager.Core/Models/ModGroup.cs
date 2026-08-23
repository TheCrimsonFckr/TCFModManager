namespace TCFModManager.Core.Models;

// A user-defined separator for organizing the Installed page, similar to Mod Organizer 2's
// separators. Purely organizational - has no effect on load order or what's actually installed.
public sealed class ModGroup
{
    public required Guid Id { get; init; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public bool IsCollapsed { get; set; }
}

// The full set of groups plus which installed mod belongs to which, persisted as one file.
public sealed class ModGroupData
{
    public List<ModGroup> Groups { get; init; } = [];

    // Installed-mod identity (InstalledModCardViewModel.Name, lowercased) -> group id. A mod with
    // no entry here is ungrouped. Keyed by name rather than ModId since plenty of installed mods
    // never match a sp-mod.com catalog listing and so never get one.
    public Dictionary<string, Guid> Assignments { get; init; } = [];
}
