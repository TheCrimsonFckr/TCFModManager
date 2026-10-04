using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// A cheap stand-in for "has anything the Installed page shows changed since its last scan?", so a
// return visit can keep the cards it already has instead of rescanning and rebuilding every one -
// the rebuild is what froze the page transition for ~300-470ms on a 107-mod install.
//
// Deliberately errs towards "changed": a false "changed" costs one rescan, the same as every visit
// cost before, while a false "unchanged" would show a stale page. So it covers everything the scan
// reads - each mod container and its disabled twin, every DLL and package.json under each mod
// folder - plus the removed-mods sessions behind the leftover notes, and the app's own Data files
// (install records, mod lists and pins, the download ledger, the catalog caches, settings). The
// catalog and addon lists are compared by identity, because a refresh or the update watcher's
// patch replaces the list rather than changing it in place.
//
// The only Data files left out are the two that pages write as a matter of course without changing
// anything the Installed page shows: its own group assignments, which it applies in place, and the
// Footprint page's cache, which that page rewrites on every visit.
//
// Every DLL's size and modified time is metadata only - no file is opened - so this is a directory
// walk rather than the PE reading that makes up most of a scan.
//
internal static class InstallFingerprint
{
    private static readonly HashSet<string> IgnoredDataFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "mod_groups.json",
        "mod_footprints.json",
    };

    private static readonly EnumerationOptions Recursive = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = true,
    };

    public static string Compute(string installPath, object catalog, object addons, string? sptVersion)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append(installPath).Append('|').Append(sptVersion)
                .Append('|').Append(RuntimeHelpers.GetHashCode(catalog))
                .Append('|').Append(RuntimeHelpers.GetHashCode(addons)).Append('\n');

            foreach (var container in DisabledModPaths.ClientContainers(installPath)
                         .Concat(DisabledModPaths.ServerContainers(installPath)))
            {
                AppendContainer(sb, container);
                AppendContainer(sb, DisabledModPaths.Disabled(container));
            }

            AppendRemovedSessions(sb, RemovedMods.Root(installPath));
            AppendDataFiles(sb, AppPaths.DataDirectory);

            return sb.ToString();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Something moved mid-walk - which is itself a change, so this never matches.
            return Guid.NewGuid().ToString();
        }
    }

    private static void AppendContainer(StringBuilder sb, string container)
    {
        sb.Append("C ").Append(container);
        if (!Directory.Exists(container))
        {
            sb.Append(" -\n");
            return;
        }
        sb.Append('\n');

        foreach (var file in Sorted(new DirectoryInfo(container).EnumerateFiles()))
            AppendFile(sb, file);

        foreach (var dir in Sorted(new DirectoryInfo(container).EnumerateDirectories()))
        {
            sb.Append("D ").Append(dir.Name).Append(' ').Append(dir.LastWriteTimeUtc.Ticks).Append('\n');

            foreach (var file in Sorted(dir.EnumerateFiles("*.dll", Recursive)))
                AppendFile(sb, file, dir.FullName);

            var packageJson = new FileInfo(Path.Combine(dir.FullName, "package.json"));
            if (packageJson.Exists) AppendFile(sb, packageJson, dir.FullName);
        }
    }

    private static void AppendRemovedSessions(StringBuilder sb, string root)
    {
        sb.Append("R ").Append(root);
        if (!Directory.Exists(root))
        {
            sb.Append(" -\n");
            return;
        }
        sb.Append('\n');

        foreach (var session in Sorted(new DirectoryInfo(root).EnumerateDirectories()))
        {
            sb.Append("S ").Append(session.Name).Append('\n');
            foreach (var file in Sorted(session.EnumerateFiles())) AppendFile(sb, file);
        }
    }

    private static void AppendDataFiles(StringBuilder sb, string dataDirectory)
    {
        if (!Directory.Exists(dataDirectory)) return;

        foreach (var file in Sorted(new DirectoryInfo(dataDirectory).EnumerateFiles()))
            if (!IgnoredDataFiles.Contains(file.Name)) AppendFile(sb, file);
    }

    private static void AppendFile(StringBuilder sb, FileInfo file, string? relativeTo = null) =>
        sb.Append("F ").Append(relativeTo is null ? file.Name : Path.GetRelativePath(relativeTo, file.FullName))
            .Append(' ').Append(file.Length)
            .Append(' ').Append(file.LastWriteTimeUtc.Ticks).Append('\n');

    private static IEnumerable<T> Sorted<T>(IEnumerable<T> entries) where T : FileSystemInfo =>
        entries.OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase);
}
