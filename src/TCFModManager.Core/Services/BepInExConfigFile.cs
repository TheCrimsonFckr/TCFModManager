using System.Text.RegularExpressions;

namespace TCFModManager.Core.Services;

//
// A BepInEx .cfg file, read as something editable without ever being regenerated.
//
// The format carries its own documentation - every setting is preceded by the author's description
// and by BepInEx's own record of the setting's type, default and acceptable values - which is what
// makes a typed editor possible without a schema from anywhere else.
//
// Saving is a line-preserving edit rather than a rewrite: the file is held as its original lines,
// and ToText replaces only the "Key = Value" lines whose value actually changed. Comments, blank
// lines, ordering, and any line this parser didn't understand all come back out exactly as they went
// in. That matters more here than it looks - a mod's .cfg is often the only documentation its
// settings have, and a rewrite that dropped the comments would take that with it.
//
public sealed class BepInExConfigFile
{
    private readonly List<string> _lines;

    //
    // What followed each line in the original file - "\r\n", "\n", or "" for the last one. Held per
    // line rather than as a single newline style for the whole file, because real .cfg files are not
    // consistent: an author whose setting description contains a bare "\n" gets that written into an
    // otherwise CRLF file verbatim, and a writer that picked one style would silently rewrite every
    // one of those lines. Two of the ten configs in the install this was checked against are like
    // that.
    //
    private readonly List<string> _terminators;

    private readonly List<BepInExConfigSetting> _settings;

    private BepInExConfigFile(
        List<string> lines,
        List<string> terminators,
        List<BepInExConfigSetting> settings,
        List<BepInExConfigSection> sections)
    {
        _lines = lines;
        _terminators = terminators;
        _settings = settings;
        Sections = sections;
    }

    // The file's settings grouped by [Section], in the order both appear in the file.
    public IReadOnlyList<BepInExConfigSection> Sections { get; }

    public IReadOnlyList<BepInExConfigSetting> Settings => _settings;

    public bool IsModified => _settings.Any(s => s.IsModified);

    public static BepInExConfigFile Parse(string text)
    {
        var (lines, terminators) = SplitKeepingLineEndings(text);

        var settings = new List<BepInExConfigSetting>();
        var order = new List<string>();
        var bySection = new Dictionary<string, List<BepInExConfigSetting>>(StringComparer.Ordinal);

        // A setting's description and metadata are the comment lines immediately above it, so they
        // accumulate as the file is walked and are cleared by anything that breaks the run.
        var description = new List<string>();
        var metadata = new SettingMetadata();
        var section = string.Empty;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                description.Clear();
                metadata = new SettingMetadata();
                continue;
            }

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                section = trimmed[1..^1].Trim();
                description.Clear();
                metadata = new SettingMetadata();
                continue;
            }

            // "## " is the author's description of the setting; a single "#" is BepInEx's own
            // machine-written note about it.
            if (trimmed.StartsWith("##", StringComparison.Ordinal))
            {
                description.Add(trimmed[2..].Trim());
                continue;
            }

            if (trimmed.StartsWith('#'))
            {
                metadata.Read(trimmed[1..].Trim());
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals < 0)
            {
                description.Clear();
                metadata = new SettingMetadata();
                continue;
            }

            var key = line[..equals].Trim();
            if (key.Length == 0)
            {
                description.Clear();
                metadata = new SettingMetadata();
                continue;
            }

            // BepInEx trims both sides when it reads a value back, so a value is stored trimmed and
            // written back in BepInEx's own "key = value" shape.
            var value = line[(equals + 1)..].Trim();
            var prefix = line[..(equals + 1)] + (line.Length > equals + 1 && line[equals + 1] == ' ' ? " " : string.Empty);

            var setting = new BepInExConfigSetting
            {
                Section = section,
                Key = key,
                Description = string.Join(Environment.NewLine, description),
                SettingType = metadata.SettingType,
                DefaultValue = metadata.DefaultValue,
                AcceptableValues = metadata.AcceptableValues,
                RangeMinimum = metadata.RangeMinimum,
                RangeMaximum = metadata.RangeMaximum,
                AllowsMultipleValues = metadata.AllowsMultipleValues,
                OriginalValue = value,
                Value = value,
                LineIndex = i,
                LinePrefix = prefix,
            };

            //
            // BepInEx takes the last occurrence when the same key appears twice in a section, so the
            // later one is the live setting and is what an edit has to land on. The earlier line is
            // left in the file untouched, exactly as BepInEx leaves it.
            //
            if (!bySection.TryGetValue(section, out var list))
            {
                bySection[section] = list = [];
                order.Add(section);
            }

            var duplicate = list.FindIndex(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
            if (duplicate >= 0)
            {
                settings.Remove(list[duplicate]);
                list[duplicate] = setting;
            }
            else
            {
                list.Add(setting);
            }

            settings.Add(setting);

            description.Clear();
            metadata = new SettingMetadata();
        }

        var sections = order
            .Select(name => new BepInExConfigSection { Name = name, Settings = bySection[name] })
            .ToList();

        return new BepInExConfigFile(lines, terminators, settings, sections);
    }

    //
    // Splits text into lines and the line ending that followed each, so the file can be put back
    // together exactly as it came apart. The last entry always has an empty terminator and may be an
    // empty line, which is what a file ending in a newline looks like here.
    //
    private static (List<string> Lines, List<string> Terminators) SplitKeepingLineEndings(string text)
    {
        var lines = new List<string>();
        var terminators = new List<string>();
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;

            var end = i;
            var terminator = "\n";

            if (end > start && text[end - 1] == '\r')
            {
                end--;
                terminator = "\r\n";
            }

            lines.Add(text[start..end]);
            terminators.Add(terminator);
            start = i + 1;
        }

        lines.Add(text[start..]);
        terminators.Add(string.Empty);

        return (lines, terminators);
    }

    // The file as it should be written back: the original lines and their own line endings, with
    // only the changed values replaced.
    public string ToText()
    {
        var output = new System.Text.StringBuilder();
        var replacements = _settings
            .Where(s => s.LineIndex >= 0 && s.LineIndex < _lines.Count)
            .ToDictionary(s => s.LineIndex, s => s);

        for (var i = 0; i < _lines.Count; i++)
        {
            output.Append(replacements.TryGetValue(i, out var setting)
                ? setting.LinePrefix + setting.Value
                : _lines[i]);

            output.Append(_terminators[i]);
        }

        return output.ToString();
    }

    // Puts every setting back to the default BepInEx recorded for it, where it recorded one.
    public void ResetAllToDefault()
    {
        foreach (var setting in _settings) setting.ResetToDefault();
    }

    // Marks the current values as the saved ones, so nothing reads as modified after a successful write.
    public void AcceptChanges()
    {
        foreach (var setting in _settings) setting.AcceptChanges();
    }

    // The "# ..." lines BepInEx writes above each setting, accumulated as the file is walked.
    private sealed class SettingMetadata
    {
        private static readonly Regex RangePattern = new(
            @"^From\s+(?<from>.+?)\s+to\s+(?<to>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public string? SettingType { get; private set; }
        public string? DefaultValue { get; private set; }
        public IReadOnlyList<string> AcceptableValues { get; private set; } = [];
        public string? RangeMinimum { get; private set; }
        public string? RangeMaximum { get; private set; }
        public bool AllowsMultipleValues { get; private set; }

        public void Read(string line)
        {
            if (TryValue(line, "Setting type:", out var type))
            {
                SettingType = type;
                return;
            }

            // Kept even when empty: an empty default is a real default ("") and has to stay
            // distinguishable from a setting BepInEx recorded no default for at all.
            if (TryValue(line, "Default value:", out var defaultValue))
            {
                DefaultValue = defaultValue;
                return;
            }

            if (TryValue(line, "Acceptable values:", out var values))
            {
                AcceptableValues = values
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
                return;
            }

            if (TryValue(line, "Acceptable value range:", out var range))
            {
                var match = RangePattern.Match(range);
                if (!match.Success) return;

                RangeMinimum = match.Groups["from"].Value.Trim();
                RangeMaximum = match.Groups["to"].Value.Trim();
                return;
            }

            // BepInEx writes this under a flags enum's acceptable values.
            if (line.StartsWith("Multiple values can be set at the same time", StringComparison.OrdinalIgnoreCase))
                AllowsMultipleValues = true;
        }

        private static bool TryValue(string line, string prefix, out string value)
        {
            if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = string.Empty;
                return false;
            }

            value = line[prefix.Length..].Trim();
            return true;
        }
    }
}

// One [Section] of a .cfg file, with the settings under it in file order.
public sealed class BepInExConfigSection
{
    public required string Name { get; init; }

    public required IReadOnlyList<BepInExConfigSetting> Settings { get; init; }

    // BepInEx allows a setting before any section header. Shown under a heading of its own rather
    // than an empty one.
    public string DisplayName => Name.Length == 0 ? "General" : Name;
}

//
// One "Key = Value" setting, together with everything the file says about it. Value is the only
// mutable part - editing it is what a save writes back, and nothing else about the file changes.
//
public sealed class BepInExConfigSetting
{
    public required string Section { get; init; }

    public required string Key { get; init; }

    // The author's own "##" description lines, joined. Empty when the setting has none.
    public required string Description { get; init; }

    // BepInEx's recorded type - "Boolean", "Int32", "Single", "String", "KeyboardShortcut", or an
    // enum's own type name. Null when the file doesn't say.
    public string? SettingType { get; init; }

    // The default BepInEx recorded, which is what makes a per-setting reset possible. Null when the
    // file doesn't say; empty string when the default genuinely is an empty value.
    public string? DefaultValue { get; init; }

    // The values an enum-typed setting accepts, in the order the file lists them.
    public IReadOnlyList<string> AcceptableValues { get; init; } = [];

    // The ends of a numeric setting's declared range. Both null when it has none. Kept as the file's
    // own text rather than parsed to a number, since the range applies to whatever numeric type the
    // setting is and re-formatting it would misrepresent the file.
    public string? RangeMinimum { get; init; }

    public string? RangeMaximum { get; init; }

    // True for a flags enum, where the value is a comma-separated combination rather than one choice.
    public bool AllowsMultipleValues { get; init; }

    // What the file held when it was last read or saved. Kept so an edit can be recognised, and
    // undone. Backed by a field rather than a plain init-only property because a successful save has
    // to move it forward without rebuilding the setting.
    private string _savedValue = string.Empty;

    public required string OriginalValue
    {
        get => _savedValue;
        init => _savedValue = value;
    }

    public required string Value { get; set; }

    public bool IsModified => !string.Equals(Value, OriginalValue, StringComparison.Ordinal);

    public bool HasDefault => DefaultValue is not null;

    public bool IsAtDefault => DefaultValue is not null && string.Equals(Value, DefaultValue, StringComparison.Ordinal);

    // True when the setting is a fixed choice - an enum, whose acceptable values the file lists.
    public bool HasChoices => AcceptableValues.Count > 0;

    // True when the setting is numeric with both ends of its range declared, so it can be shown as a slider.
    public bool HasRange => RangeMinimum is not null && RangeMaximum is not null;

    public bool IsBoolean => string.Equals(SettingType, "Boolean", StringComparison.OrdinalIgnoreCase);

    public void ResetToDefault()
    {
        if (DefaultValue is not null) Value = DefaultValue;
    }

    public void Revert() => Value = OriginalValue;

    internal void AcceptChanges() => _savedValue = Value;

    internal int LineIndex { get; init; }

    // The setting's own line up to and including its "=", plus the space after it when the file had
    // one - so a rewritten line keeps the file's own spacing rather than being reformatted.
    internal string LinePrefix { get; init; } = string.Empty;
}
