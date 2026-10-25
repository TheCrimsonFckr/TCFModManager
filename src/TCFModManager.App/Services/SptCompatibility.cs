using TCFModManager.App.Localization;
using TCFModManager.App.Views;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

public enum IncompatibleAnswer
{
    InstallAll,
    SkipThese,
    Cancel,
}

//
// The one question every install path asks before it fetches a version that doesn't support the
// installed SPT (Chris, 2026-10-02). It used to fetch the newest version silently when none fitted -
// PityLoot "~4.1.6" onto SPT 4.1.5, which the server then refused to load.
//
// Asked, never refused: the constraint is the author's word and can be wrong. The default never
// installs them. An unknown SPT version or an unreadable constraint is not "incompatible" and isn't asked.
//
public static class SptCompatibility
{
    public static bool IsIncompatible(string? sptVersionConstraint) =>
        SptVersionMatcher.IsSatisfiedBy(sptVersionConstraint, AppServices.SptEnvironment.InstalledVersion) == false;

    // "PityLoot 1.0.0 - needs SPT 4.1.6 or newer on 4.1", one per mod in the question.
    public static string Line(string name, string? version, string? constraint) =>
        LocalizationService.Text(
            Strings.Install_IncompatibleLineFormat,
            name,
            version,
            SptVersionRangeFormatter.Format(constraint) ?? constraint);

    //
    // Asks whether to install one version that doesn't support the installed SPT anyway. Nothing to
    // ask returns true.
    //
    public static bool ConfirmAnyway(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return true;

        var spt = AppServices.SptEnvironment.InstalledVersion;

        return AskWindow.Show(new AskRequest
        {
            Title = LocalizationService.Text(Strings.Install_NotMadeForTitleFormat, spt),
            Lead = LocalizationService.Text(Strings.Install_NotMadeForOneFormat, spt),
            Sections = [new(null, lines)],
            Detail = [Strings.Install_MayNotLoadOne],
            First = Strings.Install_InstallAnyway,
            Default = AskChoice.Cancel,
        }) == AskChoice.First;
    }

    //
    // The same question for a batch (OPEN-38 Q3): install them too, leave them out and fetch the
    // rest, or stop the whole batch. Nothing to ask installs everything.
    //
    public static IncompatibleAnswer AskBatch(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return IncompatibleAnswer.InstallAll;

        var spt = AppServices.SptEnvironment.InstalledVersion;

        return AskWindow.Show(new AskRequest
        {
            Title = LocalizationService.Text(Strings.Install_NotMadeForTitleFormat, spt),
            Lead = LocalizationService.Text(Strings.Install_NotMadeForBatchFormat, spt),
            Sections = [new(null, lines)],
            Detail = [Strings.Install_MayNotLoadMany],
            First = Strings.Install_SkipThese,
            Second = Strings.Install_InstallThemAnyway,
            Default = AskChoice.First,
        }) switch
        {
            AskChoice.First => IncompatibleAnswer.SkipThese,
            AskChoice.Second => IncompatibleAnswer.InstallAll,
            _ => IncompatibleAnswer.Cancel,
        };
    }
}
