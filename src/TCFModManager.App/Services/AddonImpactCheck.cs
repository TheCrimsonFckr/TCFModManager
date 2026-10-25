using TCFModManager.App.Localization;
using TCFModManager.App.ViewModels;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.Services;

public sealed record InstalledAddon(int AddonId, string Name, string? InstalledVersion, Addon? Listing, bool Overridden = false);

public sealed record AddonImpactItem(InstalledAddon Addon, int ModId, string ModName, string ModVersion, AddonImpactResult Result)
{
    public bool IsAffected => Result.Verdict is AddonImpactVerdict.NeedsChange or AddonImpactVerdict.NoCompatibleVersion or AddonImpactVerdict.Overridden;
}

public enum AddonImpactAnswer
{
    UpdateBoth,
    ModOnly,
    Cancel,
}

//
// OPEN-32 D4-D8: what moving a mod to another version does to its installed addons, and the one
// question asked before it happens. Disabled addons are left out - nothing is loading them.
//
public sealed class AddonImpactCheck
{
    private const int PageSize = 50;

    private readonly Dictionary<int, IReadOnlyList<AddonVersion>> _published = [];

    public static List<InstalledAddon> From(IEnumerable<InstalledModCardViewModel> addonCards) =>
    [
        .. addonCards
            .Where(c => c is { IsAddon: true, IsDisabled: false, ModId: not null })
            .Select(c => new InstalledAddon(
                c.ModId!.Value, c.DisplayTitle, c.InstalledVersion, AppServices.Addons.ById(c.ModId.Value), c.IsFitOverridden)),
    ];

    public async Task<List<AddonImpactItem>> AssessAsync(
        IEnumerable<InstalledAddon> addons, int modId, string modName, string modVersion, CancellationToken ct = default)
    {
        var spt = AppServices.SptEnvironment.InstalledVersion;
        var items = new List<AddonImpactItem>();

        foreach (var addon in addons)
        {
            var result = await AddonImpact.AssessAsync(
                addon.AddonId, addon.Listing, addon.InstalledVersion, modVersion, spt, PublishedAsync, ct, addon.Overridden);
            items.Add(new AddonImpactItem(addon, modId, modName, modVersion, result));
        }

        return items;
    }

    private async Task<IReadOnlyList<AddonVersion>> PublishedAsync(int addonId, CancellationToken ct)
    {
        if (_published.TryGetValue(addonId, out var known)) return known;

        var all = new List<AddonVersion>();
        for (var page = 1; ; page++)
        {
            var result = await AppServices.SpModApi.GetAddonVersionsAsync(
                addonId.ToString(),
                new AddonVersionsQuery { Sort = "-published_at", Page = page, PerPage = PageSize },
                ct);

            all.AddRange(result.Data);

            if (result.Data.Count < PageSize || (result.Meta is { LastPage: > 0 } meta && page >= meta.LastPage)) break;
        }

        _published[addonId] = all;
        return all;
    }

    //
    // D6/D7: asked once, only when something is affected. Any addon with a version to change to
    // gives Update both / Mod only / Cancel; only addons with no fitting version give Update anyway /
    // Cancel, Cancel by default. Nothing affected answers UpdateBoth without asking.
    // willInstall: the update installs rather than only downloads, so addons with no fitting version
    // are disabled once it lands (D11) and the window says so.
    //
    public static AddonImpactAnswer Ask(IReadOnlyList<AddonImpactItem> items, bool willInstall)
    {
        var change = items.Where(i => i.Result.Verdict == AddonImpactVerdict.NeedsChange).ToList();
        var none = items.Where(i => i.Result.Verdict == AddonImpactVerdict.NoCompatibleVersion).ToList();

        if (change.Count == 0 && none.Count == 0) return AddonImpactAnswer.UpdateBoth;

        var sections = new List<AskSection>();
        if (change.Count > 0) sections.Add(new(Strings.AddonImpact_ChangeHeading, [.. change.Select(ChangeLine)]));
        if (none.Count > 0) sections.Add(new(Strings.AddonImpact_NoVersionHeading, [.. none.Select(NoVersionLine)]));

        string[] detail = none.Count == 0
            ? []
            : [willInstall ? Strings.AddonImpact_NoVersionDisabled : Strings.AddonImpact_NoVersionStays];

        if (change.Count > 0)
        {
            return AskWindow.Show(new AskRequest
                {
                    Title = Strings.AddonImpact_NeedChangeTitle,
                    Lead = Strings.AddonImpact_Lead,
                    Sections = sections,
                    Detail = detail,
                    First = Strings.AddonImpact_UpdateBoth,
                    Second = Strings.AddonImpact_ModOnly,
                    Default = AskChoice.First,
                }) switch
                {
                    AskChoice.First => AddonImpactAnswer.UpdateBoth,
                    AskChoice.Second => AddonImpactAnswer.ModOnly,
                    _ => AddonImpactAnswer.Cancel,
                };
        }

        return AskWindow.Show(new AskRequest
            {
                Title = Strings.AddonImpact_MayStopTitle,
                Lead = Strings.AddonImpact_Lead,
                Sections = sections,
                Detail = detail,
                First = Strings.AddonImpact_UpdateAnyway,
                Default = AskChoice.Cancel,
            }) == AskChoice.First
            ? AddonImpactAnswer.ModOnly
            : AddonImpactAnswer.Cancel;
    }

    //
    // OPEN-37 D4: addons installed anyway that no version fits the new one either are asked about on
    // their own, after Ask - Keep (the default, and what closing the window means) or disable them
    // once the update has installed. True means disable. Nothing is disabled when the update only
    // downloads, so nothing is asked.
    //
    public static bool AskOverridden(IReadOnlyList<AddonImpactItem> items, bool willInstall)
    {
        var overridden = items.Where(i => i.Result.Verdict == AddonImpactVerdict.Overridden).ToList();
        if (!willInstall || overridden.Count == 0) return false;

        return AskWindow.Show(new AskRequest
        {
            Title = Strings.AddonImpact_OverriddenTitle,
            Lead = Strings.AddonImpact_OverriddenLead,
            Sections = [new(Strings.AddonImpact_OverriddenHeading, [.. overridden.Select(NoVersionLine)])],
            First = Strings.AddonImpact_KeepEnabled,
            Second = Strings.AddonImpact_DisableAfter,
            Cancel = null,
            Default = AskChoice.First,
        }) == AskChoice.Second;
    }

    // What Update both queues for one addon: the version it changes to, ready for the download queue.
    public static ModVersion? Download(AddonImpactItem item) =>
        item.Result is { Verdict: AddonImpactVerdict.NeedsChange, Target: { Link: not null } target }
            ? new ModVersion
            {
                Id = target.Id,
                Version = target.Version,
                Description = target.Description,
                Link = target.Link,
                ContentLength = target.ContentLength,
            }
            : null;

    public static string Verdict(AddonImpactResult result) => result switch
    {
        { Verdict: AddonImpactVerdict.Unaffected } => Strings.AddonImpact_Fits,
        { Verdict: AddonImpactVerdict.NeedsChange, Target.Version: var target } => Text(Strings.AddonImpact_ChangeFormat, target),
        { Verdict: AddonImpactVerdict.NoCompatibleVersion } => Strings.AddonImpact_NoVersion,
        { Verdict: AddonImpactVerdict.Overridden } => Strings.AddonImpact_Overridden,
        _ => result.UnknownReason switch
        {
            AddonImpactUnknownReason.Detached => Strings.AddonImpact_UnknownDetached,
            AddonImpactUnknownReason.InstalledVersionUnknown => Strings.AddonImpact_UnknownInstalledVersion,
            AddonImpactUnknownReason.InstalledVersionNotPublished => Strings.AddonImpact_UnknownNotPublished,
            AddonImpactUnknownReason.UnreadableRequirement => Strings.AddonImpact_UnknownRequirement,
            AddonImpactUnknownReason.LookupFailed => Strings.AddonImpact_UnknownLookupFailed,
            _ => Strings.AddonImpact_UnknownNotListed,
        },
    };

    private static string ChangeLine(AddonImpactItem i) =>
        Text(Strings.AddonImpact_ChangeLineFormat, i.Addon.Name, i.Addon.InstalledVersion, i.Result.Target?.Version, i.ModName, i.ModVersion);

    private static string NoVersionLine(AddonImpactItem i) =>
        Text(Strings.AddonImpact_NoVersionLineFormat, i.Addon.Name, i.Addon.InstalledVersion, i.ModName, i.ModVersion);

    private static string Text(string format, params object?[] values) => LocalizationService.Text(format, values);
}
