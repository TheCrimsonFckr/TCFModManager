using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// A client folder several mods share (OPEN-28) - SPT Stance Overhaul and SPT Realism Common Lib both
// installing into BepInEx\plugins\Realism - reported as one entry per mod rather than one per folder.
// The live folder and its .disabled counterpart are split together, so a share keeps its name and
// owner whichever side its files are on.
//
public static partial class InstalledModScanner
{
    private sealed record ShareFile(string FullPath, string Relative, bool Disabled, bool IsTopLevelDll, DeclaredMetadata? Plugin);

    private sealed class Share
    {
        public int? ModId { get; init; }
        public InstalledModRecord? Record { get; set; }
        public string? Guid { get; set; }
        public List<ShareFile> Files { get; } = [];
        public DateTimeOffset Placed { get; set; } = DateTimeOffset.MaxValue;
        public string Name { get; set; } = "";
    }

    private static List<InstalledMod>? SplitShared(
        string installPath,
        string container,
        string name,
        string? liveDir,
        string? offDir,
        List<(string Dll, DeclaredMetadata Declared)> livePlugins,
        List<(string Dll, DeclaredMetadata Declared)> offPlugins,
        bool isPatcher,
        ModOwnership owners,
        HashSet<string> taken)
    {
        var records = owners.RecordsIn(installPath, RelativeTo(installPath, Path.Combine(container, name)));
        var recordIds = records.Select(r => r.ModId).ToHashSet();
        var plugins = livePlugins.Concat(offPlugins).ToDictionary(p => p.Dll, p => p.Declared, StringComparer.OrdinalIgnoreCase);

        var claimed = new HashSet<int>(recordIds);
        var byGuidOnly = false;
        var unclaimedPlugin = false;

        foreach (var (dll, declared) in plugins)
        {
            if (owners.RecordFor(installPath, RelativeTo(installPath, dll)) is not null) continue;

            if (owners.ListingFor(declared.Guid) is { } listing)
            {
                claimed.Add(listing);
                if (!recordIds.Contains(listing)) byGuidOnly = true;
            }
            else if (declared.Guid is not null)
            {
                unclaimedPlugin = true;
            }
        }

        var unclaimedHasShare = unclaimedPlugin && !byGuidOnly && recordIds.Count > 0;
        if (claimed.Count + (unclaimedHasShare ? 1 : 0) < 2) return null;

        var files = new List<ShareFile>();
        if (liveDir is not null) files.AddRange(FilesIn(liveDir, disabled: false, plugins));
        if (offDir is not null) files.AddRange(FilesIn(offDir, disabled: true, plugins));

        var shares = new Dictionary<int, Share>();
        var unclaimed = unclaimedHasShare ? new Share() : null;
        var leftovers = new List<ShareFile>();

        foreach (var file in files)
        {
            if (owners.RecordFor(installPath, RelativeTo(installPath, file.FullPath)) is { } record)
            {
                ShareFor(record.ModId, record, record.Guid).Files.Add(file);
            }
            else if (file.Plugin?.Guid is { } guid && owners.ListingFor(guid) is { } listing)
            {
                ShareFor(listing, records.FirstOrDefault(r => r.ModId == listing), guid).Files.Add(file);
            }
            else if (unclaimed is not null && file.Plugin?.Guid is not null)
            {
                unclaimed.Files.Add(file);
            }
            else
            {
                leftovers.Add(file);
            }
        }

        var found = shares.Values.Where(s => s.Files.Count > 0).ToList();
        if (unclaimed is { Files.Count: > 0 }) found.Add(unclaimed);
        if (found.Count < 2) return null;

        foreach (var share in found)
        {
            share.Placed = share.Record?.InstalledAt
                ?? EarliestCreated(share.Files.Where(f => f.IsTopLevelDll))
                ?? EarliestCreated(share.Files)
                ?? DateTimeOffset.MaxValue;
        }

        var ordered = found
            .OrderBy(s => s.Placed)
            .ThenBy(s => Path.GetFileName(PrimaryDll(s, name)?.FullPath) ?? "", StringComparer.Ordinal)
            .ToList();

        (unclaimed is { Files.Count: > 0 } ? unclaimed : ordered[0]).Files.AddRange(leftovers);

        ordered[0].Name = name;
        foreach (var share in ordered.Skip(1))
        {
            var stem = Path.GetFileNameWithoutExtension((PrimaryDll(share, name) ?? share.Files[0]).FullPath);
            share.Name = taken.Contains(stem) ? $"{name}.{stem}" : stem;
            taken.Add(share.Name);
        }

        var results = new List<InstalledMod>();

        foreach (var share in ordered)
        {
            foreach (var disabled in new[] { false, true })
            {
                var side = share.Files.Where(f => f.Disabled == disabled).ToList();
                if (side.Count == 0) continue;

                results.Add(ShareEntry(share, side, disabled ? offDir! : liveDir!, name, disabled, isPatcher));
            }
        }

        return results;

        Share ShareFor(int modId, InstalledModRecord? record, string? guid)
        {
            if (!shares.TryGetValue(modId, out var share)) shares[modId] = share = new Share { ModId = modId };
            share.Record ??= record;
            share.Guid ??= guid;
            return share;
        }
    }

    private static InstalledMod ShareEntry(Share share, List<ShareFile> side, string dir, string folderName, bool disabled, bool isPatcher)
    {
        var plugins = side
            .Where(f => f.IsTopLevelDll)
            .OrderBy(f => f.Relative, StringComparer.Ordinal)
            .Select(f => (Dll: f.FullPath, Declared: f.Plugin ?? DeclaredMetadata.Empty))
            .ToList();

        var guids = plugins
            .Select(p => p.Declared.Guid)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var guid = guids.FirstOrDefault(g => string.Equals(g, share.Guid, StringComparison.OrdinalIgnoreCase))
            ?? guids.FirstOrDefault();

        var dll = PrimaryDll(share, folderName, side)?.FullPath;
        var primary = plugins.FirstOrDefault(p => p.Dll == dll && p.Declared.Guid is not null).Declared
            ?? plugins.FirstOrDefault(p => p.Declared.Guid is not null && p.Declared.Guid == guid).Declared;
        var fileVersion = dll is null ? null : TryGetFileVersion(dll);

        return new InstalledMod
        {
            Name = share.Name,
            Version = primary?.Version ?? fileVersion,
            FileVersion = fileVersion,
            DeclaredName = primary?.Name,
            Guid = guid,
            Guids = guid is null ? guids : [guid, .. guids.Where(g => !string.Equals(g, guid, StringComparison.OrdinalIgnoreCase))],
            Target = InstalledModTarget.Client,
            IsPatcher = isPatcher,
            FolderPath = dir,
            InstalledAt = EarliestCreated(side),
            IsDisabled = disabled,
            Dependencies = MergeDependencies(plugins.SelectMany(p => p.Declared.Dependencies)),
            Assemblies = disabled
                ? []
                : [.. side
                    .Where(f => f.Relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f.Relative, StringComparer.Ordinal)
                    .Take(MaxAssembliesPerEntry)
                    .Select(f => Describe(f.FullPath, f.Relative))],
            OwnedFiles = [.. side.Select(f => f.Relative).Order(StringComparer.Ordinal)],
            ShareOwnerModId = share.ModId,
        };
    }

    private static ShareFile? PrimaryDll(Share share, string folderName, IEnumerable<ShareFile>? within = null)
    {
        var dlls = (within ?? share.Files)
            .Where(f => f.IsTopLevelDll)
            .OrderBy(f => f.Disabled)
            .ThenBy(f => f.Relative, StringComparer.Ordinal)
            .ToList();

        return dlls.FirstOrDefault(f => share.Guid is not null && string.Equals(f.Plugin?.Guid, share.Guid, StringComparison.OrdinalIgnoreCase))
            ?? dlls.FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f.Relative), folderName, StringComparison.OrdinalIgnoreCase))
            ?? dlls.FirstOrDefault(f => f.Plugin?.Guid is not null)
            ?? dlls.FirstOrDefault();
    }

    private static IEnumerable<ShareFile> FilesIn(string dir, bool disabled, IReadOnlyDictionary<string, DeclaredMetadata> plugins)
    {
        IEnumerable<string> paths;
        try
        {
            paths = Directory.EnumerateFiles(dir, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = true,
            }).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var path in paths)
        {
            var relative = Path.GetRelativePath(dir, path).Replace('\\', '/');
            var topLevelDll = !relative.Contains('/') && relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

            yield return new ShareFile(path, relative, disabled, topLevelDll, plugins.GetValueOrDefault(path));
        }
    }

    private static DateTimeOffset? EarliestCreated(IEnumerable<ShareFile> files) =>
        files.Select(f => TryGetCreationTime(f.FullPath)).Where(t => t is not null).Min();

    private static string RelativeTo(string installPath, string path) =>
        Path.GetRelativePath(installPath, path).Replace('\\', '/');
}
