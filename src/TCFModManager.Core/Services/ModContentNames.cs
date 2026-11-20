using System.Text.Json;

namespace TCFModManager.Core.Services;

//
// OPEN-53 N7: names for the items and traders a mod adds, read from the mod's own JSON - locale keys
// ("<id> Name", "<id> Nickname"), a "locales" block on an item or trader that carries its id, a
// trader's "nickname", and an item's "_props.Name" or "_name" last. An id a mod names only in code
// gets no name.
//
public static class ModContentNames
{
    private const long LargestFileRead = 16L * 1024 * 1024;

    private static readonly string[] IdKeys = ["_id", "id", "newId", "newItemId", "itemId", "itemTplId"];

    private static readonly HashSet<string> LocaleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ch", "cz", "de", "en", "es", "es-mx", "fr", "ge", "hu", "it", "jp", "kr", "pl", "po", "pt", "ro", "ru", "sk", "tu", "tr", "zh",
    };

    private static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    //
    // The best name found for each wanted id, in <paramref name="language"/> (a two-letter code)
    // when the mod has it, else English, else whatever it has.
    //
    public static IReadOnlyDictionary<string, string> Find(IEnumerable<string> paths, IReadOnlySet<string> ids, string? language = null)
    {
        var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids) wanted.TryAdd(id, id);
        if (wanted.Count == 0) return new Dictionary<string, string>();

        var search = new Search(wanted, Preferred(language));
        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path)) search.File(path);
                else if (Directory.Exists(path))
                {
                    foreach (var file in Directory.EnumerateFiles(path, "*.json*", SearchOption.AllDirectories)) search.File(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Debug("Profiles", $"couldn't search {path} for names: {ex.Message}");
            }
        }

        return search.Found.ToDictionary(f => f.Key, f => f.Value.Name, StringComparer.Ordinal);
    }

    private static string[] Preferred(string? language) =>
        string.IsNullOrWhiteSpace(language) ? []
        : string.Equals(language, "de", StringComparison.OrdinalIgnoreCase) ? ["ge", "de"]
        : [language];

    private sealed class Search(Dictionary<string, string> wanted, string[] preferred)
    {
        public Dictionary<string, (int Rank, string Name)> Found { get; } = new(StringComparer.Ordinal);

        public void File(string file)
        {
            var extension = Path.GetExtension(file);
            if (!extension.Equals(".json", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".jsonc", StringComparison.OrdinalIgnoreCase)) return;

            try
            {
                if (new FileInfo(file).Length > LargestFileRead) return;
                var text = System.IO.File.ReadAllText(file);
                if (!wanted.Keys.Any(id => text.Contains(id, StringComparison.OrdinalIgnoreCase))) return;

                using var doc = JsonDocument.Parse(text, Options);
                var stem = Path.GetFileNameWithoutExtension(file);
                Walk(doc.RootElement, null, LocaleCodes.Contains(stem) ? stem : null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                AppLog.Debug("Profiles", $"couldn't read {file} for names: {ex.Message}");
            }
        }

        private void Walk(JsonElement element, string? self, string? language)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in element.EnumerateArray()) Walk(child, null, language);
                return;
            }

            if (element.ValueKind != JsonValueKind.Object) return;

            if (self is null)
            {
                foreach (var key in IdKeys)
                {
                    if (element.TryGetProperty(key, out var value) && Wanted(value) is { } id)
                    {
                        self = id;
                        break;
                    }
                }
            }

            if (self is not null) Own(element, self, language);

            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String && LocaleKey(property.Name) is { } keyed)
                {
                    Offer(keyed, property.Value.GetString(), Rank(language));
                    continue;
                }

                var childLanguage = LocaleCodes.Contains(property.Name) ? property.Name : language;
                Walk(property.Value, wanted.TryGetValue(property.Name, out var id) ? id : null, childLanguage);
            }
        }

        private void Own(JsonElement element, string self, string? language)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals("locales", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var locale in property.Value.EnumerateObject())
                    {
                        if (locale.Value.ValueKind == JsonValueKind.Object && Text(locale.Value, "name") is { } name)
                            Offer(self, name, Rank(locale.Name));
                    }
                }
                else if (property.Name.Equals("locale", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.Object
                         && Text(property.Value, "name") is { } name)
                {
                    Offer(self, name, Rank(language) + 1);
                }
                else if (property.Name.Equals("nickname", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                {
                    Offer(self, property.Value.GetString(), Rank(language) + 1);
                }
                else if (property.Name == "_props" && property.Value.ValueKind == JsonValueKind.Object && Text(property.Value, "Name") is { } propsName)
                {
                    Offer(self, propsName, 8);
                }
                else if (property.Name == "_name" && property.Value.ValueKind == JsonValueKind.String)
                {
                    Offer(self, property.Value.GetString(), 9);
                }
            }
        }

        private int Rank(string? language) =>
            language is null ? 3
            : preferred.Contains(language, StringComparer.OrdinalIgnoreCase) ? 0
            : language.Equals("en", StringComparison.OrdinalIgnoreCase) ? 1
            : 5;

        private void Offer(string id, string? name, int rank)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name)) return;
            if (Found.TryGetValue(id, out var have) && have.Rank <= rank) return;
            Found[id] = (rank, name);
        }

        private string? Wanted(JsonElement value) =>
            value.ValueKind == JsonValueKind.String && value.GetString() is { Length: 24 } s && wanted.TryGetValue(s, out var id) ? id : null;

        private string? LocaleKey(string key)
        {
            if (key.Length < 29 || key[24] != ' ') return null;
            var suffix = key.AsSpan(25);
            if (!suffix.Equals("Name", StringComparison.OrdinalIgnoreCase) && !suffix.Equals("Nickname", StringComparison.OrdinalIgnoreCase)) return null;
            return wanted.TryGetValue(key[..24], out var id) ? id : null;
        }

        private static string? Text(JsonElement element, string name)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                    return property.Value.GetString();
            }

            return null;
        }
    }
}
