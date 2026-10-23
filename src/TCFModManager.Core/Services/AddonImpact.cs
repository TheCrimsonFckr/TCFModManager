using TCFModManager.Core.Models;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.Core.Services;

public enum AddonImpactVerdict
{
    Unaffected,
    NeedsChange,
    NoCompatibleVersion,
    Unknown,
}

public enum AddonImpactUnknownReason
{
    None,
    NotListed,
    Detached,
    InstalledVersionUnknown,
    InstalledVersionNotPublished,
    UnreadableRequirement,
    LookupFailed,
}

//
// What moving a mod to another version does to one of its installed addons (OPEN-32 D4). Target is
// the version to change to for NeedsChange; TargetIsOlder when that is a step down from the installed one.
//
public sealed record AddonImpactResult(
    int AddonId,
    string? InstalledVersion,
    AddonImpactVerdict Verdict,
    AddonVersionSummary? Target = null,
    bool TargetIsOlder = false,
    AddonImpactUnknownReason UnknownReason = AddonImpactUnknownReason.None);

public delegate Task<IReadOnlyList<AddonVersion>> AddonVersionsLookup(int addonId, CancellationToken ct);

//
// Judges an installed addon against the version its mod is about to be. The cached listing is tried
// first; when it can't settle the answer (the installed version isn't among its latest versions, or
// none of them fits) every published version is looked up through lookup.
//
public static class AddonImpact
{
    public static async Task<AddonImpactResult> AssessAsync(
        int addonId,
        Addon? listing,
        string? installedVersion,
        string targetModVersion,
        string? sptVersion,
        AddonVersionsLookup lookup,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetModVersion);

        if (listing is null) return Unknown(addonId, installedVersion, AddonImpactUnknownReason.NotListed);
        if (listing.IsDetached == true) return Unknown(addonId, installedVersion, AddonImpactUnknownReason.Detached);
        if (string.IsNullOrWhiteSpace(installedVersion))
            return Unknown(addonId, installedVersion, AddonImpactUnknownReason.InstalledVersionUnknown);

        if (Judge(addonId, installedVersion, listing.Versions ?? [], targetModVersion, sptVersion, complete: false) is { } cached)
            return cached;

        IReadOnlyList<AddonVersion> published;
        try
        {
            published = await lookup(addonId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SpModApiException or HttpRequestException
            || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            AppLog.Warn("AddonImpact", $"addon {addonId}: version lookup failed: {ex.Message}");
            return Unknown(addonId, installedVersion, AddonImpactUnknownReason.LookupFailed);
        }

        return Judge(addonId, installedVersion, [.. published.Select(Summary)], targetModVersion, sptVersion, complete: true)!;
    }

    private static AddonImpactResult? Judge(
        int addonId,
        string installedVersion,
        IReadOnlyList<AddonVersionSummary> versions,
        string targetModVersion,
        string? sptVersion,
        bool complete)
    {
        var installed = versions.FirstOrDefault(v => SameVersion(v.Version, installedVersion));
        if (installed is null)
            return complete ? Unknown(addonId, installedVersion, AddonImpactUnknownReason.InstalledVersionNotPublished) : null;

        switch (AddonFit.Check(installed.ModVersionConstraint, targetModVersion, sptVersion))
        {
            case true:
                return new(addonId, installedVersion, AddonImpactVerdict.Unaffected);
            case null:
                return Unknown(addonId, installedVersion, AddonImpactUnknownReason.UnreadableRequirement);
        }

        var fitting = NewestFittingVersion.Newest(versions, v => v.PublishedAt, v => v.Version)
            .FirstOrDefault(v => AddonFit.Accepts(v.ModVersionConstraint, targetModVersion, sptVersion));

        if (fitting is not null)
            return new(
                addonId,
                installedVersion,
                AddonImpactVerdict.NeedsChange,
                fitting,
                TargetIsOlder: ModVersionComparer.Compare(fitting.Version, installedVersion) < 0);

        return complete ? new(addonId, installedVersion, AddonImpactVerdict.NoCompatibleVersion) : null;
    }

    private static bool SameVersion(string? a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase) || ModVersionComparer.Compare(a, b) == 0;

    private static AddonImpactResult Unknown(int addonId, string? installedVersion, AddonImpactUnknownReason reason) =>
        new(addonId, installedVersion, AddonImpactVerdict.Unknown, UnknownReason: reason);

    private static AddonVersionSummary Summary(AddonVersion v) => new()
    {
        Id = v.Id,
        Version = v.Version,
        ModVersionConstraint = v.ModVersionConstraint,
        Downloads = v.Downloads,
        Description = v.Description,
        Link = v.Link,
        ContentLength = v.ContentLength,
        PublishedAt = v.PublishedAt,
    };
}
