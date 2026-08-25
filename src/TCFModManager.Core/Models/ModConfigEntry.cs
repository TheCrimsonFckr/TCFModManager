namespace TCFModManager.Core.Models;

//
// Where a config file lives. This is the distinction the Configs page is built around, because it
// is a real behavioural difference and not just a label: a client mod's settings sit in
// BepInEx\config, outside the mod's own folder, so they survive both disabling the mod and removing
// it; a server mod's sit inside its user\mods folder, and are what ModConfigFiles.MoveOut rescues
// when the mod is uninstalled.
//
public enum ModConfigSource
{
    // BepInEx\config\<plugin guid>.cfg, tied back to an installed plugin.
    Client,

    // A JSON config inside a server mod's own folder under user\mods.
    Server,

    // BepInEx's own configuration rather than any mod's - still editable, but it is not a mod and
    // shouldn't be listed as if some mod owned it.
    Framework,

    // A .cfg in BepInEx\config that no installed plugin claims. Usually a mod that has since been
    // removed and left its settings behind, which is worth seeing rather than hiding.
    Unmatched,
}

// How a config file is read and written - which decides whether it gets a generated form or a text editor.
public enum ModConfigFormat
{
    // BepInEx's own .cfg format, which declares each setting's type, default and acceptable values
    // in comments, so a typed form can be generated from the file itself.
    BepInExCfg,

    // JSON, JSON5 or JSONC. No schema to build a form from, and authors document settings in
    // comments that a parse-and-rewrite round trip would destroy, so these are edited as text.
    Json,
}

// One config file found in an SPT install, with whatever installed mod it could be attributed to.
public sealed record ModConfigEntry
{
    public required string FullPath { get; init; }

    // FullPath relative to the install root, forward-slashed - what the UI shows, so two mods each
    // holding a "config.json" are never ambiguous.
    public required string DisplayPath { get; init; }

    public required string FileName { get; init; }

    public required ModConfigFormat Format { get; init; }

    public required ModConfigSource Source { get; init; }

    // The installed mod's folder/package name, or null for a Framework or Unmatched file.
    public string? ModName { get; init; }

    // The matched mod's plugin GUID, where it has one - whichever tier actually matched it. Null for
    // a server mod, for a plugin whose GUID couldn't be read, and for a Framework or Unmatched file.
    public string? ModGuid { get; init; }

    //
    // True when the mod owning this file is currently disabled. Only ever true for a Server file:
    // disabling moves user\mods\<mod> and takes its config with it, while BepInEx\config is not a
    // mod container and is never moved.
    //
    public bool IsModDisabled { get; init; }
}
