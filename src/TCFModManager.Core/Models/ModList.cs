namespace TCFModManager.Core.Models;

// Where a mod list came from. Only Local lists can be edited in place; the other two are records of
// something someone else authored, and editing one forks it to a new list (see ModListStore.Fork).
public enum ModListOrigin
{
    // Made here, from this install or by hand.
    Local,

    // Received as a file from someone else.
    Imported,

    // Served by a server this install connected to.
    Server,
}

// What applying a list does to installed mods the list doesn't mention.
public enum ModListPolicy
{
    // Anything not on the list is disabled, so the install ends up as the list describes it.
    Exclusive,

    // Nothing is disabled; the list's mods are installed and enabled alongside whatever is there.
    Additive,
}

// One mod in a list.
//
// Three states, and the difference decides what applying it can do:
//   - pinned      ModId and VersionId both known, so an exact version can be fetched from The Forge.
//   - resolved    ModId known but VersionId isn't, because the pinned version isn't in the cached
//                 version list (only the six most recent are embedded on a catalog Mod). The mod is
//                 still fetchable; the version has to be looked up or fallen back on.
//   - unresolved  no ModId at all - a GitHub-only mod, a hand-installed one, anything the catalog
//                 matcher couldn't place. Carried by name so the receiver is told to fetch it
//                 themselves, never silently dropped.
public sealed class ModListEntry
{
    // The mod's display name as it read when the list was made - the catalog listing name where one
    // matched, the folder name otherwise.
    public required string Name { get; init; }

    public int? ModId { get; init; }
    public int? VersionId { get; init; }

    // The version string as installed. Kept alongside VersionId so a pinned version that has since
    // been taken down can still be named ("pinned 1.4.2 is gone, latest is 1.5.0").
    public string? Version { get; init; }

    // The mod's plugin GUID where it has one, as a second join key on the receiving side.
    public string? Guid { get; init; }

    // The mod folder names on disk this entry covers, lowercased - the same names
    // InstalledModScanner reports. What an unresolved entry is matched on locally.
    public List<string> Folders { get; init; } = [];

    public bool IsPinned => ModId is not null && VersionId is not null;

    public bool IsResolved => ModId is not null;
}

// A named set of mods: a playlist, a shared list and a server-served list are all this same object,
// differing only in Origin and in how they arrived.
public sealed class ModList
{
    public required Guid Id { get; init; }

    // Free text and not unique - two people can both call a list "Fika night". Id is the identity.
    public required string Name { get; set; }

    public string? Description { get; set; }

    // Bumped on every edit that changes Entries. Monotonic per Id, so a receiver can tell a newer
    // revision of a list it already has from an older one.
    public int Revision { get; set; } = 1;

    public ModListOrigin Origin { get; init; } = ModListOrigin.Local;

    public ModListPolicy Policy { get; set; } = ModListPolicy.Exclusive;

    // The list this one was forked from, when it was made by editing an imported or served list.
    public Guid? DerivedFrom { get; init; }

    // Who or what it came from - an author name for an imported file, a server address for a served
    // list. Null for a list made here.
    public string? Source { get; init; }

    // The SPT version this install was running when the list was captured, so a receiver on a
    // different version can be warned before anything is fetched.
    public string? SptVersion { get; init; }

    // True for a list written automatically to record the install as it stood before a list was
    // applied - the "put me back" undo. Shown apart from lists the user made deliberately.
    public bool IsSnapshot { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<ModListEntry> Entries { get; init; } = [];

    public bool IsEditable => Origin == ModListOrigin.Local;

    // Entries that can't be fetched from The Forge, so the receiver has to install them by hand.
    public IEnumerable<ModListEntry> Unresolved => Entries.Where(e => !e.IsResolved);
}

// Every list this install holds, plus which one is currently applied.
public sealed class ModListData
{
    public List<ModList> Lists { get; init; } = [];

    // One list is active at a time. Null when the install isn't following a list.
    public Guid? ActiveListId { get; set; }
}
