using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

// Display wrapper for the Installed page's card grid, merging a mod's client and server entries and matching it against the sp-mod.com catalog.
public sealed partial class InstalledModCardViewModel : ObservableObject
{
    public required string Name { get; init; }

    // The version to show as "installed". The install manifest's version when this app
    // installed the mod, since that is what was actually fetched; otherwise whatever the files on
    // disk report. See InstalledVersionDetail for the file-reported versions when they differ.
    public string? InstalledVersion { get; init; }

    // The versions the files themselves report, shown only when they disagree with each
    // other or with the version recorded at install time. Null when there is nothing to explain.
    public string? InstalledVersionDetail { get; init; }

    // Earliest InstalledAt across the merged entries.
    public DateTimeOffset? InstalledAt { get; init; }

    public bool HasClient { get; init; }
    public bool HasServer { get; init; }

    public string TargetSummary => (HasClient, HasServer) switch
    {
        (true, true) => "Client + Server",
        (true, false) => "Client only",
        (false, true) => "Server only",
        _ => "Unknown",
    };

    // The latest version published on sp-mod.com for whatever mod matched this one. Null when
    // the catalog hasn't loaded yet or nothing matched.
    public string? LatestPublishedVersion { get; init; }

    // The matched sp-mod.com listing's UpdatedAt. Null under the same conditions as LatestPublishedVersion.
    public DateTimeOffset? LatestUpdatedAt { get; init; }

    // The matched sp-mod.com listing's actual display Name, often different from the installed
    // folder/package Name. Null when nothing matched.
    public string? MatchedModName { get; init; }

    // The title the card leads with: the real sp-mod.com display name when there is one,
    // otherwise the folder/package Name.
    public string DisplayTitle => MatchedModName ?? Name;

    // The raw installed folder/package name, shown as a secondary line under DisplayTitle only
    // when there's a catalog match whose name differs from the folder name.
    public string? FolderNameIfDifferent =>
        MatchedModName is not null && !string.Equals(MatchedModName, Name, StringComparison.OrdinalIgnoreCase)
            ? Name
            : null;

    // The matched sp-mod.com listing's GUID. Null when nothing matched.
    public string? Guid { get; init; }

    // The matched sp-mod.com listing's primary author. Null when nothing matched. Backs the
    // "@author" search syntax in InstalledViewModel's search box.
    public string? Author { get; init; }

    // True when the matched catalog listing is flagged Fika compatible.
    public bool IsFikaCompatible { get; init; }

    // True when the matched catalog listing is flagged as containing ads.
    public bool ContainsAds { get; init; }

    // True when the matched catalog listing is flagged as containing AI content.
    public bool ContainsAiContent { get; init; }

    // True when a newer, SPT-compatible version than InstalledVersion is published. False when
    // there's no newer or no compatible newer version. Null when it can't be determined (no catalog match,
    // unparsable version, or no installed SPT version detected).
    public bool? UpdateAvailable { get; init; }

    // Client half's folder (or, for a loose top-level DLL, the DLL itself). Null when there's no client half.
    public string? ClientFolderPath { get; init; }

    // Server half's folder. Null when there's no server half.
    public string? ServerFolderPath { get; init; }

    // The client/server halves' folder (or loose-DLL) *names*, as InstalledModScanner reports them -
    // what a manual version override records as this mod's Folders, so a later rescan still ties the
    // override back to this card. Null when there's no corresponding half.
    public string? ClientFolderName { get; init; }
    public string? ServerFolderName { get; init; }

    // A single path for this card for existing callers/diagnostics; client wins when both exist.
    public required string FolderPath { get; init; }

    // The matched sp-mod.com listing's numeric id. Null when nothing matched.
    public int? ModId { get; init; }

    // True when this mod has an install record this app itself wrote, meaning Remove can delete
    // exactly those files. False for anything installed by hand or from outside the app, including a
    // mod with only a manually-confirmed version (see IsManualOverride).
    public bool IsAppManaged { get; init; }

    // True when InstalledVersion comes from the user manually confirming/overriding it (Installed
    // page's "manage version" controls) rather than from an app install or the files on disk.
    public bool IsManualOverride { get; init; }

    //
    // Every scan entry merged into this card: both halves of a client+server mod, and both copies
    // of a mod left in a container and in that container's ".disabled" sibling. What the disable
    // commands actually move, and what ModDependencyGraph is keyed on.
    //
    public IReadOnlyList<InstalledMod> Entries { get; init; } = [];

    // True when every one of this mod's folders sits under a ".disabled" container, so SPT loads none of it.
    public bool IsDisabled => Entries.Count > 0 && Entries.All(e => e.IsDisabled);

    // True when some of this mod's folders are disabled and some aren't - one half of a
    // client+server mod parked on its own, or a half-completed move leaving copies in both places.
    public bool IsMixedState => Entries.Any(e => e.IsDisabled) && Entries.Any(e => !e.IsDisabled);

    // The same folder sitting in both a container and its ".disabled" sibling, which the disable
    // toggle can't settle on its own - one of the two copies has to be set aside first.
    public IReadOnlyList<ModDuplicatePair> DuplicateFolders => ModDisableService.DuplicatePairs(Entries);

    public bool HasDuplicateFolders => DuplicateFolders.Count > 0;

    // Dims the whole card (and the group-view row) while disabled.
    public double CardOpacity => IsDisabled ? 0.45 : 1.0;

    // Ticked in the flat grid's multi-select mode. Cards are rebuilt on every scan, so a selection
    // doesn't survive one.
    [ObservableProperty]
    private bool _isSelected;

    public string DisableToggleGlyph => IsDisabled ? "PlugConnected24" : "PlugDisconnected24";

    public string DisableToggleTooltip => IsDisabled
        ? "Enable - move it back where SPT loads it from"
        : "Disable - move it to a .disabled folder, deleting nothing";

    // This mod's status, using the same vocabulary and icons as the Browse and Dependencies pages.
    // Everything here is installed by definition, so it's only ever disabled, up-to-date or outdated.
    public ModStatus Status => IsDisabled
        ? ModStatus.Disabled
        : UpdateAvailable switch
        {
            true => ModStatus.UpdateAvailable,
            false => ModStatus.Installed,
            _ => ModStatus.Unknown,
        };

    // A mixed state is neither cleanly enabled nor cleanly disabled, so it gets the conflict icon
    // rather than either side's.
    public string StatusGlyph => IsMixedState ? "ErrorCircle24" : ModStatusDisplay.Glyph(Status);

    public string StatusTooltip => HasDuplicateFolders
        ? "This mod is in both an enabled and a disabled folder - use Sort out to keep one copy"
        : IsMixedState
            ? "Partly disabled - some of this mod's folders are disabled and some aren't"
            : UpdateAvailable == true && !IsDisabled && LatestPublishedVersion is not null
                ? $"Update available - {LatestPublishedVersion}"
                : ModStatusDisplay.Tooltip(Status);

    // Groups raw scan results into one card per distinct mod and looks up each against the cached
    // catalog for its latest published version.
    // <param name="installRecords">The current install's manifest. Each record names the folders it
    // placed, which identifies a mod exactly, and the version actually fetched, which beats whatever
    // the files on disk claim. Pass null or an empty list if the manifest doesn't apply.</param>
    public static List<InstalledModCardViewModel> BuildFrom(
        IEnumerable<InstalledMod> scanned,
        IReadOnlyList<Mod> catalog,
        string? installedSptVersion,
        IReadOnlyList<InstalledModRecord>? installRecords = null)
    {
        var recordsByFolder = IndexByFolder(installRecords);
        var recordsByModId = (installRecords ?? [])
            .GroupBy(r => r.ModId)
            .ToDictionary(g => g.Key, g => g.First());

        var groups = scanned
            .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Key: g.Key, Entries: g.ToList()))
            .Select(g => (g.Entries, Match: ResolveCatalogMatch(g.Key, g.Entries, catalog, recordsByFolder)))
            .ToList();

        return MergeSplitClientServerHalves(groups)
            .Select(g => BuildCard(g.Entries, g.Match, installedSptVersion, recordsByModId))
            .ToList();
    }

    // Maps every folder a record identifies - whether an app-managed install placed it or a manual
    // version override names it - to that record. A folder claimed by two different mods is dropped
    // rather than resolved arbitrarily.
    private static Dictionary<string, InstalledModRecord> IndexByFolder(IReadOnlyList<InstalledModRecord>? records)
    {
        var index = new Dictionary<string, InstalledModRecord>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records ?? [])
        {
            foreach (var folder in InstalledModFolders.Resolve(record))
            {
                if (index.TryGetValue(folder, out var existing) && existing.ModId != record.ModId)
                {
                    ambiguous.Add(folder);
                    continue;
                }

                index[folder] = record;
            }
        }

        foreach (var folder in ambiguous) index.Remove(folder);

        return index;
    }

    // The catalog lookup used by BuildFrom, run once per name-group.
    private static Mod? ResolveCatalogMatch(
        string folderName,
        List<InstalledMod> entries,
        IReadOnlyList<Mod> catalog,
        Dictionary<string, InstalledModRecord> recordsByFolder)
    {
        // This app placed the folder, so the catalog listing is already known exactly. Everything
        // below is inference for mods installed by hand, which is where a folder like "EpicsAIO"
        // can't be talked into matching a listing called "Epic's All in One".
        if (recordsByFolder.TryGetValue(folderName, out var record))
        {
            var fromRecord = catalog.FirstOrDefault(m => m.Id == record.ModId)
                ?? catalog.FirstOrDefault(m => GuidsMatch(record.Guid, m.Guid));
            if (fromRecord is not null) return fromRecord;
        }

        // The real GUID, read from a client DLL's [BepInPlugin] attribute, is tried first as an
        // exact identifier before falling back to the folder-name heuristics below.
        var installedGuid = entries.FirstOrDefault(m => m.Target == InstalledModTarget.Client)?.Guid;

        // Name/slug inference is the only tier that can plausibly hit more than one listing, and
        // being attributed to the wrong one is worse than not being matched at all: the update it
        // then offers would install a different mod over this one. So both name/slug tiers require
        // exactly one candidate, and an exact name/slug match is tried before the fuzzy one rather
        // than letting a loose match on Name beat an exact match on Slug.
        return catalog.FirstOrDefault(m => GuidsMatch(installedGuid, m.Guid))
            ?? catalog.FirstOrDefault(m => GuidMatchesFolderName(m.Guid, folderName))
            ?? OnlyMatch(catalog, m => NamesMatch(m.Name, folderName) || NamesMatch(m.Slug, folderName))
            ?? OnlyMatch(catalog, m => NameOrSlugMatches(m.Name, folderName) || NameOrSlugMatches(m.Slug, folderName));
    }

    // The single catalog mod satisfying <paramref name="predicate"/>, or null when none or more
    // than one does.
    private static Mod? OnlyMatch(IReadOnlyList<Mod> catalog, Func<Mod, bool> predicate)
    {
        Mod? only = null;

        foreach (var mod in catalog)
        {
            if (!predicate(mod)) continue;
            if (only is not null && only.Id != mod.Id) return null;

            only ??= mod;
        }

        return only;
    }

    // Joins a client-only name-group to a server-only name-group when they're the only two that
    // independently resolved to the same catalog Mod. Groups that already have both targets, matched
    // nothing, or have an ambiguous match pass through unmerged.
    private static List<(List<InstalledMod> Entries, Mod? Match)> MergeSplitClientServerHalves(
        List<(List<InstalledMod> Entries, Mod? Match)> groups)
    {
        var results = new List<(List<InstalledMod> Entries, Mod? Match)>();
        var consumed = new HashSet<int>();

        for (var i = 0; i < groups.Count; i++)
        {
            if (consumed.Contains(i)) continue;

            var (entries, match) = groups[i];
            var hasClient = entries.Any(m => m.Target == InstalledModTarget.Client);
            var hasServer = entries.Any(m => m.Target == InstalledModTarget.Server);

            if (match is not null && hasClient != hasServer)
            {
                var partners = Enumerable.Range(i + 1, groups.Count - i - 1)
                    .Where(j => !consumed.Contains(j) && groups[j].Match?.Id == match.Id)
                    .Where(j =>
                    {
                        var otherHasClient = groups[j].Entries.Any(m => m.Target == InstalledModTarget.Client);
                        var otherHasServer = groups[j].Entries.Any(m => m.Target == InstalledModTarget.Server);
                        return otherHasClient != otherHasServer && otherHasClient != hasClient;
                    })
                    .ToList();

                if (partners.Count == 1)
                {
                    consumed.Add(partners[0]);
                    results.Add((entries.Concat(groups[partners[0]].Entries).ToList(), match));
                    continue;
                }
            }

            results.Add((entries, match));
        }

        return results;
    }

    private static InstalledModCardViewModel BuildCard(
        List<InstalledMod> entries,
        Mod? match,
        string? installedSptVersion,
        IReadOnlyDictionary<int, InstalledModRecord> recordsByModId)
    {
        var client = entries.FirstOrDefault(m => m.Target == InstalledModTarget.Client);
        var server = entries.FirstOrDefault(m => m.Target == InstalledModTarget.Server);

        var record = match is not null && recordsByModId.TryGetValue(match.Id, out var found) ? found : null;

        // The version recorded at install time beats anything read off disk: plenty of authors never
        // bump the assembly version, so a mod installed as 1.2.1 can still report 1.0.0.0 from its
        // DLL - which then reads as an update being available forever.
        var fileVersion = client?.Version ?? server?.Version;
        var installedVersion = record?.Version ?? fileVersion;

        string? detail = null;
        if (client is not null && server is not null
            && !string.Equals(client.Version, server.Version, StringComparison.OrdinalIgnoreCase))
        {
            detail = $"Files report client {client.Version ?? "unknown"} / server {server.Version ?? "unknown"}";
        }
        else if (record is not null && fileVersion is not null
                 && ModVersionComparer.IsUpdateAvailable(record.Version, fileVersion) is null or false
                 && !VersionsAreEquivalent(record.Version, fileVersion))
        {
            detail = $"Files report {fileVersion}";
        }

        if (record is not null && !record.IsAppManaged)
        {
            detail = detail is null ? "Manually confirmed" : $"{detail} - manually confirmed";
        }

        var installedAt = new[] { client?.InstalledAt, server?.InstalledAt }
            .Where(d => d is not null)
            .OrderBy(d => d)
            .FirstOrDefault();

        var latestVersion = match is null ? null : ModCardViewModel.LatestVersion(match);
        var latestPublished = latestVersion?.Version;

        // No installed version could be determined at all (no record, and nothing readable off the
        // files themselves) - plenty of mods never expose a usable version this way. Rather than
        // reading as an update forever, assume the latest published version is what's on disk.
        var versionUndetermined = installedVersion is null && match is not null;

        bool? isNewer;
        if (versionUndetermined)
        {
            isNewer = false;
            detail ??= latestPublished is not null
                ? $"Version couldn't be determined - assuming the latest published version ({latestPublished}) is installed"
                : "Version couldn't be determined - assuming it's up to date";
        }
        else
        {
            // A newer version alone isn't enough - it also has to target the installed SPT version.
            isNewer = ModVersionComparer.IsUpdateAvailable(installedVersion, latestPublished);
        }

        var updateAvailable = isNewer == true
            ? SptVersionMatcher.IsSatisfiedBy(latestVersion?.SptVersionConstraint, installedSptVersion)
            : isNewer;

        return new InstalledModCardViewModel
        {
            // Client's folder/package name wins when client and server disagree.
            Name = client?.Name ?? server!.Name,
            InstalledVersion = installedVersion,
            InstalledVersionDetail = detail,
            InstalledAt = installedAt,
            HasClient = client is not null,
            HasServer = server is not null,
            LatestPublishedVersion = latestPublished,
            LatestUpdatedAt = match?.UpdatedAt,
            MatchedModName = match?.Name,
            Guid = match?.Guid,
            Author = match?.Owner?.Name,
            IsFikaCompatible = match?.FikaCompatibility == true,
            ContainsAds = match?.ContainsAds == true,
            ContainsAiContent = match?.ContainsAiContent == true,
            UpdateAvailable = updateAvailable,
            ClientFolderPath = client?.FolderPath,
            ServerFolderPath = server?.FolderPath,
            ClientFolderName = client?.Name,
            ServerFolderName = server?.Name,
            FolderPath = client?.FolderPath ?? server!.FolderPath,
            ModId = match?.Id,
            IsAppManaged = record?.IsAppManaged ?? false,
            IsManualOverride = record is not null && !record.IsAppManaged,
            Entries = entries,
        };
    }

    // True when two loosely-formatted version strings mean the same release, so a trailing
    // ".0" difference isn't reported as a discrepancy.
    private static bool VersionsAreEquivalent(string? a, string? b) =>
        ModVersionComparer.IsUpdateAvailable(a, b) == false && ModVersionComparer.IsUpdateAvailable(b, a) == false;

    // Exact case-insensitive comparison between the real GUID read out of a client mod's DLL and a
    // catalog mod's Guid.
    private static bool GuidsMatch(string? installedGuid, string? catalogGuid) =>
        !string.IsNullOrWhiteSpace(installedGuid) && !string.IsNullOrWhiteSpace(catalogGuid)
        && string.Equals(installedGuid, catalogGuid, StringComparison.OrdinalIgnoreCase);

    // Best-effort match against a catalog mod's GUID by guessing it from the folder name. Tries
    // two folder-naming conventions:
    //
    // 1. Drop the leading segment, dash-join the rest - e.g. GUID "com.acidphantasm.bosseshavegpcoins"
    //    -> folder "acidphantasm-bosseshavegpcoins".
    // 2. Reverse the non-modname segments into domain order and concatenate, then dash-join with the
    //    modname - e.g. GUID "wtf.archangel.lotsoflootredux" -> folder "archangelwtf-lotsoflootredux".
    //
    // Tried before the Name/Slug fallback.
    private static bool GuidMatchesFolderName(string? guid, string folderName)
    {
        if (string.IsNullOrWhiteSpace(guid)) return false;

        var parts = guid.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        var dropFirst = string.Join('-', parts.Skip(1));
        if (NamesMatch(dropFirst, folderName)) return true;

        var modName = parts[^1];
        var reversedDomain = string.Concat(parts[..^1].Reverse());
        var reversedDomainCandidate = $"{reversedDomain}-{modName}";
        return NamesMatch(reversedDomainCandidate, folderName);
    }

    // Matches a trailing "-client"/"-server"/"_client"/"_server" (or no separator) on a folder name,
    // stripped before matching since it isn't part of the mod's actual identity.
    private static readonly Regex TrailingTargetSuffix =
        new(@"[-_]?(client|server)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Strict identity comparison used only for GUID-derived candidates (see GuidMatchesFolderName).
    // Strips the trailing target suffix, then punctuation and casing, before comparing exactly.
    private static bool NamesMatch(string? candidate, string folderName)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;

        var trimmedFolder = TrailingTargetSuffix.Replace(folderName, "");
        return Normalize(candidate) == Normalize(trimmedFolder);
    }

    private static string Normalize(string s) =>
        new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    // Splits camelCase/PascalCase words apart before the non-alphanumeric split in SignificantTokens
    // below, so a folder name with no separator between words still tokenizes correctly. Two rules:
    // 1. lower/digit -> upper ("ServerMod" -> "Server Mod").
    // 2. upper -> upper+lower ("CSGasServer" -> "CS Gas Server").
    private static readonly Regex CamelCaseBoundary =
        new(@"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.Compiled);
    private static readonly Regex NonAlphanumericRun = new(@"[^a-zA-Z0-9]+", RegexOptions.Compiled);

    // Words that describe how a mod is packaged, not what it actually is - stripped from both sides
    // of NameOrSlugMatches's token comparison below.
    private static readonly HashSet<string> GenericTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "mod", "mods", "patch", "addon", "plugin", "client", "server", "backend", "frontend",
    };

    // Matches the first "-", "_", or "." in a folder name - used by NameOrSlugMatches to retry with a
    // probable author-name prefix dropped off (the "Author-ModName" convention).
    private static readonly Regex FirstSeparator = new(@"[-_.]", RegexOptions.Compiled);

    private static HashSet<string> SignificantTokens(string s)
    {
        var spaced = CamelCaseBoundary.Replace(s, " ");
        return NonAlphanumericRun.Split(spaced)
            .Select(t => t.ToLowerInvariant())
            .Where(t => t.Length >= 2 && !GenericTokens.Contains(t))
            .ToHashSet();
    }

    // Looser identity comparison used for the catalog's own Name/Slug (see BuildFrom). Three tiers:
    //
    // 1. Exact match (same strict comparison GUID-derived guesses use).
    // 2. Token coverage: every significant word in the folder name has to appear somewhere in the
    //    candidate, in any order, ignoring generic packaging words (see GenericTokens).
    // 3. Same token coverage as (2), retried against only the part of the folder name after its
    //    first "-"/"_" - covers the common "Author-ModName" folder convention.
    //
    // Known imprecision: a very short, single-token folder name can match any catalog entry containing
    // that token.
    private static bool NameOrSlugMatches(string? candidate, string folderName)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        if (NamesMatch(candidate, folderName)) return true;

        var candidateTokens = SignificantTokens(candidate);
        if (TokensCoveredBy(SignificantTokens(folderName), candidateTokens)) return true;

        var separator = FirstSeparator.Match(folderName);
        if (!separator.Success) return false;

        var afterAuthorPrefix = folderName[(separator.Index + separator.Length)..];
        return afterAuthorPrefix.Length > 0
            && TokensCoveredBy(SignificantTokens(afterAuthorPrefix), candidateTokens);
    }

    // True when every one of folderTokens appears somewhere in candidateTokens. The length-4 floor
    // guards against a near-empty token set matching everything by vacuous truth. A folder token also
    // counts as covered if it's a short abbreviation of some candidate token (see IsAbbreviationOf).
    private static bool TokensCoveredBy(HashSet<string> folderTokens, HashSet<string> candidateTokens)
    {
        if (folderTokens.Count == 0 || folderTokens.Sum(t => t.Length) < 4) return false;
        return folderTokens.All(t => candidateTokens.Contains(t) || candidateTokens.Any(c => IsAbbreviationOf(t, c)));
    }

    // True when folderToken reads as a plausible truncation of candidateToken: a prefix match with
    // folderToken between 3 and 6 letters, and candidateToken only a few letters longer.
    private static bool IsAbbreviationOf(string folderToken, string candidateToken) =>
        folderToken.Length is >= 3 and <= 6
        && candidateToken.Length - folderToken.Length is > 0 and <= 5
        && candidateToken.StartsWith(folderToken, StringComparison.Ordinal);
}
