using System.Globalization;

namespace TCFModManager.Core.Services;

public enum BepInExValueKind
{
    Text,
    Boolean,
    Integer,
    Number,
    Choice,
    Flags,
    Color,
    Shortcut,
}

public readonly record struct BepInExColor(byte R, byte G, byte B, byte A);

public static class BepInExConfigValues
{
    private const NumberStyles NumberStyle = NumberStyles.Float | NumberStyles.AllowThousands;

    private const string NoFlags = "None";

    private static readonly Dictionary<string, (decimal Min, decimal Max)> IntegerTypes = new(StringComparer.Ordinal)
    {
        ["Byte"] = (byte.MinValue, byte.MaxValue),
        ["SByte"] = (sbyte.MinValue, sbyte.MaxValue),
        ["Int16"] = (short.MinValue, short.MaxValue),
        ["UInt16"] = (ushort.MinValue, ushort.MaxValue),
        ["Int32"] = (int.MinValue, int.MaxValue),
        ["UInt32"] = (uint.MinValue, uint.MaxValue),
        ["Int64"] = (long.MinValue, long.MaxValue),
        ["UInt64"] = (ulong.MinValue, ulong.MaxValue),
    };

    private static readonly HashSet<string> NumberTypes = new(StringComparer.Ordinal) { "Single", "Double", "Decimal" };

    public static BepInExValueKind KindOf(BepInExConfigSetting setting)
    {
        if (setting.IsBoolean) return BepInExValueKind.Boolean;
        if (setting.HasChoices) return setting.AllowsMultipleValues ? BepInExValueKind.Flags : BepInExValueKind.Choice;

        return setting.SettingType switch
        {
            "KeyboardShortcut" => BepInExValueKind.Shortcut,
            "Color" => BepInExValueKind.Color,
            { } type when IntegerTypes.ContainsKey(type) => BepInExValueKind.Integer,
            { } type when NumberTypes.Contains(type) => BepInExValueKind.Number,
            _ => BepInExValueKind.Text,
        };
    }

    public static bool TryReadBoolean(string? text, out bool value) => bool.TryParse(text?.Trim(), out value);

    public static string FormatBoolean(bool value) => value ? "true" : "false";

    public static bool TryReadInteger(string? text, out decimal value) =>
        decimal.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    public static bool TryReadNumber(string? text, out decimal value)
    {
        if (decimal.TryParse(text?.Trim(), NumberStyle, CultureInfo.InvariantCulture, out value)) return true;
        if (!double.TryParse(text?.Trim(), NumberStyle, CultureInfo.InvariantCulture, out var wide) || !double.IsFinite(wide)) return false;
        if (Math.Abs(wide) > (double)decimal.MaxValue) return false;

        value = (decimal)wide;
        return true;
    }

    public static string FormatNumber(BepInExConfigSetting setting, decimal value) => setting.SettingType switch
    {
        "Single" => ((float)value).ToString(CultureInfo.InvariantCulture),
        "Double" => ((double)value).ToString(CultureInfo.InvariantCulture),
        _ when IsInteger(setting) => decimal.Truncate(value).ToString(CultureInfo.InvariantCulture),
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    public static bool TryGetRange(BepInExConfigSetting setting, out decimal minimum, out decimal maximum)
    {
        minimum = maximum = 0;
        if (setting.HasRange
            && TryReadNumber(setting.RangeMinimum, out minimum)
            && TryReadNumber(setting.RangeMaximum, out maximum)
            && minimum <= maximum)
        {
            return true;
        }

        if (setting.SettingType is { } type && IntegerTypes.TryGetValue(type, out var bounds))
        {
            (minimum, maximum) = bounds;
            return true;
        }

        return false;
    }

    public static decimal Clamp(BepInExConfigSetting setting, decimal value)
    {
        if (IsInteger(setting)) value = decimal.Round(value, MidpointRounding.AwayFromZero);
        return TryGetRange(setting, out var minimum, out var maximum) ? Math.Clamp(value, minimum, maximum) : value;
    }

    public static IReadOnlyList<string> ReadFlags(BepInExConfigSetting setting, string? text)
    {
        var picked = new List<string>();
        foreach (var part in (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(part, NoFlags, StringComparison.OrdinalIgnoreCase)) continue;
            var known = setting.AcceptableValues.FirstOrDefault(v => string.Equals(v, part, StringComparison.OrdinalIgnoreCase)) ?? part;
            if (!picked.Contains(known, StringComparer.OrdinalIgnoreCase)) picked.Add(known);
        }

        return picked;
    }

    public static string FormatFlags(BepInExConfigSetting setting, IEnumerable<string> picked)
    {
        var chosen = picked
            .Where(p => !string.Equals(p, NoFlags, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ordered = setting.AcceptableValues.Where(chosen.Contains)
            .Concat(chosen.Where(c => !setting.AcceptableValues.Contains(c, StringComparer.OrdinalIgnoreCase)))
            .ToList();

        if (ordered.Count > 0) return string.Join(", ", ordered);
        return setting.AcceptableValues.Contains(NoFlags, StringComparer.OrdinalIgnoreCase) ? NoFlags : "0";
    }

    public static string? MatchChoice(BepInExConfigSetting setting, string? text)
    {
        var value = text?.Trim() ?? string.Empty;
        return setting.AcceptableValues.FirstOrDefault(v => string.Equals(v, value, ChoiceComparison(setting)));
    }

    public static bool TryReadColor(string? text, out BepInExColor color)
    {
        color = default;
        var hex = (text ?? string.Empty).Trim('#', ' ');
        if (hex.Length is not (3 or 4 or 6 or 8) || !hex.All(Uri.IsHexDigit)) return false;

        if (hex.Length <= 4)
        {
            hex = string.Concat(hex.Select(c => new string(c, 2)));
        }

        var r = Convert.ToByte(hex[..2], 16);
        var g = Convert.ToByte(hex[2..4], 16);
        var b = Convert.ToByte(hex[4..6], 16);
        var a = hex.Length == 8 ? Convert.ToByte(hex[6..8], 16) : (byte)0xFF;

        color = new BepInExColor(r, g, b, a);
        return true;
    }

    public static string FormatColor(BepInExColor color) =>
        $"{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";

    public static bool SameValue(BepInExConfigSetting setting, string? left, string? right)
    {
        if (string.Equals(left?.Trim(), right?.Trim(), StringComparison.Ordinal)) return true;

        return KindOf(setting) switch
        {
            BepInExValueKind.Boolean => TryReadBoolean(left, out var a) && TryReadBoolean(right, out var b) && a == b,
            BepInExValueKind.Integer => TryReadInteger(left, out var a) && TryReadInteger(right, out var b) && a == b,
            BepInExValueKind.Number => SameNumber(setting, left, right),
            BepInExValueKind.Choice => string.Equals(left?.Trim(), right?.Trim(), ChoiceComparison(setting)),
            BepInExValueKind.Flags => ReadFlags(setting, left).ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(ReadFlags(setting, right)),
            BepInExValueKind.Color => TryReadColor(left, out var a) && TryReadColor(right, out var b) && a == b,
            BepInExValueKind.Shortcut => BepInExShortcut.Read(left).Equals(BepInExShortcut.Read(right)),
            _ => false,
        };
    }

    public static bool DiffersFromDefault(BepInExConfigSetting setting) =>
        setting.DefaultValue is { } defaultValue && !SameValue(setting, setting.Value, defaultValue);

    private static bool SameNumber(BepInExConfigSetting setting, string? left, string? right)
    {
        if (!TryReadNumber(left, out var a) || !TryReadNumber(right, out var b)) return false;

        return setting.SettingType switch
        {
            "Single" => (float)a == (float)b,
            "Double" => (double)a == (double)b,
            _ => a == b,
        };
    }

    private static bool IsInteger(BepInExConfigSetting setting) =>
        setting.SettingType is { } type && IntegerTypes.ContainsKey(type);

    private static StringComparison ChoiceComparison(BepInExConfigSetting setting) =>
        string.Equals(setting.SettingType, "String", StringComparison.Ordinal)
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
}
