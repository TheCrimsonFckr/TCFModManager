using System.Windows;
using TCFModManager.Core.Models;

namespace TCFModManager.App.Behaviors;

public static class CardDensity
{
    public const string TitleFontSizeKey = "CardTitleFontSize";
    public const string LineFontSizeKey = "CardLineFontSize";
    public const string ChipFontSizeKey = "CardChipFontSize";
    public const string LineMarginKey = "CardLineMargin";
    public const string ChipRowMarginKey = "CardChipRowMargin";
    public const string SmallPictureSizeKey = "CardSmallPictureSize";
    public const string BannerHeightKey = "CardBannerHeight";

    public static (double Title, double Line, double Chip) FontSizes(CardSize size) => size switch
    {
        CardSize.Compact => (13, 10.5, 10),
        CardSize.Roomy => (16, 12.5, 12),
        _ => (14, 11, 11),
    };

    public static double LineGap(CardSize size) => size switch
    {
        CardSize.Compact => 0,
        CardSize.Roomy => 3,
        _ => 0,
    };

    public static void Apply(ResourceDictionary resources, CardSize size)
    {
        var (title, line, chip) = FontSizes(size);
        var gap = LineGap(size);

        resources[TitleFontSizeKey] = title;
        resources[LineFontSizeKey] = line;
        resources[ChipFontSizeKey] = chip;
        resources[LineMarginKey] = new Thickness(0, gap, 0, 0);
        resources[ChipRowMarginKey] = new Thickness(0, 4 + gap, 0, 0);
        resources[SmallPictureSizeKey] = size switch
        {
            CardSize.Compact => 36.0,
            CardSize.Roomy => 64.0,
            _ => 48.0,
        };
        resources[BannerHeightKey] = size switch
        {
            CardSize.Compact => 64.0,
            CardSize.Roomy => 120.0,
            _ => 90.0,
        };
    }
}
