using TCFModManager.Core.Services;

namespace TCFModManager.App;

public static class WineLayout
{
    public static double HoverPreviewPictureHeight { get; } = WineHost.IsWine ? 240 : double.NaN;
}
