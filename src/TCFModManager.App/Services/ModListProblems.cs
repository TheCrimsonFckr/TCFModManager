using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// What the user is told when applying a mod list stops before it finishes.
//
// The Core half reports a ModListStop and the values behind it; the wording lives here beside the
// rest of this app's prose, for the same reason AppUpdateProblems and ModInstallProblems do.
//
// Every sentence here also answers "what happened to my install?", because that is the question a
// stopped apply actually raises. The applier's ordering is what makes those answers true: nothing
// is ever disabled unless every download worked, so a stop can only ever have enabled things.
//
public static class ModListProblems
{
    public static string Describe(ModListApplyResult result) => result.Stopped switch
    {
        // Worded by ModInstallProblems rather than here, so a locked install reads the same on this
        // page as it does everywhere else in the app.
        ModListStop.InstallInUse =>
            ModInstallProblems.InstallInUse(result.Running, ModInstallAction.ApplyList),

        ModListStop.FetchCancelled => Strings.ModListApply_Cancelled,

        ModListStop.FetchFailed => Strings.ModListApply_FetchFailed(result.FailedFetches),

        // The refusal already knows which operation was blocked and what is holding the install.
        ModListStop.MovesRefused when result.Refusal is { } refusal =>
            ModInstallProblems.Describe(refusal),

        _ => Strings.ModListApply_Stopped,
    };

    public static string Reason(ModListUnavailable unavailable) => unavailable.Reason switch
    {
        ModListUnavailableReason.NoAddonListing => Strings.ModList_ReasonNoAddonListing,

        ModListUnavailableReason.OrphanedAddon => Text(
            Strings.ModList_ReasonOrphanedAddonFormat,
            unavailable.ParentName ?? Text(Strings.ModList_UnnamedModFormat, unavailable.ParentId)),

        ModListUnavailableReason.NoVersions => Strings.ModList_ReasonNoVersions,
        ModListUnavailableReason.NoVersionForSpt => Text(Strings.ModList_ReasonNoVersionForSptFormat, unavailable.Version),
        ModListUnavailableReason.NoVersionForParent => Text(Strings.ModList_ReasonNoVersionForParentFormat, unavailable.Version),
        ModListUnavailableReason.VersionGone => Text(Strings.ModList_ReasonVersionGoneFormat, unavailable.Version),
        ModListUnavailableReason.SpModFailed => Text(Strings.ModList_ReasonApiFailedFormat, unavailable.Detail),
        _ => Strings.ModList_ReasonNoListing,
    };

    private static string Text(string format, params object?[] values) => LocalizationService.Text(format, values);
}
