using TCFModManager.App.Localization;
using TCFModManager.Core.Models;

namespace TCFModManager.App.ViewModels;

// Display wrapper for one row in ModUpdateDialogViewModel.Versions, representing one published mod version.
public sealed class ModVersionRowViewModel
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    // The full version record, used to install this version when selected.
    public required ModVersion Raw { get; init; }

    public string VersionText => Raw.Version ?? Strings.Common_Unknown;

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

    // Set for an addon version, whose requirement is its parent mod's version rather than an SPT
    // one - e.g. "Raid Review ^1.5.0". Null for an ordinary mod version.
    public string? ParentRequirement { get; init; }

    public string? CompatibilityLabel => (ParentRequirement, IsCompatible) switch
    {
        ({ } requirement, true) => Text(Strings.ModUpdate_VersionFitsFormat, requirement),
        ({ } requirement, false) => Text(Strings.ModUpdate_VersionNeedsFormat, requirement),
        ({ } requirement, null) => Text(Strings.ModUpdate_VersionNeedsUncheckedFormat, requirement),
        (null, true) => Strings.ModUpdate_VersionCompatible,
        (null, false) => Strings.ModUpdate_VersionIncompatible,
        _ => null,
    };
}
