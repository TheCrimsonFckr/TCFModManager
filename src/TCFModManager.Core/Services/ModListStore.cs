using System.Text.Json;
using System.Text.Json.Serialization;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Loads/saves ModListData as JSON under <app folder>\Data\mod_lists.json - every mod list this
// install holds and which one is currently applied.
//
// Nothing here touches the game folder. A list is a description of a set of mods; turning one into
// files on disk is the apply engine's job, and reading which mods are installed is the scanner's.
// A corrupt or hand-edited file falls back to an empty set rather than blocking the app.
//
public sealed class ModListStore
{
    private readonly string _filePath;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public ModListStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(AppPaths.DataDirectory, "mod_lists.json");
    }

    public string FilePath => _filePath;

    public ModListData Load()
    {
        if (!File.Exists(_filePath)) return new ModListData();

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<ModListData>(json, Options) ?? new ModListData();
        }
        catch (JsonException)
        {
            return new ModListData();
        }
    }

    public void Save(ModListData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(data, Options));
    }

    public ModList? Find(Guid id) => Load().Lists.FirstOrDefault(l => l.Id == id);

    // Stores a list built elsewhere - a capture, an import, a fork. Replaces an existing list with
    // the same Id, so re-importing a newer revision of a list updates it rather than duplicating it.
    public ModList Add(ModList list)
    {
        var data = Load();
        data.Lists.RemoveAll(l => l.Id == list.Id);
        data.Lists.Add(list);
        Save(data);
        return list;
    }

    public void Rename(Guid id, string name)
    {
        var data = Load();
        var list = data.Lists.FirstOrDefault(l => l.Id == id);
        if (list is null || !list.IsEditable || list.Name == name) return;

        list.Name = name;
        list.UpdatedAt = DateTimeOffset.UtcNow;
        Save(data);
    }

    public void SetPolicy(Guid id, ModListPolicy policy)
    {
        var data = Load();
        var list = data.Lists.FirstOrDefault(l => l.Id == id);
        if (list is null || !list.IsEditable || list.Policy == policy) return;

        list.Policy = policy;
        list.UpdatedAt = DateTimeOffset.UtcNow;
        Save(data);
    }

    // Replaces a local list's contents and bumps its revision. Refused for an imported or served
    // list - Fork is the way to edit one of those.
    public ModList? ReplaceEntries(Guid id, IEnumerable<ModListEntry> entries)
    {
        var data = Load();
        var list = data.Lists.FirstOrDefault(l => l.Id == id);
        if (list is null || !list.IsEditable) return null;

        list.Entries.Clear();
        list.Entries.AddRange(entries);
        list.Revision++;
        list.UpdatedAt = DateTimeOffset.UtcNow;
        Save(data);
        return list;
    }

    // Copies an imported or served list into a new local list carrying a DerivedFrom pointer back
    // at it, so the original stays exactly as it was received.
    public ModList Fork(Guid id, string name, DateTimeOffset timestamp)
    {
        var data = Load();
        var source = data.Lists.FirstOrDefault(l => l.Id == id)
            ?? throw new InvalidOperationException($"No mod list with id {id}.");

        var fork = new ModList
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = source.Description,
            Revision = 1,
            Origin = ModListOrigin.Local,
            Policy = source.Policy,
            DerivedFrom = source.Id,
            Source = source.Source,
            SptVersion = source.SptVersion,
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
        };

        fork.Entries.AddRange(source.Entries);
        data.Lists.Add(fork);
        Save(data);
        return fork;
    }

    // Removes the list. Clears the active pointer too when it was the one being followed, so the
    // install stops claiming to follow a list that no longer exists.
    public void Delete(Guid id)
    {
        var data = Load();
        if (data.Lists.RemoveAll(l => l.Id == id) == 0) return;

        if (data.ActiveListId == id) data.ActiveListId = null;
        Save(data);
    }

    // Marks which list the install is currently following, or null for none. One at a time.
    public void SetActive(Guid? id)
    {
        var data = Load();
        if (id is not null && data.Lists.All(l => l.Id != id)) return;
        if (data.ActiveListId == id) return;

        data.ActiveListId = id;
        Save(data);
    }

    public ModList? GetActive()
    {
        var data = Load();
        return data.ActiveListId is null ? null : data.Lists.FirstOrDefault(l => l.Id == data.ActiveListId);
    }
}
