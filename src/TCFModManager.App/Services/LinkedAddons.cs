using TCFModManager.App.Localization;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

// Addons linked by hand to files inside their parent's folder, standing in for install records.
public static class LinkedAddons
{
    public static List<InstalledModRecord> Records(ModLinks links, IEnumerable<InstalledModRecord>? records)
    {
        var recorded = (records ?? []).Where(r => r.IsAddon).Select(r => r.ModId).ToHashSet();

        return
        [
            .. links.AddonFiles
                .Where(a => !recorded.Contains(a.AddonId) && a.Files.Count > 0)
                .Select(a => Record(a.AddonId, [.. a.Files])),
        ];
    }

    public static InstalledModRecord Record(int addonId, List<string> files, string version = "")
    {
        var addon = AppServices.Addons.ById(addonId);
        return new InstalledModRecord
        {
            ModId = addonId,
            IsAddon = true,
            Name = addon?.Name ?? addonId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Slug = addon?.Slug,
            Version = version,
            InstalledAt = DateTimeOffset.MinValue,
            IsAppManaged = false,
            Files = files,
            Folders = [.. InstalledModFolders.FromPlacedFiles(files)],
        };
    }

    // The record the addons list shows for one addon linked by hand, or null.
    public static InstalledModRecord? ForAddonsList(int addonId) =>
        AppServices.ModLinks.Load().AddonFiles.FirstOrDefault(a => a.AddonId == addonId) is { } link
            ? Record(addonId, [.. link.Files], Strings.Common_Unknown)
            : null;
}
