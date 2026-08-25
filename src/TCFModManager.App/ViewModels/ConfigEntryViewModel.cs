using System.IO;
using TCFModManager.Core.Models;

namespace TCFModManager.App.ViewModels;

// Display wrapper for one config file in the Configs page's list.
public sealed class ConfigEntryViewModel
{
    public required ModConfigEntry Entry { get; init; }

    public string FullPath => Entry.FullPath;

    public string FileName => Entry.FileName;

    public ModConfigSource Source => Entry.Source;

    // What the list groups on. See ConfigSectionHeader for why this is a record derived per entry.
    public ConfigSectionHeader Section => new(Entry.Source);

    //
    // The mod's folder/package name, or - for a file no mod owns - the file's own name without its
    // extension, so the row doesn't read as the same string twice with the path underneath.
    //
    // The installed folder name is used deliberately rather than the catalog's prettier display
    // name: matching every installed mod against the catalog is the expensive pass that made the
    // Installed page hang, and the folder name is what the path underneath actually says.
    //
    public string Title => Entry.ModName ?? Path.GetFileNameWithoutExtension(Entry.FileName);

    //
    // The file's path within its own container, so the two mods that both keep a "config.json" are
    // never ambiguous - "betterkeys/config/config.jsonc" rather than the full path, since the
    // container is already the heading this row sits under.
    //
    public string Subtitle => Entry.Source switch
    {
        ModConfigSource.Server => After(Entry.DisplayPath, "user/mods/") ?? After(Entry.DisplayPath, "user/mods.disabled/") ?? Entry.DisplayPath,
        _ => After(Entry.DisplayPath, "BepInEx/config/") ?? Entry.DisplayPath,
    };

    public string Glyph => Entry.Format == ModConfigFormat.BepInExCfg ? "TextboxSettings24" : "Braces24";

    public string FormatLabel => Entry.Format == ModConfigFormat.BepInExCfg ? "BepInEx config" : "JSON";

    //
    // A pristine copy of the defaults shipped alongside the real config - "config.default.json",
    // "defaultConfig.jsonc". Editing one changes nothing the mod reads, so the row says so and is
    // dimmed. It is still listed rather than hidden: it is the thing to look at when working out
    // what a setting was before it was changed.
    //
    public bool IsShippedDefault
    {
        get
        {
            var stem = Path.GetFileNameWithoutExtension(Entry.FileName);

            return stem.Contains(".default", StringComparison.OrdinalIgnoreCase)
                || stem.StartsWith("default", StringComparison.OrdinalIgnoreCase);
        }
    }

    public string? Badge => IsShippedDefault
        ? "Shipped default"
        : Entry.IsModDisabled
            ? "Mod disabled"
            : null;

    public double RowOpacity => IsShippedDefault ? 0.6 : 1.0;

    //
    // The one line that explains what editing this file actually means, shown above the editor.
    // This is the difference the whole page is organised around: a client mod's settings live
    // outside its folder and outlive it, a server mod's live inside it and go where it goes.
    //
    public string LocationNote => Entry.Source switch
    {
        ModConfigSource.Client =>
            "Kept in BepInEx\\config, outside the mod's own folder - it survives disabling the mod, and removing it.",
        ModConfigSource.Server =>
            "Kept inside the mod's own folder - removing the mod sets this file aside rather than deleting it.",
        ModConfigSource.Framework =>
            "BepInEx's own configuration rather than any mod's. Changing it affects how every plugin is loaded.",
        _ =>
            "No installed plugin claims this file, so the mod it belonged to has most likely been removed. Editing it will not affect anything until that mod is installed again.",
    };

    // Extra note for the rarer states, shown under LocationNote when it applies.
    public string? StateNote => IsShippedDefault
        ? "This is the pristine copy of the defaults the mod ships with, not the config it reads. Edits here have no effect."
        : Entry.IsModDisabled
            ? "This mod is disabled, so SPT isn't loading it. The file moved into the \".disabled\" folder with the rest of the mod, and edits will apply when it's enabled again."
            : null;

    public bool HasStateNote => StateNote is not null;

    // Everything after the first occurrence of marker, or null when the path doesn't contain it.
    private static string? After(string path, string marker)
    {
        var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? null : path[(index + marker.Length)..];
    }

    // Whether this row survives the search box. Matches the mod name, the file name and the path,
    // so both "sain" and "config.jsonc" find something.
    public bool MatchesSearch(string term) =>
        Title.Contains(term, StringComparison.OrdinalIgnoreCase)
        || Entry.FileName.Contains(term, StringComparison.OrdinalIgnoreCase)
        || Entry.DisplayPath.Contains(term, StringComparison.OrdinalIgnoreCase);
}
