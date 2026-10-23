using System.IO;
using TCFModManager.App.Localization;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public sealed partial class InstalledModCardViewModel
{
    // Groups raw scan results into one card per distinct mod and looks up each against the cached
    // catalog for its latest published version.
    // <param name="installRecords">The current install's manifest. Each record names the folders it
    // placed, which identifies a mod exactly, and the version actually fetched, which beats whatever
    // the files on disk claim. Pass null or an empty list if the manifest doesn't apply.</param>
    public static List<InstalledModCardViewModel> BuildFrom(
        IEnumerable<InstalledMod> scanned,
        IReadOnlyList<Mod> catalog,
        string? installedSptVersion,
        IReadOnlyList<InstalledModRecord>? installRecords = null,
        IReadOnlyList<Addon>? addonCatalog = null)
    {
        // Addon records are held apart from mod records the whole way down: their ids belong to a
        // different sequence, so letting them into recordsByModId or the catalog index would have
        // addon 116 answer to mod 116.
        var addonRecords = (installRecords ?? []).Where(r => r.IsAddon).ToList();
        var modRecords = (installRecords ?? []).Where(r => !r.IsAddon).ToList();
        var addonRecordsByFolder = IndexByFolder(addonRecords);
        var addonRecordsById = addonRecords
            .GroupBy(r => r.ModId)
            .ToDictionary(g => g.Key, g => g.First());

        //
        // OPEN-31: an addon's share of its parent's folder was already identified by the scan, from
        // the record that placed its files, so it goes straight to an addon card and never near the
        // catalog matching below.
        //
        var all = scanned.ToList();
        var addonShares = all
            .Where(m => m is { ShareOwnerIsAddon: true, ShareOwnerModId: { } id } && addonRecordsById.ContainsKey(id))
            .ToList();
        var modEntries = addonShares.Count == 0 ? all : all.Except(addonShares).ToList();

        var recordsByFolder = IndexByFolder(modRecords);
        var recordsByModId = modRecords
            .GroupBy(r => r.ModId)
            .ToDictionary(g => g.Key, g => g.First());

        // Every per-catalog-mod value the matching tiers below compare against is derived once here
        // rather than recomputed for each installed mod. Without it, matching 120 installed mods
        // against a 3000-entry catalog re-normalizes and re-tokenizes 360,000 catalog entries.
        var index = CatalogIndex.Build(catalog);

        var scannedGroups = modEntries
            .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Key: g.Key, Entries: g.ToList()))
            .ToList();

        var resolved = scannedGroups
            .Select(g => (g.Key, g.Entries, Match: ResolveCatalogMatch(g.Key, g.Entries, index, recordsByFolder)))
            .ToList();

        //
        // An addon usually installs INTO its parent mod's folder rather than one of its own - a SAIN
        // preset lands in BepInEx\plugins\SAIN, a spawn notifier in the mod's own plugin folder - so
        // the folders its install record names are very often the parent's. A folder that resolves
        // to a catalog mod therefore stays that mod's, always: claiming it for the addon would make
        // the parent disappear from this page and come back wearing the addon's name.
        //
        // Only a folder nothing resolved to a mod, and that an addon install placed, is the addon's
        // own. An addon with no folder of its own has no card here and is shown inside its parent's
        // dialog instead, which is where it was installed from and where it updates.
        //
        var addonFolders = resolved
            .Where(g => g.Match is null && addonRecordsByFolder.ContainsKey(g.Key))
            .Select(g => (g.Key, g.Entries))
            .ToList();

        var claimed = addonFolders.Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var groups = MergeRecordFolders(
                resolved.Where(g => !claimed.Contains(g.Key)).ToList(),
                recordsByFolder,
                recordsByModId,
                index)
            .Select(g => (g.Entries, g.Match))
            .ToList();

        // Patchers first: folding one into the mod it belongs to can turn a client-only group into
        // a client group that still needs its server half found, and never the other way round.
        // One count per parent mod, worked out once rather than per card.
        var addonsByParent = (addonCatalog ?? [])
            .Where(a => a.ModId is not null)
            .GroupBy(a => a.ModId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var merged = MergeSplitClientServerHalves(MergePatcherFolders(groups));

        // OPEN-13 R5: Fika-incompatible releases are only worth saying anything about where Fika runs.
        var fikaPresent = FikaInstall.IsPresent(scannedGroups.SelectMany(g => g.Entries));

        // Read once for the whole scan rather than per card.
        var dismissedSpt = AppServices.DismissedSptWarnings.Load();

        //
        // A record belongs to the card holding the folders it placed. A second copy of the same mod
        // installed by hand ("SomeMod - Copy") resolves to the same catalog listing, and taking the
        // record by listing alone made that copy app-managed too - so removing the copy (Remove, or
        // Keep this one on the original) went by the record and took the ORIGINAL's files instead.
        // Where some card holds the record's folders, every other card of that listing is the
        // copy it looks like. Where none does, the record still applies by listing, as before.
        //
        var ownedRecords = merged
            .Where(g => g.Match is not null
                && recordsByModId.TryGetValue(g.Match.Id, out var r)
                && HoldsRecordFolder(g.Entries, r))
            .Select(g => g.Match!.Id)
            .ToHashSet();

        var cards = merged
            .Select(g =>
            {
                var record = g.Match is not null && recordsByModId.TryGetValue(g.Match.Id, out var found)
                    && (!ownedRecords.Contains(g.Match.Id) || HoldsRecordFolder(g.Entries, found))
                    ? found
                    : null;

                return BuildCard(g.Entries, g.Match, installedSptVersion, record, addonsByParent, fikaPresent, dismissedSpt);
            })
            .ToList();

        var addonGroups = addonFolders
            .Select(g => (Record: addonRecordsByFolder[g.Key], g.Entries))
            .Concat(addonShares
                .GroupBy(m => m.ShareOwnerModId!.Value)
                .Select(g => (Record: addonRecordsById[g.Key], Entries: g.ToList())))
            .GroupBy(x => x.Record.ModId)
            .Select(g => (g.First().Record, Entries: g.SelectMany(x => x.Entries).ToList()))
            .ToList();

        if (addonGroups.Count == 0)
        {
            NoteSharedFolders(cards);
            return cards;
        }

        // Addon cards come last because an addon's update state is measured against its parent
        // mod's installed version, which the cards built above are what report.
        var addonsById = (addonCatalog ?? []).ToDictionary(a => a.Id, a => a);
        var parentVersionsById = cards
            .Where(c => c is { IsAddon: false, ModId: not null })
            .GroupBy(c => c.ModId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        cards.AddRange(addonGroups.Select(g =>
        {
            var addon = addonsById.GetValueOrDefault(g.Record.ModId);
            var parent = addon?.ModId is { } parentId ? parentVersionsById.GetValueOrDefault(parentId) : null;
            var parentName = addon?.ModId is { } id
                ? catalog.FirstOrDefault(m => m.Id == id)?.Name ?? parent?.DisplayTitle
                : parent?.DisplayTitle;

            return BuildAddonCard(g.Entries, addon, g.Record, parent, parentName, installedSptVersion);
        }));

        NoteSharedFolders(cards);
        return cards;
    }

    //
    // One card for an addon this app installed. Everything it knows comes from the install record
    // and the cached addon listing - there is no folder/name/GUID matching involved, because an
    // addon has no GUID and its id shares no space with a mod's.
    //
    private static InstalledModCardViewModel BuildAddonCard(
        List<InstalledMod> entries,
        Addon? addon,
        InstalledModRecord record,
        InstalledModCardViewModel? parent,
        string? parentName,
        string? installedSptVersion)
    {
        var plugin = entries.FirstOrDefault(m => m is { Target: InstalledModTarget.Client, IsPatcher: false });
        var patcher = entries.FirstOrDefault(m => m is { Target: InstalledModTarget.Client, IsPatcher: true });
        var client = plugin ?? patcher;
        var server = entries.FirstOrDefault(m => m.Target == InstalledModTarget.Server);

        var fileVersion = client?.Version ?? server?.Version;
        var installedVersion = record.Version ?? fileVersion;

        // Only versions the installed parent mod actually satisfies count as an update: an addon
        // built for the next release of its parent isn't something this install can use yet.
        // AddonFit also counts an SPT range typed into the parent box, as sp-mod does.
        var parentVersion = parent?.InstalledVersion;
        var versions = (addon?.Versions ?? [])
            .OrderByDescending(v => v.PublishedAt ?? DateTimeOffset.MinValue)
            .ToList();

        var latest = versions.FirstOrDefault(v =>
            AddonFit.Accepts(v.ModVersionConstraint, parentVersion, installedSptVersion));

        var installedFits = parentVersion is not null
            && versions.FirstOrDefault(v => string.Equals(v.Version, installedVersion, StringComparison.OrdinalIgnoreCase)) is { } installedRelease
                ? AddonFit.Check(installedRelease.ModVersionConstraint, parentVersion, installedSptVersion)
                : null;

        (string Key, object?[] Values)? detail = null;
        if (latest is null && versions.Count > 0)
        {
            var newest = versions[0];
            detail = (parentVersion, parentName) switch
            {
                (null, null) => (nameof(Strings.Installed_DetailParentMissingUnnamed), []),
                (null, _) => (nameof(Strings.Installed_DetailParentMissingFormat), [parentName]),
                (_, null) => (
                    nameof(Strings.Installed_DetailParentConstraintUnnamedFormat),
                    [newest.Version, newest.ModVersionConstraint, parentVersion]),
                _ => (
                    nameof(Strings.Installed_DetailParentConstraintFormat),
                    [newest.Version, parentName, newest.ModVersionConstraint, parentVersion]),
            };
        }
        else if (installedFits == false && latest is not null && parentName is not null)
        {
            detail = (nameof(Strings.Installed_DetailAddonDoesntFitFormat), [installedVersion, parentName, parentVersion, latest.Version]);
        }
        else if (fileVersion is not null && !VersionsAreEquivalent(record.Version, fileVersion))
        {
            detail = (nameof(Strings.Installed_DetailFilesReportFormat), [fileVersion]);
        }

        return new InstalledModCardViewModel
        {
            Name = client?.Name ?? server!.Name,
            InstalledVersion = installedVersion,
            VersionDetail = detail,
            IsVersionManuallyConfirmed = !record.IsAppManaged,
            InstalledAt = InstallDate(record, client, server),
            HasClient = client is not null,
            HasPlugin = plugin is not null,
            HasPatcher = patcher is not null,
            HasServer = server is not null,
            LatestPublishedVersion = latest?.Version,
            LatestUpdatedAt = addon?.UpdatedAt,
            MatchedModName = addon?.Name ?? record.Name,
            Author = addon?.Owner?.Name,
            AuthorId = addon?.Owner?.Id,
            ContainsAds = addon?.ContainsAds == true,
            UpdateAvailable = ModVersionComparer.IsUpdateAvailable(installedVersion, latest?.Version),
            ClientFolderPath = client?.FolderPath,
            ServerFolderPath = server?.FolderPath,
            ClientFolderName = client?.Name,
            ServerFolderName = server?.Name,
            FolderPath = client?.FolderPath ?? server!.FolderPath,
            ModId = record.ModId,
            IsAddon = true,
            IsAddonListed = addon is not null,
            ParentModName = parentName,
            ParentModId = addon?.ModId,
            ParentInstalledVersion = parentVersion,
            IsAppManaged = record.IsAppManaged,
            IsManualOverride = !record.IsAppManaged,
            Entries = entries,
        };
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

    private static InstalledModCardViewModel BuildCard(
        List<InstalledMod> entries,
        Mod? match,
        string? installedSptVersion,
        InstalledModRecord? record,
        IReadOnlyDictionary<int, int> addonsByParent,
        bool fikaPresent = false,
        IReadOnlySet<string>? dismissedSpt = null)
    {
        // The plugin half speaks for the client side wherever there's a choice - it's the one with
        // the mod's real name, version and GUID on it, where a patcher is a support file whose
        // assembly version often has nothing to do with the mod's own.
        var plugin = entries.FirstOrDefault(m => m is { Target: InstalledModTarget.Client, IsPatcher: false });
        var patcher = entries.FirstOrDefault(m => m is { Target: InstalledModTarget.Client, IsPatcher: true });
        var client = plugin ?? patcher;
        var server = entries.FirstOrDefault(m => m.Target == InstalledModTarget.Server);

        // Not done for addon cards: an addon's record names its PARENT's folders, so comparing them
        // against the addon's own scan entries would report every one of them as missing.
        IReadOnlyList<string> missingFolders = record is null
            ? []
            : InstalledModFolders.MissingFrom(record, entries.SelectMany(FolderNamesOf));

        // The version recorded at install time beats anything read off disk: plenty of authors never
        // bump the assembly version, so a mod installed as 1.2.1 can still report 1.0.0.0 from its
        // DLL - which then reads as an update being available forever.
        var fileVersion = client?.Version ?? server?.Version;
        var installedVersion = record?.Version ?? fileVersion;

        (string Key, object?[] Values)? detail = null;
        // Compared as versions, not text: a plugin declaring "1.1.5" beside a server half that only
        // has its file version "1.1.5.0" is the same release, not a disagreement.
        if (client is not null && server is not null
            && !VersionsAreEquivalent(client.Version, server.Version))
        {
            detail = (
                nameof(Strings.Installed_DetailFilesReportPairFormat),
                [client.Version, server.Version]);
        }
        else if (record is not null && fileVersion is not null
                 && ModVersionComparer.IsUpdateAvailableByNumbers(record.Version, fileVersion) is null or false
                 && !VersionsAreEquivalent(record.Version, fileVersion))
        {
            detail = (nameof(Strings.Installed_DetailFilesReportFormat), [fileVersion]);
        }

        var manuallyConfirmed = record is not null && !record.IsAppManaged;

        var installedAt = InstallDate(record, client, server);

        //
        // What an update would install: the newest release that runs on this SPT - the same pick
        // Browse's card, its sort and Update selected all use. Comparing against the newest release
        // overall hid real updates: Task Search 1.0.1 on 4.0.13 has 1.9.1 for 4.0 and 2.0.2 for 4.1
        // only, and with the newest being 4.1-only the card said nothing at all.
        //
        var updateTarget = match is null ? null : ModCardViewModel.PickDisplayVersion(match, installedSptVersion);

        // The card's "Latest published" is the same pick, so it names a release this SPT can run whenever one exists.
        var latestPublished = updateTarget?.Version;

        // No installed version could be determined at all (no record, and nothing readable off the
        // files themselves) - plenty of mods never expose a usable version this way. Rather than
        // reading as an update forever, assume the latest published version is what's on disk.
        var versionUndetermined = installedVersion is null && match is not null;

        bool? isNewer;
        if (versionUndetermined)
        {
            isNewer = false;

            // Only when nothing else has been said about the version - a manually confirmed one
            // has already been explained, and these two would contradict it.
            if (detail is null && !manuallyConfirmed)
            {
                detail = latestPublished is not null
                    ? (nameof(Strings.Installed_DetailUndeterminedLatestFormat), [latestPublished])
                    : (nameof(Strings.Installed_DetailUndetermined), []);
            }
        }
        else
        {
            // A newer version alone isn't enough - it also has to target the installed SPT version.
            isNewer = record is null
                ? ModVersionComparer.IsUpdateAvailableByNumbers(installedVersion, updateTarget?.Version)
                : ModVersionComparer.IsUpdateAvailable(installedVersion, updateTarget?.Version);
        }

        var updateAvailable = isNewer == true
            ? SptVersionMatcher.IsSatisfiedBy(updateTarget?.SptVersionConstraint, installedSptVersion)
            : isNewer;

        var updateVersion = updateAvailable == true ? updateTarget?.Version : null;

        //
        // sp-mod holds that release back: it would break another installed mod (OPEN-12 F11). Not an
        // update, unless an older release every such mod accepts is still newer than this one.
        //
        HeldBackResolution? heldBack = null;
        if (updateVersion is not null && AppServices.HeldBack.For(match?.Id, installedSptVersion) is { } held && held.Holds(updateVersion))
        {
            heldBack = HeldBackVersions.Resolve(held, match!.Versions ?? [], installedSptVersion);

            var safeIsNewer = heldBack.Safe is not { } safe
                ? false
                : record is null
                    ? ModVersionComparer.IsUpdateAvailableByNumbers(installedVersion, safe)
                    : ModVersionComparer.IsUpdateAvailable(installedVersion, safe);

            if (safeIsNewer == true)
            {
                updateVersion = heldBack.Safe;
            }
            else
            {
                updateAvailable = false;
                updateVersion = null;
            }
        }

        //
        // OPEN-13: the installed version against this SPT - sp-mod's own answer first, then the catalog
        // release's range, then a 4.x server mod's declared range. Not judged when the version is a
        // guess (versionUndetermined).
        //
        var sptFit = versionUndetermined
            ? null
            : InstalledSptFit.Judge(installedVersion, installedSptVersion, match?.Versions,
                AppServices.HeldBack.NotForSpt(match?.Id, installedSptVersion), server?.SptVersion);

        var fikaIncompatible = fikaPresent && !versionUndetermined
            && FikaInstall.IsIncompatible(InstalledSptFit.Release(installedVersion, match?.Versions)?.FikaCompatibility);

        var sptWarningKey = DismissedSptWarningStore.Key(
            match?.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? (client?.Name ?? server!.Name),
            installedVersion, installedSptVersion);

        return new InstalledModCardViewModel
        {
            SptFit = sptFit,
            SptWarningKey = sptWarningKey,
            IsSptWarningDismissed = dismissedSpt?.Contains(sptWarningKey) == true,
            InstalledSptVersion = installedSptVersion,
            IsFikaIncompatible = fikaIncompatible,
            // Client's folder/package name wins when client and server disagree.
            Name = client?.Name ?? server!.Name,
            InstalledVersion = installedVersion,
            VersionDetail = detail,
            IsVersionManuallyConfirmed = manuallyConfirmed,
            InstalledVersionFromFiles = record is null && installedVersion is not null,
            InstalledAt = installedAt,
            HasClient = client is not null,
            HasPlugin = plugin is not null,
            HasPatcher = patcher is not null,
            HasServer = server is not null,
            LatestPublishedVersion = latestPublished,
            UpdateVersion = updateVersion,
            HeldBack = heldBack,
            LatestUpdatedAt = match?.UpdatedAt,
            MatchedModName = match?.Name,
            DeclaredName = match is null ? server?.DeclaredName ?? plugin?.DeclaredName : null,
            Guid = match?.Guid ?? client?.Guid ?? server?.Guid,
            Author = match?.Owner?.Name ?? (match is null ? server?.Author : null),
            AuthorId = match?.Owner?.Id,
            CategoryTag = match?.Category?.Title,
            AddonCount = match is null ? 0 : addonsByParent.GetValueOrDefault(match.Id),
            IsFikaCompatible = match?.FikaCompatibility == true,
            ContainsAds = match?.ContainsAds == true,
            UpdateAvailable = updateAvailable,
            ClientFolderPath = client?.FolderPath,
            ServerFolderPath = server?.FolderPath,
            ClientFolderName = client?.Name,
            ServerFolderName = server?.Name,
            FolderPath = client?.FolderPath ?? server!.FolderPath,
            ModId = match?.Id,
            IsAppManaged = record?.IsAppManaged ?? false,
            IsManualOverride = record is not null && !record.IsAppManaged,
            MissingFolders = missingFolders,
            WasPartlyInstalled = record?.Incomplete ?? false,
            Entries = entries,
        };
    }

    //
    // Every name a scan entry could be known by on disk, for comparing against what an install
    // record says it placed.
    //
    // Name alone is not enough: a server mod takes its name from its package.json when it has one,
    // which is often not what the folder is called, while a record always names the folder. The
    // path gives both spellings - the folder itself, and a loose DLL's file name with the extension
    // dropped, which is how the scanner and the record both name that case.
    //
    private static bool HoldsRecordFolder(List<InstalledMod> entries, InstalledModRecord record)
    {
        if (entries.Any(e => e.ShareOwnerModId == record.ModId && e.ShareOwnerIsAddon == record.IsAddon)) return true;

        var folders = InstalledModFolders.Resolve(record).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return entries.Where(e => !e.IsShare).SelectMany(FolderNamesOf).Any(folders.Contains);
    }

    private static IEnumerable<string> FolderNamesOf(InstalledMod entry)
    {
        yield return entry.Name;

        if (string.IsNullOrWhiteSpace(entry.FolderPath)) yield break;

        var path = entry.FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        yield return Path.GetFileName(path);
        yield return Path.GetFileNameWithoutExtension(path);
    }

    //
    // The record's date when this app placed the files - the last install or update. A folder's
    // creation date is only a stand-in for a mod installed by hand (a confirmed version included):
    // copied or extracted with its original dates, a mod installed today can read as a year old.
    //
    private static DateTimeOffset? InstallDate(InstalledModRecord? record, InstalledMod? client, InstalledMod? server) =>
        record is { IsAppManaged: true }
            ? record.InstalledAt
            : new[] { client?.InstalledAt, server?.InstalledAt }.Where(d => d is not null).Min();

    // True when two loosely-formatted version strings mean the same release, so a trailing ".0" - or
    // a label the files can't carry - isn't reported as a discrepancy.
    private static bool VersionsAreEquivalent(string? a, string? b) => ModVersionComparer.SameNumbers(a, b);
}
