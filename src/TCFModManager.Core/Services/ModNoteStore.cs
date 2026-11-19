namespace TCFModManager.Core.Services;

public sealed class ModNoteStore
{
    private readonly JsonFile<Dictionary<string, string>> _file;

    public ModNoteStore(string? path = null)
    {
        _file = new(path ?? Path.Combine(AppPaths.DataDirectory, "mod_notes.json"), JsonFileOptions.Indented, keepBackups: true);
    }

    public IReadOnlyDictionary<string, string> Load() =>
        new Dictionary<string, string>(_file.Read() ?? [], StringComparer.OrdinalIgnoreCase);

    public string? For(string modName) =>
        Load().TryGetValue(ModGroupStore.KeyFor(modName), out var note) ? note : null;

    public void Set(string modName, string? note)
    {
        var notes = new Dictionary<string, string>(Load(), StringComparer.OrdinalIgnoreCase);
        var key = ModGroupStore.KeyFor(modName);
        var text = note?.Trim();

        if (string.IsNullOrEmpty(text)) notes.Remove(key);
        else notes[key] = text;

        _file.Write(notes);
    }
}
