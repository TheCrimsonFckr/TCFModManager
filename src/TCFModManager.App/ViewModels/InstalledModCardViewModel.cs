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
    // toggle can't settle on its own - one of the two copies has to be set aside first. Worked out
    // once rather than per read: Entries never changes, and HasDuplicateFolders is bound on every
    // card and every list row, so a computed property here is evaluated constantly.
    private IReadOnlyList<ModDuplicatePair>? _duplicateFolders;

    public IReadOnlyList<ModDuplicatePair> DuplicateFolders =>
        _duplicateFolders ??= ModDisableService.DuplicatePairs(Entries);

    public bool HasDuplicateFolders => DuplicateFolders.Count > 0;

    // Dims the whole card (and the group-view row) while disabled.
    public double CardOpacity => IsDisabled ? 0.45 : 1.0;

    // Ticked in the flat grid's multi-select mode. Cards are rebuilt on every scan, so a selection
    // doesn't survive one.
    [ObservableProperty]
    private bool _isSelected;

    //
    // The user-defined group this mod is assigned to, or null when it's in none. Filled in by
    // InstalledViewModel from the group store after each scan and after every group change, rather
    // than read here - the card knows nothing about where groups are stored.
    //
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GroupLabel))]
    [NotifyPropertyChangedFor(nameof(IsGrouped))]
    private string? _groupName;

    // The same assignment as GroupName, by id - what the Group filter matches on, since two groups
    // can be given the same name.
    [ObservableProperty]
    private Guid? _groupId;

    public string GroupLabel => GroupName ?? "Ungrouped";

    public bool IsGrouped => GroupName is not null;

    // The catalog listing's content flags as one line, or null when it carries none of them.
    public string? FlagsSummary
    {
        get
        {
            var flags = new List<string>();
            if (IsFikaCompatible) flags.Add("Fika compatible");
            if (ContainsAds) flags.Add("Contains ads");
            if (ContainsAiContent) flags.Add("Contains AI content");

            return flags.Count == 0 ? null : string.Join(" • ", flags);
        }
    }

    // Where this mod's recorded version comes from, spelled out for the details view.
    public string SourceLabel => IsAppManaged
        ? "Installed by this app - Remove deletes exactly the files it placed"
        : IsManualOverride
            ? "Installed by hand, with its version manually confirmed here"
            : "Installed by hand - Remove deletes its whole folder";

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

        // Every per-catalog-mod value the matching tiers below compare against is derived once here
        // rather than recomputed for each installed mod. Without it, matching 120 installed mods
        // against a 3000-entry catalog re-normalizes and re-tokenizes 360,000 catalog entries.
        var index = CatalogIndex.Build(catalog);

        var groups = scanned
            .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Key: g.Key, Entries: g.ToList()))
            .Select(g => (g.Entries, Match: ResolveCatalogMatch(g.Key, g.Entries, index, recordsByFolder)))
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

    //
    // Everything about the catalog the matching tiers need, derived once per BuildFrom call. The
    // tiers match exactly what they matched before - this only lifts the per-catalog-mod work out
    // of the inner loop, and turns the three exact tiers into dictionary lookups.
    //
    private sealed class CatalogIndex
    {
        // Catalog order decides ties in the tiers that take the first match, so each of these keeps
        // whichever mod was seen first.
        public required Dictionary<int, Mod> ById { get; init; }
        public required Dictionary<string, Mod> ByGuid { get; init; }
        public required Dictionary<string, Mod> ByGuidDerivedFolderName { get; init; }

        // The exact name/slug tier has to know when two different mods answer to the same
        // normalized name, so it can decline rather than pick one.
        public required Dictionary<string, List<Mod>> ByNormalizedNameOrSlug { get; init; }

        // The fuzzy tier still walks the whole catalog, but compares against these rather than
        // re-normalizing and re-tokenizing each candidate for every installed mod.
        public required List<CatalogTokens> Tokens { get; init; }

        public static CatalogIndex Build(IReadOnlyList<Mod> catalog)
        {
            var byId = new Dictionary<int, Mod>();
            var byGuid = new Dictionary<string, Mod>(StringComparer.OrdinalIgnoreCase);
            var byGuidFolder = new Dictionary<string, Mod>(StringComparer.Ordinal);
            var byName = new Dictionary<string, List<Mod>>(StringComparer.Ordinal);
            var tokens = new List<CatalogTokens>(catalog.Count);

            foreach (var mod in catalog)
            {
                byId.TryAdd(mod.Id, mod);

                if (!string.IsNullOrWhiteSpace(mod.Guid)) byGuid.TryAdd(mod.Guid, mod);

                foreach (var candidate in GuidDerivedFolderNames(mod.Guid))
                    byGuidFolder.TryAdd(candidate, mod);

                var normalizedName = NormalizedOrNull(mod.Name);
                var normalizedSlug = NormalizedOrNull(mod.Slug);

                foreach (var normalized in new[] { normalizedName, normalizedSlug })
                {
                    if (normalized is null) continue;
                    if (!byName.TryGetValue(normalized, out var list)) byName[normalized] = list = [];
                    if (list.All(m => m.Id != mod.Id)) list.Add(mod);
                }

                tokens.Add(new CatalogTokens(
                    mod,
                    normalizedName,
                    normalizedSlug,
                    SignificantTokens(mod.Name ?? string.Empty),
                    SignificantTokens(mod.Slug ?? string.Empty)));
            }

            return new CatalogIndex
            {
                ById = byId,
                ByGuid = byGuid,
                ByGuidDerivedFolderName = byGuidFolder,
                ByNormalizedNameOrSlug = byName,
                Tokens = tokens,
            };
        }
    }

    // One catalog mod's precomputed name and slug forms, for the fuzzy matching tier.
    private sealed record CatalogTokens(
        Mod Mod,
        string? NormalizedName,
        string? NormalizedSlug,
        HashSet<string> NameTokens,
        HashSet<string> SlugTokens);

    // The catalog lookup used by BuildFrom, run once per name-group.
    private static Mod? ResolveCatalogMatch(
        string folderName,
        List<InstalledMod> entries,
        CatalogIndex index,
        Dictionary<string, InstalledModRecord> recordsByFolder)
    {
        // This app placed the folder, so the catalog listing is already known exactly. Everything
        // below is inference for mods installed by hand, which is where a folder like "EpicsAIO"
        // can't be talked into matching a listing called "Epic's All in One".
        if (recordsByFolder.TryGetValue(folderName, out var record))
        {
            var fromRecord = index.ById.GetValueOrDefault(record.ModId)
                ?? (string.IsNullOrWhiteSpace(record.Guid) ? null : index.ByGuid.GetValueOrDefault(record.Guid));
            if (fromRecord is not null) return fromRecord;
        }

        // The real GUID, read from a client DLL's [BepInPlugin] attribute, is tried first as an
        // exact identifier before falling back to the folder-name heuristics below.
        var installedGuid = entries.FirstOrDefault(m => m.Target == InstalledModTarget.Client)?.Guid;

        if (!string.IsNullOrWhiteSpace(installedGuid) && index.ByGuid.TryGetValue(installedGuid, out var byGuid))
            return byGuid;

        // Derived from the folder name once, rather than from every catalog entry in turn.
        var normalizedFolder = Normalize(TrailingTargetSuffix.Replace(folderName, ""));

        if (normalizedFolder.Length > 0
            && index.ByGuidDerivedFolderName.TryGetValue(normalizedFolder, out var byGuidFolder))
        {
            return byGuidFolder;
        }

        // Name/slug inference is the only tier that can plausibly hit more than one listing, and
        // being attributed to the wrong one is worse than not being matched at all: the update it
        // then offers would install a different mod over this one. So both name/slug tiers require
        // exactly one candidate, and an exact name/slug match is tried before the fuzzy one rather
        // than letting a loose match on Name beat an exact match on Slug.
        if (normalizedFolder.Length > 0
            && index.ByNormalizedNameOrSlug.TryGetValue(normalizedFolder, out var exact))
        {
            return exact.Count == 1 ? exact[0] : null;
        }

        return OnlyFuzzyMatch(index, folderName, normalizedFolder);
    }

    //
    // The single catalog mod whose name or slug covers the folder name's significant tokens, or
    // null when none or more than one does - the same rule NameOrSlugMatches applied per candidate,
    // with the folder's own forms built once instead of once per catalog entry.
    //
    private static Mod? OnlyFuzzyMatch(CatalogIndex index, string folderName, string normalizedFolder)
    {
        var folderTokens = TokenSet.For(folderName);

        var separator = FirstSeparator.Match(folderName);
        var afterPrefix = separator.Success ? folderName[(separator.Index + separator.Length)..] : null;
        var afterPrefixTokens = string.IsNullOrEmpty(afterPrefix) ? TokenSet.Empty : TokenSet.For(afterPrefix);

        // Neither side can cover anything and there is no exact form to compare, so the whole
        // catalog walk is skipped rather than run to reach the same answer.
        if (!folderTokens.CanCover && !afterPrefixTokens.CanCover && normalizedFolder.Length == 0) return null;

        Mod? only = null;

        foreach (var candidate in index.Tokens)
        {
            if (!Covers(candidate.NormalizedName, candidate.NameTokens)
                && !Covers(candidate.NormalizedSlug, candidate.SlugTokens))
            {
                continue;
            }

            if (only is not null && only.Id != candidate.Mod.Id) return null;

            only ??= candidate.Mod;
        }

        return only;

        bool Covers(string? normalizedCandidate, HashSet<string> candidateTokens) =>
            (normalizedCandidate is not null && normalizedFolder.Length > 0 && normalizedCandidate == normalizedFolder)
            || folderTokens.IsCoveredBy(candidateTokens)
            || afterPrefixTokens.IsCoveredBy(candidateTokens);
    }

    // Normalize, or null when the source is blank or normalizes to nothing - an empty key would
    // otherwise match every folder name that normalizes to nothing too.
    private static string? NormalizedOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var normalized = Normalize(value);
        return normalized.Length == 0 ? null : normalized;
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

    // The folder names a catalog mod's GUID would plausibly have been installed under, normalized
    // ready to compare. Two folder-naming conventions:
    //
    // 1. Drop the leading segment, dash-join the rest - e.g. GUID "com.acidphantasm.bosseshavegpcoins"
    //    -> folder "acidphantasm-bosseshavegpcoins".
    // 2. Reverse the non-modname segments into domain order and concatenate, then dash-join with the
    //    modname - e.g. GUID "wtf.archangel.lotsoflootredux" -> folder "archangelwtf-lotsoflootredux".
    //
    // Tried before the Name/Slug fallback.
    private static IEnumerable<string> GuidDerivedFolderNames(string? guid)
    {
        if (string.IsNullOrWhiteSpace(guid)) yield break;

        var parts = guid.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) yield break;

        if (NormalizedOrNull(string.Join('-', parts.Skip(1))) is { } dropFirst) yield return dropFirst;

        var modName = parts[^1];
        var reversedDomain = string.Concat(parts[..^1].Reverse());
        if (NormalizedOrNull($"{reversedDomain}-{modName}") is { } reversed) yield return reversed;
    }

    // Matches a trailing "-client"/"-server"/"_client"/"_server" (or no separator) on a folder name,
    // stripped before matching since it isn't part of the mod's actual identity.
    private static readonly Regex TrailingTargetSuffix =
        new(@"[-_]?(client|server)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
    // of OnlyFuzzyMatch's token comparison.
    private static readonly HashSet<string> GenericTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "mod", "mods", "patch", "addon", "plugin", "client", "server", "backend", "frontend",
    };

    // Matches the first "-", "_", or "." in a folder name - used by OnlyFuzzyMatch to retry with a
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

    //
    // A folder name's significant tokens, with the "is this worth comparing at all" test done once
    // up front rather than repeated for every catalog entry it is compared against. The length-4
    // floor guards against a near-empty token set matching everything by vacuous truth.
    //
    private readonly struct TokenSet
    {
        public static TokenSet Empty { get; } = new([]);

        private readonly HashSet<string> _tokens;

        private TokenSet(HashSet<string> tokens)
        {
            _tokens = tokens;
            CanCover = tokens.Count > 0 && tokens.Sum(t => t.Length) >= 4;
        }

        public bool CanCover { get; }

        public static TokenSet For(string source) => new(SignificantTokens(source));

        //
        // True when every one of these tokens appears somewhere in candidateTokens. A token also
        // counts as covered if it is a short abbreviation of some candidate token (see
        // IsAbbreviationOf). Plain loops rather than LINQ: this runs once per catalog entry per
        // unmatched mod, which is where the matching pass spends nearly all of its time.
        //
        public bool IsCoveredBy(HashSet<string> candidateTokens)
        {
            if (!CanCover) return false;

            foreach (var token in _tokens)
            {
                if (candidateTokens.Contains(token)) continue;

                var abbreviated = false;
                foreach (var candidate in candidateTokens)
                {
                    if (!IsAbbreviationOf(token, candidate)) continue;

                    abbreviated = true;
                    break;
                }

                if (!abbreviated) return false;
            }

            return true;
        }
    }

    // True when folderToken reads as a plausible truncation of candidateToken: a prefix match with
    // folderToken between 3 and 6 letters, and candidateToken only a few letters longer.
    private static bool IsAbbreviationOf(string folderToken, string candidateToken) =>
        folderToken.Length is >= 3 and <= 6
        && candidateToken.Length - folderToken.Length is > 0 and <= 5
        && candidateToken.StartsWith(folderToken, StringComparison.Ordinal);
}
