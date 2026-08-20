using TCFModManagement.Core.Models;

namespace TCFModManagement.App.ViewModels;

// Display wrapper for one row in ModUpdateDialogViewModel.Versions, representing one published mod version.
public sealed class ModVersionRowViewModel
{
    // The full version record, used to install this version when selected.
    public required ModVersion Raw { get; init; }

    public string VersionText => Raw.Version ?? "unknown";

    // Per-version release notes as raw HTML; null when there are none.
    public string? Changelog => string.IsNullOrWhiteSpace(Raw.Description) ? null : Raw.Description;

    public DateTimeOffset? PublishedAt => Raw.PublishedAt;

    public string? SptVersionConstraint => Raw.SptVersionConstraint;

    // True for the row matching the version already installed on disk.
    public required bool IsInstalled { get; init; }

    // True only for the single newest published row.
    public bool IsLatest { get; set; }

    // Null when compatibility can't be determined.
    public required bool? IsCompatible { get; init; }

    public string? CompatibilityLabel => IsCompatible switch
    {
        true => "Compatible with your installed SPT version",
        false => "Not compatible with your installed SPT version",
        null => null,
    };
}
