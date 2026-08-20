using System.Text.Json;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// 
// Loads/saves the ModInstallManifest as JSON under &lt;app folder&gt;\Data\installed-mods.json.
// A corrupt or hand-edited manifest falls back to an empty one rather than blocking the app.
// 
public sealed class ModInstallManifestService
{
    private readonly string _filePath;

    public ModInstallManifestService()
    {
        AppPaths.MigrateLegacyFile("installed-mods.json");
        _filePath = Path.Combine(AppPaths.DataDirectory, "installed-mods.json");
    }

    public ModInstallManifest Load()
    {
        if (!File.Exists(_filePath)) return new ModInstallManifest();

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<ModInstallManifest>(json) ?? new ModInstallManifest();
        }
        catch (JsonException)
        {
            return new ModInstallManifest();
        }
    }

    public void Save(ModInstallManifest manifest)
    {
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}
