namespace TCFModManager.Core.Models;

// One unfinished download queue item kept across a restart. DependencyOf and InstallsBefore are
// indexes into the same saved list.
public sealed record RememberedDownload
{
    public required InstallTarget Target { get; init; }

    public required string VersionLabel { get; init; }

    public int? VersionId { get; init; }

    public required string InstallPath { get; init; }

    public bool CheckDependencies { get; init; }

    // Past the questions asked before downloading, so they are not asked again.
    public bool Asked { get; init; }

    public bool DownloadOnly { get; init; }

    public string? DownloadSubfolder { get; init; }

    public long? TotalBytes { get; init; }

    // Installs: the queue archive (its ".part" while incomplete). Monitor mode: the part file in the download folder.
    public string? ArchivePath { get; init; }

    public int? DependencyOf { get; init; }

    public int? InstallsBefore { get; init; }

    public List<int> UpdatesWith { get; init; } = [];
}
