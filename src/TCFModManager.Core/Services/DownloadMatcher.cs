using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Whether a downloaded archive now looks installed by hand.
//
// Two questions, cheapest first. Is any folder it would place in the scan? If not, nothing has
// happened yet and nothing is said. If so, is every file it would place on disk at the size the
// archive says? Presence and size only - no hashing - which is enough to tell "installed" from
// "the folder is there but it's still the old version", and costs one stat per file.
//
public static class DownloadMatcher
{
    public static DownloadMatch Check(
        DownloadedModRecord download, string installPath, IEnumerable<string> scannedFolders)
    {
        if (download.Unrecognised || download.ExpectedFolders.Count == 0 || download.ExpectedFiles.Count == 0)
            return DownloadMatch.Absent;

        var present = scannedFolders.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!download.ExpectedFolders.Any(present.Contains)) return DownloadMatch.Absent;

        var missing = 0;
        var differing = 0;

        foreach (var file in download.ExpectedFiles)
        {
            var info = new FileInfo(Path.Combine(installPath, file.Path.Replace('/', Path.DirectorySeparatorChar)));

            if (!info.Exists) missing++;
            else if (file.Size >= 0 && info.Length != file.Size) differing++;
        }

        return missing == 0 && differing == 0
            ? DownloadMatch.Installed
            : new DownloadMatch(DownloadMatchKind.Partial, missing, differing);
    }
}

public enum DownloadMatchKind
{
    // None of its folders are in the scan.
    Absent,

    // A folder is there, but some files are missing or a different size.
    Partial,

    // Every file it would place is on disk at the expected size.
    Installed,
}

public sealed record DownloadMatch(DownloadMatchKind Kind, int MissingFiles = 0, int DifferingFiles = 0)
{
    public static DownloadMatch Absent { get; } = new(DownloadMatchKind.Absent);

    public static DownloadMatch Installed { get; } = new(DownloadMatchKind.Installed);
}
