namespace TCFModManager.Core.Models;

// Which half of an SPT install a discovered mod lives in: BepInEx plugins/patchers (client) or user/mods (server).
public enum InstalledModTarget
{
    Client,
    Server,
}

// One mod found on disk by InstalledModScanner. Represents what's installed locally, not a catalog listing.
public sealed class InstalledMod
{
    public required string Name { get; init; }

    // Null when no version could be determined.
    public string? Version { get; init; }

    // For a client mod, the plugin's [BepInPlugin] GUID read from the compiled DLL; null for server mods or if it couldn't be read.
    public string? Guid { get; init; }

    // Populated only for server mods, from package.json's "author" field.
    public string? Author { get; init; }

    public required InstalledModTarget Target { get; init; }

    // Full path to the mod's folder, or the DLL itself for a loose client DLL.
    public required string FolderPath { get; init; }

    // The folder's (or loose DLL's) filesystem creation time, used as a proxy for install date. Null if it couldn't be read.
    public DateTimeOffset? InstalledAt { get; init; }
}
