using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

// One dependency in a resolved tree, with its status against the current install and
// whatever's needed to queue it. Rendered as an indented row inside its mod's expander.
public sealed partial class DependencyRow : LocalizedViewModel
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    public required string Name { get; init; }

    // Nesting level within its tree; 0 is a direct dependency of the mod.
    public int Depth { get; init; }

    public Thickness Indent => new(Depth * 24, 0, 0, 0);

    public required ModStatus Status { get; init; }

    // The version on disk, when there is one.
    public string? InstalledVersion { get; init; }

    // The newest version that satisfies both this dependency and the installed SPT version.
    // Null when the API couldn't resolve one.
    public string? RequiredVersion { get; init; }

    // The catalog listing, when the dependency matched one. Needed to queue an install.
    public Mod? CatalogMod { get; init; }

    // OPEN-23 S0: the range the dependent's own files declare that the installed version misses.
    public DeclaredVersionMiss? FilesMiss { get; init; }

    // OPEN-23 S0: the update sp-mod holds back because of the mod above, and the range sp-mod says
    // that mod accepts. Both null when sp-mod holds nothing back for this pair.
    public string? HeldVersion { get; init; }

    public string? SpModConstraint { get; init; }

    // Set once this row has been queued, so the button doesn't invite a second click.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotQueued))]
    [NotifyPropertyChangedFor(nameof(InstallToolTip))]
    private bool _isQueued;

    public bool IsNotQueued => !IsQueued;

    public string Glyph => ModStatusDisplay.Glyph(Status);

    public string StatusText => Status switch
    {
        ModStatus.Installed => InstalledVersion is null
            ? Strings.Dependencies_RowInstalled
            : Text(Strings.Dependencies_RowInstalledVersionFormat, InstalledVersion),
        ModStatus.UpdateAvailable => Text(
            Strings.Dependencies_RowNeedsUpdateFormat,
            RequiredVersion,
            InstalledVersion ?? Strings.Common_Unknown),
        ModStatus.NotInstalled => RequiredVersion is null
            ? Strings.Dependencies_RowNotInstalled
            : Text(Strings.Dependencies_RowNotInstalledNeedsFormat, RequiredVersion),
        ModStatus.NoCompatibleVersion => Strings.Dependencies_RowNoCompatible,
        ModStatus.Disabled => InstalledVersion is null
            ? Strings.Dependencies_RowDisabled
            : Text(Strings.Dependencies_RowDisabledVersionFormat, InstalledVersion),
        ModStatus.TooNew => SpModConstraint is null
            ? Text(Strings.Dependencies_RowInstalledVersionFormat, InstalledVersion)
            : Needs(SpModConstraint, InstalledVersion) switch
            {
                (var v, NeedKind.UpTo) => Text(Strings.Dependencies_RowSpModUpToFormat, v, InstalledVersion),
                var (v, _) => Text(Strings.Dependencies_RowSpModFormat, v, InstalledVersion),
            },
        _ => FilesMiss is null
            ? Text(Strings.Dependencies_RowInstalledVersionFormat, InstalledVersion)
            : Needs(FilesMiss.Range, FilesMiss.Found) switch
            {
                (var v, NeedKind.UpTo) => Text(Strings.Dependencies_RowWontLoadUpToFormat, v, FilesMiss.Found),
                (var v, NeedKind.AtLeast) => Text(Strings.Dependencies_RowWontLoadAtLeastFormat, v, FilesMiss.Found),
                var (v, _) => Text(Strings.Dependencies_RowWontLoadFormat, v, FilesMiss.Found),
            },
    };

    //
    // OPEN-23 S0: sp-mod holds back an update of this dependency because of the mod above. Shown after
    // the status in caution colour; left out when the status already says the same (TooNew).
    //
    public string? Note => HeldVersion is null || SpModConstraint is null || Status == ModStatus.TooNew
        ? null
        : Needs(SpModConstraint, HeldVersion) switch
        {
            (var v, NeedKind.UpTo) => Text(Strings.Dependencies_RowHeldBackUpToFormat, HeldVersion, v),
            var (v, _) => Text(Strings.Dependencies_RowHeldBackFormat, HeldVersion, v),
        };

    private enum NeedKind { UpTo, AtLeast, Plain }

    //
    // A range in words, never with operators (feedback: no ^ ~ >= shown): the newest published version
    // it accepts when <paramref name="against"/> is above them all ("up to 3.0.3"), the oldest when it
    // is below them all ("3.0.4 or later"), otherwise the range read out.
    //
    private (string Version, NeedKind Kind) Needs(string range, string? against)
    {
        var accepted = (CatalogMod?.Versions ?? [])
            .Select(v => v.Version)
            .Where(v => ModVersionMatcher.IsSatisfiedBy(range, v) == true)
            .Select(v => v!)
            .Order(Comparer<string>.Create((a, b) => ModVersionComparer.Compare(a, b) ?? 0))
            .ToList();

        if (accepted.Count > 0 && against is not null)
        {
            if (ModVersionComparer.Compare(against, accepted[^1]) > 0) return (accepted[^1], NeedKind.UpTo);
            if (ModVersionComparer.Compare(against, accepted[0]) < 0) return (accepted[0], NeedKind.AtLeast);
        }

        // A bare version is that version exactly here (ModVersionMatcher), not the SPT reading of it
        // the formatter gives ("3.0.3 - 3.0.x").
        if (Version.TryParse(range.Trim(), out _)) return (range.Trim(), NeedKind.Plain);

        return (SptVersionRangeFormatter.Format(range) ?? range, NeedKind.Plain);
    }

    // Whether the version sp-mod resolves would satisfy the dependent's files, so updating fixes a
    // Conflict row (a dependency below the range the files ask for).
    private bool UpdateFixesMiss =>
        Status == ModStatus.Conflict
        && FilesMiss is not null
        && ModVersionComparer.IsUpdateAvailable(InstalledVersion, RequiredVersion) == true
        && ModVersionMatcher.IsSatisfiedBy(FilesMiss.Range, RequiredVersion) == true;

    // Whether this row can be queued: something is actually missing or outdated, and there's
    // a resolved version and catalog listing to install.
    public bool CanInstall =>
        (Status is ModStatus.NotInstalled or ModStatus.UpdateAvailable || UpdateFixesMiss)
        && CatalogMod is not null
        && !string.IsNullOrWhiteSpace(RequiredVersion);

    public string InstallButtonText => Status == ModStatus.UpdateAvailable || UpdateFixesMiss
        ? Strings.Dependencies_RowUpdate
        : Strings.Dependencies_RowInstall;

    //
    // The button's tooltip. Its most useful job is explaining the DISABLED state: once a row is
    // queued the button greys out, and a greyed button with no explanation reads as broken rather
    // than as "already done". The page sets ToolTipService.ShowOnDisabled so this is actually
    // reachable then.
    //
    // Otherwise it reuses the shared mod-page gate wording, so this button says the same thing as
    // the install buttons on Browse and in the update dialog - including the warning when the gate
    // has been switched off. Read at bind time rather than bound live to the gate view model: these
    // rows are rebuilt on every refresh of the page, which is the only way to reach them after
    // changing that setting.
    //
    public string InstallToolTip => IsQueued
        ? Text(Strings.Dependencies_AlreadyQueuedFormat, Name)
        : Status == ModStatus.UpdateAvailable || UpdateFixesMiss
            ? AppServices.ModPageGate.UpdateToolTip
            : AppServices.ModPageGate.InstallToolTip;
}
