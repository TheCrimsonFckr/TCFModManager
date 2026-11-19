namespace TCFModManager.Core.Services;

public sealed record FreeSpaceShortfall(string DriveName, long RequiredBytes, long AvailableBytes);

public static class FreeSpaceCheck
{
    public const long MarginBytes = 500L * 1024 * 1024;

    public static long Required(long downloadBytes) => downloadBytes * 3 + MarginBytes;

    public static FreeSpaceShortfall? Find(long downloadBytes, IEnumerable<string> folders, Func<string, long?>? freeOn = null)
    {
        if (downloadBytes <= 0) return null;

        freeOn ??= Available;
        var required = Required(downloadBytes);

        foreach (var root in folders.Select(Root).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (freeOn(root) is { } free && free < required) return new FreeSpaceShortfall(root, required, free);
        }

        return null;
    }

    private static string? Root(string folder)
    {
        try
        {
            return Path.GetPathRoot(Path.GetFullPath(folder));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static long? Available(string root)
    {
        try
        {
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            AppLog.Debug("Downloads", $"skipped the free-space check on {root}: {ex.Message}");
            return null;
        }
    }
}
