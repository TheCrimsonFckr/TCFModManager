using System.IO;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class InstalledViewModel
{
    // Nothing matched it to sp-mod, it has folders of its own, and no install record names them.
    public static bool CanLinkToPage(InstalledModCardViewModel mod) =>
        mod is { ModId: null, IsAppManaged: false, SharedFolderName: null } && mod.Entries.Count > 0 && !mod.Entries.Any(e => e.IsShare);

    [RelayCommand]
    private async Task LinkToPageAsync(InstalledModCardViewModel? mod)
    {
        if (mod is null || !CanLinkToPage(mod)) return;
        if (ModListNameWindow.AskForPageLink(mod.DisplayTitle) is not { Length: > 0 } text) return;

        if (SpModPageLink.Parse(text) is not { Kind: SpModLinkKind.Mod or SpModLinkKind.Addon } link)
        {
            StatusMessage = Strings.ModLink_NotAPage;
            return;
        }

        var isAddon = link.Kind == SpModLinkKind.Addon;
        var name = isAddon
            ? AppServices.Addons.ById(link.Id)?.Name
            : AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == link.Id)?.Name;
        if (name is null)
        {
            StatusMessage = Strings.ModLink_NotInCatalog;
            return;
        }

        AppServices.ModLinks.Link(mod.Entries.Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase), link.Id, isAddon);
        AppLog.Info("ModLinks", $"{mod.DisplayTitle} linked to {(isAddon ? "addon" : "mod")} {link.Id} ({name})");

        await ScanAsync();
        StatusMessage = Text(Strings.ModLink_LinkedFormat, mod.DisplayTitle, name);
    }

    // A matched mod with addons on sp-mod and a folder to hold one: an addon installed by hand into it can be linked.
    public static bool CanLinkAddonFiles(InstalledModCardViewModel mod) =>
        mod is { IsAddon: false, ModId: { } id } && AppServices.Addons.ForMod(id).Any() && mod.Entries.Any(e => Directory.Exists(e.FolderPath));

    // The addons of this mod linked by hand to files, by id and name.
    public static IReadOnlyList<(int Id, string Name)> LinkedAddonsOf(InstalledModCardViewModel mod)
    {
        if (mod is not { IsAddon: false, ModId: { } id }) return [];

        var mine = AppServices.Addons.ForMod(id).ToDictionary(a => a.Id, a => a.Name ?? a.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return [.. AppServices.ModLinks.Load().AddonFiles.Where(a => mine.ContainsKey(a.AddonId)).Select(a => (a.AddonId, mine[a.AddonId]))];
    }

    [RelayCommand]
    private async Task LinkAddonFilesAsync(InstalledModCardViewModel? mod)
    {
        if (mod?.ModId is not { } modId || !CanLinkAddonFiles(mod)) return;
        if (ModListNameWindow.AskForAddonLink(mod.DisplayTitle) is not { Length: > 0 } text) return;

        if (SpModPageLink.Parse(text) is not { Kind: SpModLinkKind.Addon } link)
        {
            StatusMessage = Strings.ModLink_NotAnAddonPage;
            return;
        }

        if (AppServices.Addons.ById(link.Id) is not { } addon)
        {
            StatusMessage = Strings.ModLink_NotInCatalog;
            return;
        }

        if (addon.ModId != modId)
        {
            StatusMessage = Text(Strings.ModLink_NotThisModsAddonFormat, addon.Name, mod.DisplayTitle);
            return;
        }

        await ChooseAddonFilesAsync(mod, addon.Id, addon.Name ?? mod.DisplayTitle);
    }

    [RelayCommand]
    private async Task EditAddonFilesAsync((InstalledModCardViewModel Mod, int AddonId, string Name) target) =>
        await ChooseAddonFilesAsync(target.Mod, target.AddonId, target.Name);

    private async Task ChooseAddonFilesAsync(InstalledModCardViewModel mod, int addonId, string addonName)
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath)) return;

        var linked = AppServices.ModLinks.Load().AddonFiles.FirstOrDefault(a => a.AddonId == addonId)?.Files ?? [];
        var files = await Task.Run(() => CandidateFiles(installPath, mod, linked));

        if (AddonFilesWindow.Ask(addonName, mod.DisplayTitle, files) is not { } chosen) return;

        AppServices.ModLinks.LinkAddonFiles(addonId, chosen);
        AppLog.Info("ModLinks", $"{addonName} ({addonId}) linked to {chosen.Count} file(s) in {mod.DisplayTitle}");

        await ScanAsync();
        StatusMessage = chosen.Count == 0
            ? Text(Strings.ModLink_AddonUnlinkedFormat, addonName)
            : Strings.ModLink_AddonLinked(chosen.Count, addonName, chosen.Count, mod.DisplayTitle);
    }

    //
    // Every file in the mod's folders no install record placed, plus the addon's own linked files
    // wherever they are now: install-relative for storing, folder-relative for reading.
    //
    private static List<(string Path, string Shown, bool Chosen)> CandidateFiles(
        string installPath, InstalledModCardViewModel mod, IReadOnlyList<string> linked)
    {
        var placed = AppServices.InstallManifest.Load().Mods
            .Where(r => r.IsAppManaged)
            .SelectMany(r => r.Files)
            .Select(DisabledModPaths.ToEnabledRelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mine = linked.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = new Dictionary<string, (string Path, string Shown, bool Chosen)>(StringComparer.OrdinalIgnoreCase);
        var walk = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };

        void Add(string full, string shownRoot)
        {
            var path = DisabledModPaths.ToEnabledRelativePath(Path.GetRelativePath(installPath, full).Replace('\\', '/'));
            if (placed.Contains(path) && !mine.Contains(path)) return;
            found.TryAdd(path, (path, Path.GetRelativePath(shownRoot, full).Replace('\\', '/'), mine.Contains(path)));
        }

        foreach (var entry in mod.Entries.Where(e => Directory.Exists(e.FolderPath)))
        {
            var shownRoot = Path.GetDirectoryName(entry.FolderPath) ?? entry.FolderPath;
            try
            {
                var files = entry.OwnedFiles is { } owned
                    ? owned.Select(f => Path.Combine(entry.FolderPath, f.Replace('/', Path.DirectorySeparatorChar)))
                    : Directory.EnumerateFiles(entry.FolderPath, "*", walk);
                foreach (var file in files.ToList()) Add(file, shownRoot);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Debug("ModLinks", $"couldn't list {entry.FolderPath}: {ex.Message}");
            }
        }

        foreach (var path in linked)
        {
            if (found.ContainsKey(path)) continue;
            var full = Path.Combine(installPath, DisabledModPaths.WhereNow(installPath, path).Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full)) found[path] = (path, Path.GetFileName(Path.GetDirectoryName(full)) + "/" + Path.GetFileName(full), true);
        }

        return [.. found.Values.OrderBy(f => f.Shown, StringComparer.OrdinalIgnoreCase)];
    }
}
