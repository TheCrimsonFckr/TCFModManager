using System.Text.Json.Serialization;

namespace TCFModManager.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<ThemeBase>))]
public enum ThemeBase
{
    Dark,

    Light,
}

public sealed class ThemeDefinition
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public ThemeBase Base { get; set; } = ThemeBase.Dark;

    public ThemeColor? Accent { get; set; }

    public ThemeAccentShades? AccentShades { get; set; }

    public ThemeColors Colors { get; set; } = new();

    public double? CornerRadius { get; set; }

    public string? FontFamily { get; set; }

    public Dictionary<string, ThemeColor> Overrides { get; set; } = [];

    public ThemeDefinition Clone() => new()
    {
        Id = Id,
        Name = Name,
        Base = Base,
        Accent = Accent,
        AccentShades = AccentShades,
        Colors = Colors with { },
        CornerRadius = CornerRadius,
        FontFamily = FontFamily,
        Overrides = new Dictionary<string, ThemeColor>(Overrides, StringComparer.Ordinal),
    };
}

public sealed record ThemeAccentShades(ThemeColor Primary, ThemeColor Secondary, ThemeColor Tertiary);

public sealed record ThemeColors
{
    public ThemeColor? Background { get; init; }

    public ThemeColor? Page { get; init; }

    public ThemeColor? Surface { get; init; }

    public ThemeColor? SurfaceSecondary { get; init; }

    public ThemeColor? Flyout { get; init; }

    public ThemeColor? TextPrimary { get; init; }

    public ThemeColor? TextSecondary { get; init; }

    public ThemeColor? TextTertiary { get; init; }

    public ThemeColor? TextDisabled { get; init; }

    public ThemeColor? Stroke { get; init; }

    public ThemeColor? Divider { get; init; }

    public ThemeColor? Success { get; init; }

    public ThemeColor? Caution { get; init; }

    public ThemeColor? Critical { get; init; }

    public ThemeColor? Attention { get; init; }

    public ThemeColor? FootprintCpu { get; init; }

    public ThemeColor? FootprintGpu { get; init; }

    public ThemeColor? FootprintMemory { get; init; }

    public ThemeColor? FootprintStartup { get; init; }

    public ThemeColor? FootprintDisk { get; init; }

    public ThemeColor? FootprintNeutral { get; init; }
}
