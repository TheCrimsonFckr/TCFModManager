namespace TCFModManager.Core.Models;

// A mod installed by this app, and exactly which files it placed. Only exists for app-managed installs, not mods installed by hand or otherwise placed outside the app.
public sealed class InstalledModRecord
{
    public required int ModId { get; init; }

    // The catalog mod's GUID at install time.
    public string? Guid { get; init; }

    // The catalog mod's display name at install time.
    public required string Name { get; init; }

    public required int VersionId { get; init; }
    public required string Version { get; init; }
    public required DateTimeOffset InstalledAt { get; init; }

    // Every file this install placed, relative to the SPT install root, forward-slash separated. The exact list Uninstall deletes.
    public required List<string> Files { get; init; }

    // 
    // The mod folder names this install placed (e.g. "EpicsAIO", "WTT-ClientCommonLib") - the same
    // names InstalledModScanner reports, so the Installed page can tie a folder on disk back to
    // this record rather than guessing the catalog listing from the folder name.
    // 
    // Empty on records written before this was stored; InstalledModFolders.Resolve derives
    // them from Files in that case, so an existing manifest keeps working without reinstalling.
    public List<string> Folders { get; init; } = [];

    //
    // True when the install failed partway through and Files lists only what was placed before it
    // stopped. The record is written anyway so those files stay app-managed - a reinstall replaces
    // them, a removal cleans them up - rather than being left orphaned in the install.
    //
    public bool Incomplete { get; init; }
}

// The full set of installed-mod records for one SPT install.
public sealed class ModInstallManifest
{
    public List<InstalledModRecord> Mods { get; init; } = [];
}
