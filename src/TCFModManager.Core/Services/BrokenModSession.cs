using System.Text.Json;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

public sealed class BrokenModSession
{
    public string InstallPath { get; set; } = "";

    public DateTimeOffset StartedAt { get; set; }

    public List<string> Mods { get; set; } = [];

    public Dictionary<string, long>? Logs { get; set; }
}

//
// OPEN-54 N27: Data\broken_mod_search.json - written before the first mod is turned off, deleted once
// they are all back on, so a search cut short is put right at the next start.
//
public sealed class BrokenModSessionStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _filePath;

    public BrokenModSessionStore() : this(Path.Combine(AppPaths.DataDirectory, "broken_mod_search.json"))
    {
    }

    public BrokenModSessionStore(string filePath) => _filePath = filePath;

    public void Save(BrokenModSession session) =>
        SafeFile.WriteText(_filePath, JsonSerializer.Serialize(session, Options));

    public BrokenModSession? Load()
    {
        if (!File.Exists(_filePath)) return null;

        try
        {
            var session = JsonSerializer.Deserialize<BrokenModSession>(File.ReadAllText(_filePath));
            return session is { InstallPath.Length: > 0, Mods: not null } ? session : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppLog.Warn("Search", $"broken_mod_search.json couldn't be read: {ex.Message}");
            return null;
        }
    }

    public void Clear()
    {
        try
        {
            File.Delete(_filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Search", $"broken_mod_search.json couldn't be deleted: {ex.Message}");
        }
    }

    // Turns the session's mods on or off so exactly these are on. A mod in neither place, or both, is left.
    public static ModDisableOutcome Set(BrokenModSession session, IReadOnlySet<string> on)
    {
        var turnOff = new List<InstalledMod>();
        var turnOn = new List<InstalledMod>();

        foreach (var enabledPath in session.Mods)
        {
            if (DisabledModPaths.Counterpart(enabledPath) is not { } disabledPath) continue;

            var isOn = Exists(enabledPath);
            var isOff = Exists(disabledPath);
            if (isOn == isOff) continue;

            var wanted = on.Contains(enabledPath);
            if (isOn && !wanted) turnOff.Add(Entry(enabledPath, disabled: false));
            else if (isOff && wanted) turnOn.Add(Entry(disabledPath, disabled: true));
        }

        var off = ModDisableService.Apply(turnOff, disable: true, session.InstallPath);
        var back = ModDisableService.Apply(turnOn, disable: false, session.InstallPath);
        return new ModDisableOutcome([.. off.Moved, .. back.Moved], [.. off.Failed, .. back.Failed]);
    }

    public static ModDisableOutcome PutBack(BrokenModSession session) =>
        Set(session, session.Mods.ToHashSet(StringComparer.OrdinalIgnoreCase));

    public static int TurnedOff(BrokenModSession session) =>
        session.Mods.Count(path => !Exists(path) && DisabledModPaths.Counterpart(path) is { } off && Exists(off));

    private static bool Exists(string path) => Directory.Exists(path) || File.Exists(path);

    private static InstalledMod Entry(string path, bool disabled) => new()
    {
        Name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
        Target = InstalledModTarget.Server,
        FolderPath = path,
        IsDisabled = disabled,
    };
}
