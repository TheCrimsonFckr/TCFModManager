using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class InstalledViewModel
{
    // The Undo button's text, or null to hide it. One removal held: "Undo removing <mod>", which
    // undoes it on click. Several: "Undo a removal (n)", which opens HeldRemovals to pick from.
    [ObservableProperty]
    private string? _undoRemovalLabel;

    // Every removal still held for this install that can be undone, newest first - the Undo menu.
    [ObservableProperty]
    private IReadOnlyList<HeldRemovalItem> _heldRemovals = [];

    private void RefreshUndo()
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        var held = string.IsNullOrWhiteSpace(installPath) ? [] : HeldGroups(installPath);

        HeldRemovals =
        [
            .. held.Select(h => new HeldRemovalItem(
                h.Folder, Text(Strings.Installed_UndoRemovalItemFormat, h.Name, WhenLabel(h.RemovedAt)))),
        ];

        UndoRemovalLabel = held.Count switch
        {
            0 => null,
            1 => Text(Strings.Installed_UndoRemovalFormat, held[0].Name),
            var count => Text(Strings.Installed_UndoRemovalPickFormat, count),
        };
    }

    //
    // The held removals as Undo offers them: one entry per batch (OPEN-31 R2, a mod and its addons,
    // named after the mod and keyed on its removal), one per removal otherwise. Newest first.
    //
    private static List<(string Folder, string Name, DateTimeOffset RemovedAt)> HeldGroups(string installPath) =>
    [
        .. RemovedMods.Undoable(installPath)
            .GroupBy(h => h.Log.BatchId ?? h.Folder)
            .Select(g =>
            {
                var lead = g.FirstOrDefault(h => h.Log.IsBatchLead);
                if (lead.Folder is null) lead = g.First();

                var name = lead.Log.BatchId is not null && g.Count() > 1
                    ? Text(Strings.Installed_RemovalBatchFormat, lead.Log.BatchLead ?? lead.Log.ModName)
                    : lead.Log.ModName;

                return (lead.Folder, name, RemovedAt: g.Max(h => h.Log.RemovedAt));
            })
            .OrderByDescending(h => h.RemovedAt),
    ];

    private static string WhenLabel(DateTimeOffset at) =>
        at.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture);

    //
    // Marks the card of each folder a removal had to leave behind (RemovalLog.FoldersLeft) while that
    // removal is still held, so a leftover doesn't pass for a mod that's still installed.
    //
    private static void ApplyLeftovers(IReadOnlyList<InstalledModCardViewModel> cards)
    {
        foreach (var card in cards) card.LeftoverSummary = null;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath)) return;

        var left = new Dictionary<string, RemovalLog>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, log) in RemovedMods.Undoable(installPath))
            foreach (var folder in log.FoldersLeft)
                left.TryAdd(SamePath(Path.Combine(installPath, folder)), log);

        if (left.Count == 0) return;

        foreach (var card in cards.Where(c => !c.IsAppManaged))
        {
            var log = card.Entries
                .Select(e => left.GetValueOrDefault(SamePath(e.FolderPath)))
                .FirstOrDefault(l => l is not null);

            if (log is not null)
                card.LeftoverSummary = Text(Strings.Installed_LeftoverFormat, log.ModName, WhenLabel(log.RemovedAt));
        }
    }

    private static string SamePath(string path)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    //
    // Puts a removal back (D28): the one picked from the Undo menu, or the only one held. Any can go
    // first, and none ever overwrites: anything that has taken a removed file's place since is
    // reported and stays held.
    //
    [RelayCommand]
    private async Task UndoRemovalAsync(string? folder)
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath)) return;

        var undoable = HeldGroups(installPath);
        var picked = folder is null
            ? (undoable.Count == 1 ? undoable[0] : default)
            : undoable.FirstOrDefault(u => string.Equals(u.Folder, folder, StringComparison.OrdinalIgnoreCase));

        if (picked.Folder is null)
        {
            StatusMessage = Strings.Installed_UndoNothing;
            RefreshUndo();
            return;
        }

        var latest = picked;

        IsBusy = true;
        try
        {
            // Off the UI thread: putting back a large mod copies a lot of files.
            var result = await Task.Run(() => AppServices.ModInstall.UndoRemovalBatch(installPath, latest.Folder));

            StatusMessage = !result.Ran
                ? Strings.Installed_UndoNothing
                : result.Blocked.Count == 0
                    ? Text(Strings.Installed_UndoneFormat, latest.Name)
                    : Strings.Installed_UndoneBlocked(result.Blocked.Count, result.Blocked.Count, latest.Name, latest.Folder);

            ModRemoved?.Invoke(this, EventArgs.Empty);
        }
        catch (ModInstallException ex)
        {
            StatusMessage = ModInstallProblems.Describe(ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = Text(Strings.Installed_RemoveFailedFormat, latest.Name, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }

        await ScanAsync();
    }
}
