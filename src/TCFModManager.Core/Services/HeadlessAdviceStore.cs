using System.Text.Json;
using System.Text.Json.Serialization;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

public sealed class HeadlessAdviceStore
{
    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;

    public HeadlessAdviceStore() : this(Path.Combine(AppPaths.DataDirectory, "headless_advice.json")) { }

    public HeadlessAdviceStore(string filePath) => _filePath = filePath;

    public sealed class Cached
    {
        public int ReaderVersion { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
        public DateTimeOffset CheckedAt { get; set; }
        public HeadlessVerdict Verdict { get; set; }
        public List<string> Quotes { get; set; } = [];

        [JsonIgnore]
        public HeadlessAdvice Advice => Verdict == HeadlessVerdict.None ? HeadlessAdvice.Nothing : new(Verdict, Quotes);
    }

    private sealed class Stored
    {
        public int SchemaVersion { get; set; }
        public Dictionary<string, Cached> Entries { get; set; } = [];
    }

    public Dictionary<string, Cached> Load()
    {
        if (!File.Exists(_filePath)) return new(StringComparer.OrdinalIgnoreCase);

        try
        {
            var data = JsonSerializer.Deserialize<Stored>(File.ReadAllText(_filePath), Json);
            return data is null || data.SchemaVersion != SchemaVersion
                ? new(StringComparer.OrdinalIgnoreCase)
                : new(data.Entries, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Save(IReadOnlyDictionary<string, Cached> entries)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            SafeFile.WriteText(_filePath, JsonSerializer.Serialize(new Stored
            {
                SchemaVersion = SchemaVersion,
                Entries = entries
                    .OrderBy(e => e.Key, StringComparer.Ordinal)
                    .ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase),
            }, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("HeadlessAdvice", $"couldn't save the headless advice cache: {ex.Message}");
        }
    }
}
