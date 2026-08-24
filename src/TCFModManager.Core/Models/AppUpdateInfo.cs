using TCFModManager.Core.Services;

namespace TCFModManager.Core.Models;

//
// The newest version of this app published on sp-mod.com, as compared against the running one.
// Produced by AppUpdateService; consumed by the update page and by AppUpdateInstaller.
//
public sealed record AppUpdateInfo
{
    public required string CurrentVersion { get; init; }

    public required string LatestVersion { get; init; }

    // Null when either version string couldn't be parsed - the app says so rather than guessing at
    // what kind of update it is.
    public required VersionChangeKind? ChangeKind { get; init; }

    // The sp-mod.com download link for LatestVersion - the same URL the Download button on the mod
    // page resolves to. Null means the published version has no downloadable file, in which case
    // there's nothing to install and the user is pointed at the page instead.
    public string? DownloadUrl { get; init; }

    // Live from the API where available, falling back to SelfMod.ModPageUrl.
    public required string ModPageUrl { get; init; }

    // The release's own changelog, as the rich-text HTML sp-mod.com stores it in (rendered through
    // the same HtmlText behaviour the mod update dialog uses).
    public string? Changelog { get; init; }

    public long? DownloadSizeBytes { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    // True only when the published version is genuinely newer than the running one.
    public bool IsUpdate => ChangeKind is VersionChangeKind.Patch
        or VersionChangeKind.Minor
        or VersionChangeKind.Major;

    public bool CanInstall => IsUpdate && !string.IsNullOrWhiteSpace(DownloadUrl);
}
