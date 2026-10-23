using System.Text.RegularExpressions;
using TCFModManager.Core.Models;

namespace TCFModManager.App.ViewModels;

public sealed partial class InstalledModCardViewModel
{
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
        // A mod sharing its folder (OPEN-28) was already identified by the scan, from the record that
        // placed its files or its plugin's GUID. The folder's records belong to its neighbours.
        if (entries.Where(m => !m.ShareOwnerIsAddon).Select(m => m.ShareOwnerModId).FirstOrDefault(id => id is not null) is { } owner)
            return index.ById.GetValueOrDefault(owner);

        // This app placed the folder, so the catalog listing is already known exactly. Everything
        // below is inference for mods installed by hand, which is where a folder like "EpicsAIO"
        // can't be talked into matching a listing called "Epic's All in One".
        if (!entries.Any(m => m.IsShare) && recordsByFolder.TryGetValue(folderName, out var record))
        {
            var fromRecord = index.ById.GetValueOrDefault(record.ModId)
                ?? (string.IsNullOrWhiteSpace(record.Guid) ? null : index.ByGuid.GetValueOrDefault(record.Guid));
            if (fromRecord is not null) return fromRecord;
        }

        // The real GUID the mod declares - a client DLL's [BepInPlugin], or an SPT 4.x server mod's
        // ModGuid - is tried first as an exact identifier before falling back to the folder-name
        // heuristics below. The client's goes first when a card has both halves, as it always has;
        // a server-only hand install, which never had a GUID to offer before, now matches exactly
        // too. Taken from whichever entry actually carries one rather than the first entry, since
        // a patcher never has one and would otherwise skip the whole tier.
        var installedGuid = entries
            .OrderBy(m => m.Target == InstalledModTarget.Client ? 0 : 1)
            .Select(m => m.Guid)
            .FirstOrDefault(g => !string.IsNullOrWhiteSpace(g));

        if (!string.IsNullOrWhiteSpace(installedGuid) && index.ByGuid.TryGetValue(installedGuid, out var byGuid))
            return byGuid;

        var fromName = InferFromFolderName(index, folderName);
        if (fromName is not null) return fromName;

        //
        // A patcher folder is conventionally the mod's name plus a packaging word - "MoreBotsAPI"
        // ships "MoreBotsPrepatch", "WTT - Content Backport" ships "WTT-ContentBackportPatcher" -
        // and that extra word is exactly what stops the tiers above from recognising it: the mod's
        // listing is named after the mod, not after its patcher. Trying again without it is what
        // ties a patcher to its own listing, and, once both halves resolve to the same one, is what
        // lets MergePatcherFolders put them on one card.
        //
        // Only ever a fallback, and only for a folder that is nothing but a patcher: the unmodified
        // name is tried first above, so a patcher that really is listed under a name ending in
        // "Patcher" still matches itself rather than being talked into its stem.
        //
        if (!entries.All(m => m.IsPatcher)) return null;

        var stem = PatcherNameSuffix.Replace(folderName, "");
        return stem.Length > 0 && stem.Length != folderName.Length ? InferFromFolderName(index, stem) : null;
    }

    // The folder-name tiers of ResolveCatalogMatch, in order of how much they can be trusted.
    private static Mod? InferFromFolderName(CatalogIndex index, string folderName)
    {
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
        //
        // Enumerable.Reverse spelled out rather than parts[..^1].Reverse().
        //
        // The slice is a string[], which converts implicitly to Span<string>, and C# 14 picks
        // MemoryExtensions.Reverse<T>(this Span<T>) - which reverses in place and returns VOID -
        // over the LINQ one. This project is net9.0-windows with LangVersion=latest, so it compiled
        // for as long as the newest SDK on the machine was 9. The moment a .NET 10 SDK is installed
        // (which building the ServerMap server mod requires - it targets net10.0) this line stops
        // compiling, with an error that names neither Reverse nor Span.
        //
        var reversedDomain = string.Concat(Enumerable.Reverse(parts[..^1]));
        if (NormalizedOrNull($"{reversedDomain}-{modName}") is { } reversed) yield return reversed;
    }

    // Matches a trailing "-client"/"-server"/"_client"/"_server" (or no separator) on a folder name,
    // stripped before matching since it isn't part of the mod's actual identity.
    [GeneratedRegex(@"[-_]?(client|server)$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingTargetSuffix { get; }

    private static string Normalize(string s) =>
        new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    // Splits camelCase/PascalCase words apart before the non-alphanumeric split in SignificantTokens
    // below, so a folder name with no separator between words still tokenizes correctly. Two rules:
    // 1. lower/digit -> upper ("ServerMod" -> "Server Mod").
    // 2. upper -> upper+lower ("CSGasServer" -> "CS Gas Server").
    [GeneratedRegex(@"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")]
    private static partial Regex CamelCaseBoundary { get; }

    [GeneratedRegex(@"[^a-zA-Z0-9]+")]
    private static partial Regex NonAlphanumericRun { get; }

    // Words that describe how a mod is packaged, not what it actually is - stripped from both sides
    // of OnlyFuzzyMatch's token comparison.
    private static readonly HashSet<string> GenericTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "mod", "mods", "patch", "addon", "plugin", "client", "server", "backend", "frontend",
    };

    // Matches the first "-", "_", or "." in a folder name - used by OnlyFuzzyMatch to retry with a
    // probable author-name prefix dropped off (the "Author-ModName" convention).
    [GeneratedRegex(@"[-_.]")]
    private static partial Regex FirstSeparator { get; }

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
