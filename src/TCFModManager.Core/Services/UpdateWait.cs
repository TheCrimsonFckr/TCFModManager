namespace TCFModManager.Core.Services;

public static class UpdateWait
{
    public static readonly int[] DayChoices = [0, 1, 2, 3, 7];

    public static T? NewestOldEnough<T>(
        IEnumerable<T> versions,
        Func<T, string?> version,
        Func<T, DateTimeOffset?> published,
        string? installed,
        string upTo,
        int days,
        DateTimeOffset now) where T : class
    {
        var cutoff = now.AddDays(-days);

        return NewestFittingVersion.Newest(versions, published, version)
            .FirstOrDefault(v =>
                ModVersionComparer.Compare(version(v), upTo) is <= 0
                && (installed is null || ModVersionComparer.Compare(version(v), installed) is > 0)
                && (published(v) is not { } at || at <= cutoff));
    }

    public static bool IsWaiting(DateTimeOffset? published, int days, DateTimeOffset now) =>
        days > 0 && published is { } at && at > now.AddDays(-days);

    public static DateTimeOffset? OfferedFrom(DateTimeOffset? published, int days) =>
        published?.AddDays(days);
}
