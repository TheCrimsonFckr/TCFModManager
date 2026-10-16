using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

public sealed record ThemeImport(ThemeDefinition? Theme, ThemeFileProblem? Problem, bool Replaced)
{
    public bool Succeeded => Theme is not null;
}

public sealed class ThemeStore
{
    private readonly string _folder;

    public ThemeStore(string? folder = null)
    {
        _folder = folder ?? Path.Combine(AppPaths.DataDirectory, "themes");
    }

    public string Folder => _folder;

    public IReadOnlyList<ThemeDefinition> LoadAll()
    {
        if (!Directory.Exists(_folder)) return [];

        var themes = new List<ThemeDefinition>();

        foreach (var path in Directory.EnumerateFiles(_folder, "*.json"))
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            if (!Guid.TryParse(stem, out var id)) continue;

            var read = ThemeFile.ReadFile(path);
            if (read.Theme is not { } theme)
            {
                SafeFile.PreserveDamaged(path);
                AppLog.Warn("Themes", $"skipped {Path.GetFileName(path)}: {read.Problem}");
                continue;
            }

            theme.Id = IdText(id);
            themes.Add(theme);
        }

        return themes.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public ThemeDefinition? Find(string id)
    {
        if (!TryId(id, out var guid) || !File.Exists(PathFor(guid))) return null;

        var theme = ThemeFile.ReadFile(PathFor(guid)).Theme;
        if (theme is not null) theme.Id = IdText(guid);
        return theme;
    }

    public ThemeDefinition Save(ThemeDefinition theme)
    {
        ThemeRules.Normalise(theme);

        if (!TryId(theme.Id, out var guid)) guid = Guid.NewGuid();
        theme.Id = IdText(guid);

        Directory.CreateDirectory(_folder);
        SafeFile.WriteText(PathFor(guid), ThemeFile.Write(theme), keepBackups: true);
        return theme;
    }

    public bool Delete(string id)
    {
        if (!TryId(id, out var guid)) return false;

        var path = PathFor(guid);
        if (!File.Exists(path)) return false;

        File.Delete(path);
        return true;
    }

    public ThemeImport Import(string json)
    {
        var read = ThemeFile.Read(json);
        if (read.Theme is not { } theme) return new ThemeImport(null, read.Problem, Replaced: false);

        var replaced = TryId(theme.Id, out var guid) && File.Exists(PathFor(guid));

        return new ThemeImport(Save(theme), null, replaced);
    }

    public ThemeImport ImportFile(string path)
    {
        try
        {
            return Import(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ThemeImport(null, ThemeFileProblem.Unreadable, Replaced: false);
        }
    }

    public static bool IsCustomId(string? id) => TryId(id, out _);

    private string PathFor(Guid id) => Path.Combine(_folder, IdText(id) + ".json");

    private static bool TryId(string? id, out Guid guid) => Guid.TryParse(id, out guid) && guid != Guid.Empty;

    private static string IdText(Guid id) => id.ToString("D");
}
