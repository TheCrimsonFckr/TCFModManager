using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TCFModManager.Core.Models;

[JsonConverter(typeof(ThemeColorJsonConverter))]
public readonly record struct ThemeColor(byte A, byte R, byte G, byte B)
{
    public static ThemeColor FromRgb(byte r, byte g, byte b) => new(0xFF, r, g, b);

    public bool IsOpaque => A == 0xFF;

    public static bool TryParse(string? text, out ThemeColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var hex = text.Trim();
        if (hex.StartsWith('#')) hex = hex[1..];

        if (hex.Length is not (6 or 8)) return false;
        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)) return false;

        color = hex.Length == 6
            ? FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value)
            : new ThemeColor((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    public static ThemeColor Parse(string text) =>
        TryParse(text, out var color) ? color : throw new FormatException($"Not a colour: '{text}'");

    public ThemeColor WithAlpha(byte alpha) => this with { A = alpha };

    public override string ToString() =>
        IsOpaque
            ? string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}")
            : string.Create(CultureInfo.InvariantCulture, $"#{A:X2}{R:X2}{G:X2}{B:X2}");
}

public sealed class ThemeColorJsonConverter : JsonConverter<ThemeColor>
{
    public override ThemeColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("A colour is written as a \"#RRGGBB\" or \"#AARRGGBB\" string.");

        var text = reader.GetString();
        return ThemeColor.TryParse(text, out var color) ? color : throw new JsonException($"Not a colour: '{text}'");
    }

    public override void Write(Utf8JsonWriter writer, ThemeColor value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
