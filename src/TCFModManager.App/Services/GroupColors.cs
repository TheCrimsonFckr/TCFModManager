using System.Windows.Media;
using TCFModManager.App.Localization;

namespace TCFModManager.App.Services;

public sealed record GroupColorDefinition(string Key, string NameKey, Color Color)
{
    public string Name => LocalizationService.Get(NameKey);
}

// OPEN-59: the curated group colours. Chips and headers take a tint, so text keeps the theme's own colour.
public static class GroupColors
{
    private const byte SolidAlpha = 0xFF;
    private const byte ChipAlpha = 0x4D;
    private const byte HeaderAlpha = 0x26;

    public static IReadOnlyList<GroupColorDefinition> All { get; } =
    [
        new("red", nameof(Strings.GroupColor_Red), Color.FromRgb(0xE5, 0x48, 0x4D)),
        new("orange", nameof(Strings.GroupColor_Orange), Color.FromRgb(0xF7, 0x6B, 0x15)),
        new("yellow", nameof(Strings.GroupColor_Yellow), Color.FromRgb(0xE8, 0xB0, 0x0C)),
        new("green", nameof(Strings.GroupColor_Green), Color.FromRgb(0x30, 0xA4, 0x6C)),
        new("teal", nameof(Strings.GroupColor_Teal), Color.FromRgb(0x12, 0xA5, 0x94)),
        new("blue", nameof(Strings.GroupColor_Blue), Color.FromRgb(0x00, 0x90, 0xFF)),
        new("indigo", nameof(Strings.GroupColor_Indigo), Color.FromRgb(0x3E, 0x63, 0xDD)),
        new("purple", nameof(Strings.GroupColor_Purple), Color.FromRgb(0x8E, 0x4E, 0xC6)),
        new("pink", nameof(Strings.GroupColor_Pink), Color.FromRgb(0xD6, 0x40, 0x9F)),
        new("brown", nameof(Strings.GroupColor_Brown), Color.FromRgb(0xAD, 0x7F, 0x58)),
        new("grey", nameof(Strings.GroupColor_Grey), Color.FromRgb(0x8B, 0x8D, 0x98)),
    ];

    private static readonly Dictionary<(string Key, byte Alpha), Brush> Cache = [];

    public static GroupColorDefinition? Find(string? key) =>
        key is null ? null : All.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));

    public static Brush? Solid(string? key) => BrushFor(key, SolidAlpha);

    public static Brush? Chip(string? key) => BrushFor(key, ChipAlpha);

    public static Brush? Header(string? key) => BrushFor(key, HeaderAlpha);

    private static Brush? BrushFor(string? key, byte alpha)
    {
        if (Find(key) is not { } colour) return null;
        if (Cache.TryGetValue((colour.Key, alpha), out var cached)) return cached;

        var brush = new SolidColorBrush(Color.FromArgb(alpha, colour.Color.R, colour.Color.G, colour.Color.B));
        brush.Freeze();
        Cache[(colour.Key, alpha)] = brush;
        return brush;
    }
}
