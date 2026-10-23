using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// 
// Loads/saves AppSettings as JSON under &lt;app folder&gt;\Data\settings.json (see AppPaths).
// 
public sealed class SettingsService
{
    private readonly JsonFile<AppSettings> _file =
        new(Path.Combine(AppPaths.DataDirectory, "settings.json"), JsonFileOptions.Indented, keepBackups: true);

    public AppSettings Load() => _file.Read() ?? new AppSettings();

    public void Save(AppSettings settings) => _file.Write(settings);
}
