using System.Text.Json.Serialization;

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

//
// Who an entry is for.
//
// A server's published list describes the whole server, and not all of it is a client's business:
// the Server Map mod itself and fika-server live in user\mods, are often not on The Forge at all,
// and a client told to install them is being sent on an errand. Leaving them off the list instead
// is worse - the operator applying their own published list would then disable the very mod that
// serves it. Scope is what breaks that: one list, and the entries say who they concern.
//
// Inferred at capture from where a mod's files land (InstalledModTarget), so it costs the operator
// nothing in the common case and can be overridden for the odd one.
//
public enum ModListEntryScope
{
    // Everyone. The default, so a list written before scope existed means exactly what it meant.
    Both,

    // A playing client - a BepInEx plugin. Nothing for a headless box to do with it.
    Client,

    // The machine running the server. A client applying a SERVED list skips these entirely: not
    // installed, not disabled, not reported missing.
    Server,
}

//
// What a list is for.
//
// Housekeeping, not behaviour: it marks the one list this machine publishes to its own server, so
// the Mod lists page can show which it is and offer to re-export on save. Nothing in the planner
// reads it - what an apply does is decided by Origin and Scope.
//
public enum ModListPurpose
{
    // An ordinary list, made and applied here.
    Personal,

    // The list this machine serves through the Server Map mod.
    Published,
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

    //
    // True when ModId is an sp-mod.com addon id rather than a mod id. The two are separate
    // sequences, so every comparison of ModId has to carry this with it - otherwise a list naming
    // addon 116 would match, install or disable mod 116 on the receiving side.
    //
    // Defaults false, so a list written before addons were supported keeps meaning what it meant.
    // A list that contains one is exported at share-file schema 2 (see ModListFile), which an
    // older app refuses outright rather than misreading.
    //
    public bool IsAddon { get; init; }

    public int? VersionId { get; init; }

    // The version string as installed. Kept alongside VersionId so a pinned version that has since
    // been taken down can still be named ("pinned 1.4.2 is gone, latest is 1.5.0").
    public string? Version { get; init; }

    // The mod's plugin GUID where it has one, as a second join key on the receiving side. Always
    // null for an addon - sp-mod.com doesn't give addons a GUID.
    public string? Guid { get; init; }

    // The mod folder names on disk this entry covers, lowercased - the same names
    // InstalledModScanner reports. What an unresolved entry is matched on locally.
    public List<string> Folders { get; init; } = [];

    //
    // Who this entry is for. Omitted from the file when it is Both, which is almost always, so
    // scope costs nothing in a list that does not use it.
    //
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ModListEntryScope Scope { get; init; }

    //
    // JsonIgnore on all four computed members here and on ModList below. System.Text.Json writes
    // get-only properties by default, so every .tcfmodlist ever exported carried IsPinned and
    // IsResolved on each entry, and IsEditable plus a SECOND FULL COPY of every unresolved entry
    // under "Unresolved" on the list. Nothing has ever read them back - there are no setters - so
    // this only makes the file smaller and honest.
    //
    // Not a schema change: an older app never set these either, so a file without them means
    // exactly what a file with them meant.
    //
    [JsonIgnore]
    public bool IsPinned => ModId is not null && VersionId is not null;

    [JsonIgnore]
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

    //
    // Whether this is the list this machine publishes.
    //
    // Never written to a share file: it is local bookkeeping, and a list you RECEIVE is not your
    // published one no matter what the sender had it marked as. Plain JsonIgnore rather than
    // WhenWritingDefault, which would have let the interesting value through and only suppressed
    // the boring one - a test caught exactly that.
    //
    [JsonIgnore]
    public ModListPurpose Purpose { get; set; }

    public ModListPolicy Policy { get; set; } = ModListPolicy.Exclusive;

    // The list this one was forked from, when it was made by editing an imported or served list.
    public Guid? DerivedFrom { get; init; }

    // Who or what it came from - an author name for an imported file, a server address for a served
    // list. Null for a list made here.
    public string? Source { get; init; }

    // The SPT version this install was running when the list was captured, so a receiver on a
    // different version can be warned before anything is fetched.
    public string? SptVersion { get; init; }

    //
    // True for a list written automatically to record the install as it stood before a list was
    // applied - the "put me back" undo.
    //
    // A snapshot never lives in ModListData.Lists; there is one slot for it, overwritten by each
    // apply (see ModListData.Snapshot). It was a normal list to begin with, which meant applying
    // one produced a snapshot of a snapshot, and the names grew a "Before " every time.
    //
    public bool IsSnapshot { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<ModListEntry> Entries { get; init; } = [];

    //
    // IsEditable was the actively misleading one: a served list is written by an app where it was
    // Local, so the file said "IsEditable": true while the receiving app - which sets Origin to
    // Server on the way in - correctly treats it as read-only. A document that contradicts the
    // program reading it is worse than one that says nothing.
    //
    [JsonIgnore]
    public bool IsEditable => Origin == ModListOrigin.Local;

    // Entries that can't be fetched from The Forge, so the receiver has to install them by hand.
    [JsonIgnore]
    public IEnumerable<ModListEntry> Unresolved => Entries.Where(e => !e.IsResolved);

    //
    // The entries that concern the machine reading this list.
    //
    // A list of your own describes your install, both halves of it, so all of it applies. A list a
    // SERVER handed you describes that server, and its server-only entries are not yours to install
    // - you are not that machine.
    //
    [JsonIgnore]
    public IEnumerable<ModListEntry> EntriesApplyingHere => Origin == ModListOrigin.Server
        ? Entries.Where(e => e.Scope != ModListEntryScope.Server)
        : Entries;
}

// Every list this install holds, plus which one is currently applied.
public sealed class ModListData
{
    public List<ModList> Lists { get; init; } = [];

    //
    // The install's OWN list - the one it chose to follow. Null when it isn't following one.
    //
    // One at a time, because two personal lists both claiming to describe this install would be two
    // answers to one question.
    //
    public Guid? ActiveListId { get; set; }

    //
    // The list a SERVER hands this install, followed alongside the one above rather than instead of
    // it.
    //
    // Two slots because they answer different questions and both can be true at once: the server
    // says what its players need, and the player's own list says what else they like running. A
    // single slot forced a choice nobody should have to make - follow the server and lose your own
    // client-side mods from the list that protects them, or keep your list and have an Exclusive
    // apply set aside every mod the server requires.
    //
    // What makes them coexist is in ModListPlanner: a served list never disables, and a personal
    // list's Exclusive sweep spares everything the followed server list names.
    //
    public Guid? ActiveServerListId { get; set; }

    //
    // How the install stood before the last list was applied, and the only one kept - each apply
    // overwrites it, and reverting consumes it. Deliberately outside Lists: it is an undo point,
    // not something to browse, share or apply by hand.
    //
    public ModList? Snapshot { get; set; }
}
