using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App;

internal static class BuiltInThemes
{
    public const string TarkovId = "tarkov";
    public const string OledId = "oled";
    public const string HighContrastId = "high-contrast";
    public const string SoftId = "soft";
    public const string SteamId = "steam";

    public static IReadOnlyList<ThemeDefinition> All { get; } =
    [
        new()
        {
            Id = TarkovId,
            Base = ThemeBase.Dark,
            Accent = C("#B4A46B"),
            Colors = new ThemeColors
            {
                Background = C("#12120F"),
                Page = C("#4C2A2820"),
                Surface = C("#0FE8DFC0"),
                SurfaceSecondary = C("#08E8DFC0"),
                Flyout = C("#1E1D19"),
                TextPrimary = C("#D9D4C3"),
                TextSecondary = C("#A39E8B"),
                TextTertiary = C("#77735F"),
                Stroke = C("#1AD9CFA8"),
                Divider = C("#1FD9CFA8"),
                Success = C("#8BAA55"),
                Caution = C("#D6A845"),
                Critical = C("#C8604F"),
            },
        },
        new()
        {
            Id = OledId,
            Base = ThemeBase.Dark,
            Colors = new ThemeColors
            {
                Background = C("#000000"),
                Page = C("#4C000000"),
                Flyout = C("#0B0B0B"),
                TextSecondary = C("#C8FFFFFF"),
                TextTertiary = C("#8AFFFFFF"),
            },
        },
        new()
        {
            Id = HighContrastId,
            Base = ThemeBase.Dark,
            Accent = C("#FFD400"),
            Colors = new ThemeColors
            {
                Background = C("#000000"),
                Page = C("#FF000000"),
                Surface = C("#14FFFFFF"),
                Flyout = C("#101010"),
                TextSecondary = C("#F0FFFFFF"),
                TextTertiary = C("#D0FFFFFF"),
                TextDisabled = C("#90FFFFFF"),
                Stroke = C("#80FFFFFF"),
                Divider = C("#80FFFFFF"),
            },
            Overrides = new()
            {
                ["ControlStrokeColorSecondary"] = C("#A0FFFFFF"),
                ["CardStrokeColorDefault"] = C("#80FFFFFF"),
            },
        },
        new()
        {
            Id = SoftId,
            Base = ThemeBase.Dark,
            Accent = C("#7AA2D6"),
            Colors = new ThemeColors
            {
                Background = C("#2A2C30"),
                Flyout = C("#33363B"),
                TextPrimary = C("#DADBDD"),
                TextSecondary = C("#A9ABAF"),
                TextTertiary = C("#7F8186"),
            },
        },
        new()
        {
            Id = SteamId,
            Base = ThemeBase.Dark,
            Accent = C("#1A9FFF"),
            AccentShades = new ThemeAccentShades(C("#1A9FFF"), C("#1A9FFF"), C("#66C0F4")),
            Colors = new ThemeColors
            {
                Background = C("#171D25"),
                Page = C("#FF0F1924"),
                Surface = C("#FF1A3045"),
                SurfaceSecondary = C("#FF162332"),
                Flyout = C("#182433"),
                TextPrimary = C("#F1F2F4"),
                TextSecondary = C("#C7D5E0"),
                TextTertiary = C("#8F9FAF"),
                Stroke = C("#1A89ACD1"),
                Divider = C("#2689ACD1"),
                Success = C("#75B022"),
                Caution = C("#E5B143"),
                Critical = C("#E06A65"),
            },
        },
    ];

    public static ThemeDefinition? Find(string? id) =>
        id is null ? null : All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    private static ThemeColor C(string hex) => ThemeColor.Parse(hex);
}

internal static class ThemeCatalog
{
    public static ThemeDefinition? Resolve(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        return BuiltInThemes.Find(id)?.Clone()
            ?? (ThemeStore.IsCustomId(id) ? new ThemeStore().Find(id) : null);
    }
}
