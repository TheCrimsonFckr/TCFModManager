using System.Windows;
using TCFModManager.App.Localization;
using TCFModManager.App.ViewModels;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.Services;

public sealed record InstalledAddon(int AddonId, string Name, string? InstalledVersion, Addon? Listing);

public sealed record AddonImpactItem(InstalledAddon Addon, int ModId, string ModName, string ModVersion, AddonImpactResult Result)
{
    public bool IsAffected => Result.Verdict is AddonImpactVerdict.NeedsChange or AddonImpactVerdict.NoCompatibleVersion;
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
            .Select(c => new InstalledAddon(c.ModId!.Value, c.DisplayTitle, c.InstalledVersion, AppServices.Addons.ById(c.ModId.Value))),
    ];

    public async Task<List<AddonImpactItem>> AssessAsync(
        IEnumerable<InstalledAddon> addons, int modId, string modName, string modVersion, CancellationToken ct = default)
    {
        var spt = AppServices.SptEnvironment.InstalledVersion;
        var items = new List<AddonImpactItem>();

        foreach (var addon in addons)
        {
            var result = await AddonImpact.AssessAsync(
                addon.AddonId, addon.Listing, addon.InstalledVersion, modVersion, spt, PublishedAsync, ct);
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
    // gives Update both / Mod only / Cancel (Yes / No / Cancel); only addons with no fitting version
    // give Update anyway / Cancel, No by default. Nothing affected answers UpdateBoth without asking.
    // willInstall: the update installs rather than only downloads, so addons with no fitting version
    // are disabled once it lands (D11) and the box says so.
    //
    public static AddonImpactAnswer Ask(IReadOnlyList<AddonImpactItem> items, bool willInstall)
    {
        var change = items.Where(i => i.Result.Verdict == AddonImpactVerdict.NeedsChange).ToList();
        var none = items.Where(i => i.Result.Verdict == AddonImpactVerdict.NoCompatibleVersion).ToList();

        if (change.Count == 0 && none.Count == 0) return AddonImpactAnswer.UpdateBoth;

        var parts = new List<string>();
        if (change.Count > 0)
            parts.Add(Strings.AddonImpact_ChangeList + "\n" + string.Join("\n", change.Select(ChangeLine)));
        if (none.Count > 0)
            parts.Add((willInstall ? Strings.AddonImpact_NoVersionDisableList : Strings.AddonImpact_NoVersionList)
                + "\n" + string.Join("\n", none.Select(NoVersionLine)));

        if (change.Count > 0)
        {
            parts.Add(Strings.AddonImpact_ChangeQuestion);

            return MessageBox.Show(
                    string.Join("\n\n", parts),
                    Strings.AddonImpact_Title,
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question,
                    MessageBoxResult.Yes) switch
                {
                    MessageBoxResult.Yes => AddonImpactAnswer.UpdateBoth,
                    MessageBoxResult.No => AddonImpactAnswer.ModOnly,
                    _ => AddonImpactAnswer.Cancel,
                };
        }

        parts.Add(Strings.AddonImpact_NoVersionQuestion);

        return MessageBox.Show(
                string.Join("\n\n", parts),
                Strings.AddonImpact_Title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) == MessageBoxResult.Yes
            ? AddonImpactAnswer.ModOnly
            : AddonImpactAnswer.Cancel;
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
