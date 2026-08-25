namespace TCFModManager.Core.Models;

// Which half of an SPT install a discovered mod lives in: BepInEx plugins/patchers (client) or user/mods (server).
public enum InstalledModTarget
{
    Client,
    Server,
}

//
// One dependency an installed mod declares about itself. Identifier is a BepInEx plugin GUID for a
// client mod (from [BepInDependency]) or a package.json package name for a server mod (from
// "modDependencies"). A soft dependency is one the dependent still loads without.
//
public sealed record ModDependencyRef(string Identifier, bool IsSoft);

// One mod found on disk by InstalledModScanner. Represents what's installed locally, not a catalog listing.
public sealed class InstalledMod
{
    public required string Name { get; init; }

    // Null when no version could be determined.
    public string? Version { get; init; }

    //
    // The GUID that stands for this mod's identity - the first [BepInPlugin] GUID found in its
    // folder. Null for server mods, and for a client mod whose GUID couldn't be read. Catalog
    // matching and the dependency graph key on this, so it stays one value per mod.
    //
    public string? Guid { get; init; }

    //
    // Every [BepInPlugin] GUID found in this mod's folder, not just the one above.
    //
    // A single mod folder routinely holds several plugin DLLs - an API, a config UI, a utilities
    // assembly - each registering its own GUID, and each keeping its own file in BepInEx\config
    // named after it. Keeping only the first would leave all the others' configs looking like they
    // belonged to no installed mod at all. Empty for a server mod.
    //
    public IReadOnlyList<string> Guids { get; init; } = [];

    // Guids, falling back to the single Guid when the list wasn't populated - so a mod built by
    // hand with only Guid set still answers correctly.
    public IReadOnlyList<string> AllGuids =>
        Guids.Count > 0 ? Guids : Guid is null ? [] : [Guid];

    // Populated only for server mods, from package.json's "author" field.
    public string? Author { get; init; }

    public required InstalledModTarget Target { get; init; }

    //
    // True when this was found under BepInEx\patchers rather than BepInEx\plugins - always false for
    // a server mod. A patcher is run by BepInEx's preloader before the game's own assemblies load,
    // not as a plugin, so it carries no [BepInPlugin] attribute and has no GUID of its own to match
    // against a catalog listing. A mod that ships one almost always ships a plugin alongside it,
    // under a different folder name; InstalledModCardViewModel folds the two back together.
    //
    public bool IsPatcher { get; init; }

    // Full path to the mod's folder, or the DLL itself for a loose client DLL.
    public required string FolderPath { get; init; }

    // The folder's (or loose DLL's) filesystem creation time, used as a proxy for install date. Null if it couldn't be read.
    public DateTimeOffset? InstalledAt { get; init; }

    //
    // True when this mod was found under a ".disabled" sibling of its normal container
    // (e.g. user\mods.disabled) rather than the live one - still on disk, but not loaded by SPT.
    //
    public bool IsDisabled { get; init; }

    // What this mod declares it needs, read from its own files. Empty when it declares nothing.
    public IReadOnlyList<ModDependencyRef> Dependencies { get; init; } = [];
}
