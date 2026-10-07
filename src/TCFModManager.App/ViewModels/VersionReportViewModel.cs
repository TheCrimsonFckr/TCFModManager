using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// OPEN-23 S2: one dependency in the Version conflicts block - who asks for which version of it, and
// what (if anything) would satisfy them all. Built from DependencyVersionSolver's report; only reports
// needing attention reach the page.
//
public sealed class VersionReportViewModel : LocalizedViewModel
{
    private static string Text(string format, params object?[] values) => LocalizationService.Text(format, values);

    public required DependencyVersionReport Report { get; init; }

    public required string DependencyName { get; init; }

    public string? InstalledVersion { get; init; }

    // The catalog listing of the dependency, for Switch to.
    public Mod? CatalogMod { get; init; }

    public required IReadOnlyList<VersionRequirementRow> Rows { get; init; }

    public string Title => InstalledVersion is null
        ? Text(Strings.Dependencies_VersionTitleMissingFormat, DependencyName)
        : Text(Strings.Dependencies_VersionTitleFormat, DependencyName, InstalledVersion);

    public string Glyph => ModStatusDisplay.Glyph(Report.HasFileProblem ? ModStatus.Conflict : ModStatus.TooNew);

    public bool IsError => Report.HasFileProblem;

    public string Explanation
    {
        get
        {
            var parts = new List<string>();
            if (Report.InstallWide) parts.Add(Strings.Dependencies_VersionInstallWide);

            if (Report.Conflict) parts.Add(Strings.Dependencies_VersionConflict);
            else if (Report.FixVersion is { } fix) parts.Add(Text(Strings.Dependencies_VersionFixFormat, fix));
            else if (!Report.HasFileProblem) parts.Add(Strings.Dependencies_VersionSpModOnly);

            return string.Join(" ", parts);
        }
    }

    public bool CanSwitch => Report.FixVersion is not null && CatalogMod is not null;

    public string SwitchText => Text(Strings.Dependencies_VersionSwitchFormat, Report.FixVersion);
}

// One dependent's ask, as a row under its dependency.
public sealed partial class VersionRequirementRow : LocalizedViewModel
{
    private static string Text(string format, params object?[] values) => LocalizationService.Text(format, values);

    public required DependencyRequirement Requirement { get; init; }

    public required string DependentName { get; init; }

    public string? DependentVersion { get; init; }

    public IEnumerable<ModVersionSummary>? DependencyVersions { get; init; }

    public string DependencyName { get; init; } = "";

    public int? DependencyModId { get; init; }

    public string? DependencyInstalledVersion { get; init; }

    // S3: the dependent's own update, when one is on offer - a newer version may accept what's installed.
    public string? DependentUpdateVersion { get; init; }

    public Mod? DependentCatalogMod { get; init; }

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(CanUpdateDependent))]
    private bool _isQueued;

    private bool IsProblem => !IsOptional && Requirement.Standing is RequirementStanding.Missed or RequirementStanding.Missing;

    // R6: only sp-mod's ranges can be accepted, and only with both versions known.
    public bool CanAccept =>
        !IsOptional
        && Requirement.Source == RequirementSource.SpMod
        && Requirement.Standing == RequirementStanding.Missed
        && Requirement.Dependent.ModId is not null && DependencyModId is not null
        && DependentVersion is not null && DependencyInstalledVersion is not null;

    public bool CanUpdateDependent => IsProblem && !IsQueued && DependentUpdateVersion is not null && DependentCatalogMod is not null;

    public string UpdateDependentText => Text(Strings.Dependencies_VersionUpdateDependentFormat, DependentUpdateVersion);

    public string UpdateDependentToolTip => Text(Strings.Dependencies_VersionUpdateDependentToolTipFormat,
        DependentName, DependentUpdateVersion, DependencyName);

    public string Name => DependentVersion is null ? DependentName : string.Join(" ", DependentName, DependentVersion);

    public bool IsOptional => Requirement.IsOptional;

    public double RowOpacity => IsOptional ? 0.6 : 1.0;

    public string Glyph => ModStatusDisplay.Glyph(Requirement.Standing switch
    {
        RequirementStanding.Met => ModStatus.Installed,
        RequirementStanding.Unknown => ModStatus.Installed,
        _ when IsOptional => ModStatus.Installed,
        _ when Requirement.Source == RequirementSource.SpMod => ModStatus.TooNew,
        _ => ModStatus.Conflict,
    });

    public string Detail
    {
        get
        {
            var needs = Requirement.Range is not { Length: > 0 } range
                ? Strings.Dependencies_VersionNeedsAny
                : RangeWording.Describe(range, Requirement.Found, DependencyVersions) switch
                {
                    (var v, RangeWordingKind.UpTo) => Text(Strings.Dependencies_VersionNeedsUpToFormat, v),
                    (var v, RangeWordingKind.AtLeast) => Text(Strings.Dependencies_VersionNeedsAtLeastFormat, v),
                    var (v, _) => Text(Strings.Dependencies_VersionNeedsFormat, v),
                };

            var source = Requirement.Source == RequirementSource.Files
                ? Strings.Dependencies_VersionSourceFiles
                : Strings.Dependencies_VersionSourceSpMod;

            var text = Text(Strings.Dependencies_VersionDetailFormat, needs, source);
            return IsOptional ? Text(Strings.Dependencies_VersionOptionalFormat, text) : text;
        }
    }
}
