using System.Text.Json;
using System.Text.Json.Serialization;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// The wrapper a shared mod list travels in.
//
// The list is nested rather than written at the top level so the file can carry things about the
// transfer that aren't part of the list itself, and so a future schema can add fields without the
// list's own shape having to absorb them.
//
public sealed class ModListDocument
{
    // Bumped only when an older app could no longer read a newer file correctly. Written per file
    // by ModListFile.SchemaVersionFor rather than fixed at the current maximum - see that method.
    public int SchemaVersion { get; set; } = ModListFile.BaseSchemaVersion;

    // What wrote it. Informational - never used to decide whether to accept the file.
    public string? App { get; set; }

    // Who shared it, when they said. Becomes the imported list's Source.
    public string? Author { get; set; }

    public DateTimeOffset ExportedAt { get; set; }

    public ModList? List { get; set; }
}

// A parsed file: the list, or why it couldn't be read. Never throws at the caller.
public sealed record ModListImport(ModList? List, string? Error)
{
    public bool Succeeded => List is not null;

    public static ModListImport Failed(string error) => new(null, error);
}

//
// Reads and writes the shareable mod list file.
//
// What travels is a manifest, never mod files - "install mod 2426 at version 5", not somebody's
// archive. That is what keeps sharing free of hosting, bandwidth and redistribution questions, and
// what keeps The Forge's download counts honest.
//
public static class ModListFile
{
    //
    // The version every list has always been written at, and the only one an app that predates
    // addon support can read.
    //
    public const int BaseSchemaVersion = 1;

    //
    // Added when addon entries did. An entry's ModId is an addon id when IsAddon is set, and an app
    // that doesn't know the field reads that id as a mod id - so it would offer to install, update
    // or disable whichever unrelated mod happens to carry the same number. That is exactly the
    // "an older app could no longer read this correctly" case the version exists for.
    //
    public const int AddonSchemaVersion = 2;

    // The highest version this app can read.
    public const int SchemaVersion = AddonSchemaVersion;

    //
    // The version a given list has to be written at. Only a list that actually contains an addon is
    // stamped 2, so every addon-free list stays readable by an older app - the alternative, pinning
    // every export to the newest version, would break sharing between versions to describe a
    // feature the file doesn't use.
    //
    public static int SchemaVersionFor(ModList list) =>
        list.Entries.Any(e => e.IsAddon) ? AddonSchemaVersion : BaseSchemaVersion;

    // Deliberately its own extension rather than .json, so the app can be associated with it later
    // and so a double-click means something.
    public const string Extension = ".tcfmodlist";

    public const string FileFilter = "TCF mod list (*.tcfmodlist)|*.tcfmodlist|All files (*.*)|*.*";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Write(ModList list, string? author = null, DateTimeOffset? exportedAt = null) =>
        JsonSerializer.Serialize(
            new ModListDocument
            {
                SchemaVersion = SchemaVersionFor(list),
                App = "TCFModManager",
                Author = string.IsNullOrWhiteSpace(author) ? null : author.Trim(),
                ExportedAt = exportedAt ?? DateTimeOffset.UtcNow,
                List = list,
            },
            Options);

    public static void Save(ModList list, string path, string? author = null)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        File.WriteAllText(path, Write(list, author));
    }

    //
    // Parses a file's contents into a list ready to store.
    //
    // The imported list keeps its Id and Revision, so receiving a newer revision of a list you
    // already have updates it in place rather than leaving two. Everything that describes how a
    // list relates to *this* install is reset: Origin becomes Imported (which makes it read-only -
    // editing forks it), and a snapshot flag never survives the trip, since somebody else's undo
    // point is not one of yours.
    //
    public static ModListImport Read(string json, string? fallbackSource = null)
    {
        if (string.IsNullOrWhiteSpace(json)) return ModListImport.Failed("the file is empty");

        ModListDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<ModListDocument>(json, Options);
        }
        catch (JsonException ex)
        {
            return ModListImport.Failed($"it isn't a readable mod list file ({ex.Message})");
        }

        if (document?.List is not { } list) return ModListImport.Failed("it doesn't contain a mod list");

        if (document.SchemaVersion > SchemaVersion)
        {
            return ModListImport.Failed(
                $"it was written by a newer version of this app (format {document.SchemaVersion}, this one reads {SchemaVersion})");
        }

        if (list.Id == Guid.Empty) return ModListImport.Failed("the list has no id");
        if (string.IsNullOrWhiteSpace(list.Name)) return ModListImport.Failed("the list has no name");

        var imported = new ModList
        {
            Id = list.Id,
            Name = list.Name.Trim(),
            Description = list.Description,
            Revision = Math.Max(1, list.Revision),
            Origin = ModListOrigin.Imported,
            Policy = list.Policy,
            DerivedFrom = list.DerivedFrom,
            Source = document.Author ?? list.Source ?? fallbackSource,
            SptVersion = list.SptVersion,
            IsSnapshot = false,
            CreatedAt = list.CreatedAt,
            UpdatedAt = list.UpdatedAt,
        };

        // Entries with no name at all can't be shown or matched on, so they are dropped rather than
        // carried through as blanks.
        imported.Entries.AddRange(list.Entries.Where(e => !string.IsNullOrWhiteSpace(e.Name)));

        return new ModListImport(imported, null);
    }

    public static ModListImport Load(string path)
    {
        try
        {
            return Read(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
        }
        catch (IOException ex)
        {
            return ModListImport.Failed($"it couldn't be opened ({ex.Message})");
        }
        catch (UnauthorizedAccessException ex)
        {
            return ModListImport.Failed($"it couldn't be opened ({ex.Message})");
        }
    }

    //
    // A filename for a list, safe on Windows and recognisable in a chat window.
    //
    // The invalid set is written out rather than taken from Path.GetInvalidFileNameChars(), which
    // answers for the platform it is running on: on Linux that is only '/' and NUL, so a name with
    // a colon in it would come back untouched and then be rejected by the Windows machine the file
    // is actually for.
    //
    public static string SuggestedFileName(ModList list)
    {
        const string invalid = "<>:\"/\\|?*";

        var name = new string([.. list.Name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '-' : c)]).Trim();

        return (string.IsNullOrWhiteSpace(name) ? "mod list" : name) + Extension;
    }
}
