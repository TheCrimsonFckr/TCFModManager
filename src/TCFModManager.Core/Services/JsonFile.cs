using System.Text.Json;

namespace TCFModManager.Core.Services;

public sealed class JsonFile<T>(string path, JsonSerializerOptions? options = null, bool keepBackups = false, bool preserveDamaged = true)
    where T : class
{
    public string FilePath => path;

    public T? Read()
    {
        if (!File.Exists(path)) return null;

        var json = File.ReadAllText(path);

        try
        {
            return JsonSerializer.Deserialize<T>(json, options);
        }
        catch (JsonException)
        {
            if (preserveDamaged) SafeFile.PreserveDamaged(path);
            return null;
        }
    }

    public void Write(T value) =>
        SafeFile.WriteText(path, JsonSerializer.Serialize(value, options), keepBackups);
}

public static class JsonFileOptions
{
    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
}
