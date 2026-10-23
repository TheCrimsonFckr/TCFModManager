using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.ServerMap;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class ModListsViewModel
{
    //
    // Marks this list as the one this machine serves, and writes it into the Server Map mod's config
    // folder so the server picks it up.
    //
    // One step rather than export-then-copy: the app already knows where that folder is, and a file
    // the operator has to move by hand is a file that ends up in the wrong place - which is exactly
    // what happened the first time. The server re-reads on the file's timestamp, so there is nothing
    // to restart.
    //
    [RelayCommand(CanExecute = nameof(CanPublish))]
    private void Publish()
    {
        if (Selected is not { } row) return;

        var installPath = new SettingsService().Load().SptInstallPath;

        if (!ServerMapConfigFolder.TryFind(installPath, out var directory))
        {
            StatusMessage = Strings.ModLists_NoServerHere;
            return;
        }

        try
        {
            //
            // Always the preferred name, never the list's own. A folder holding one arbitrarily
            // named list works, but two of them is ambiguous and the server then serves neither -
            // writing the name it prefers means republishing under a new list name replaces the old
            // file instead of sitting beside it.
            //
            var path = Path.Combine(directory, PublishedFileName);

            //
            // The revision moves HERE when the contents differ from what is already published, and
            // nowhere else on this path - see ModListPublication. Without it an edited list goes out
            // under the number its receivers already hold, and they never ask for it again.
            //
            var list = row.List;
            var bumped = false;

            if (ModListPublication.NeedsNewRevision(list, File.Exists(path) ? ModListFile.Load(path).List : null))
            {
                // Refused for a list somebody else wrote, whose numbering is theirs - the same guard
                // an apply is under. Publishing still writes the file.
                list = AppServices.ModLists.BumpRevision(row.Id) ?? list;
                bumped = list.Revision != row.List.Revision;
            }

            ModListFile.Save(list, path);
            AppServices.ModLists.SetPublished(row.Id);
            Refresh(row.Id);

            AppLog.Info("ServerMap", $"published \"{row.Name}\" revision {list.Revision} to {path}");

            StatusMessage = bumped
                ? Text(Strings.ModLists_RepublishedFormat, row.Name, list.Revision)
                : Text(Strings.ModLists_PublishedFormat, row.Name, list.Revision);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = Text(Strings.ModLists_PublishFailedFormat, ex.Message);
        }
    }

    //
    // A served list is not republishable: it came from somewhere else, and serving it back would
    // make this machine claim authorship of somebody else's list.
    //
    private bool CanPublish() => Selected is { List.Origin: not ModListOrigin.Server, List.IsSnapshot: false };

    // Matches PublishedModList.PreferredFileName on the server side.
    private const string PublishedFileName = "published.tcfmodlist";

    //
    // Asks the server for its list again, whether or not the revision has moved.
    //
    // The revision cannot carry this on its own: it counts APPLIES, so an operator who edits the
    // list they publish and republishes it hands out different contents under the same number, and
    // the automatic path - which fetches only when the number moves - correctly declines to ask.
    // Deleting the held copy to make it re-download was the only way through, and deleting the list
    // to get the list is not a workflow.
    //
    // Here rather than only on the Server map page because this is where the list lives and where
    // somebody looking at a stale copy is standing. The fetch itself is the map page's, so both
    // routes store identically.
    //
    [RelayCommand(CanExecute = nameof(CanRefreshFromServer))]
    private async Task RefreshFromServerAsync()
    {
        if (Selected is not { IsFromServer: true } row) return;

        var gate = AppServices.ServerMap;

        if (!gate.IsConfigured)
        {
            StatusMessage = Strings.ModLists_NoServerAddress;
            return;
        }

        IsBusy = true;

        try
        {
            var before = row.List.Entries.ToList();
            var fetched = await gate.FetchAndStoreAsync();

            if (fetched is null)
            {
                // The gate has already worded why, and its wording is the one the map page shows.
                StatusMessage = Text(Strings.ModLists_ListNotFetchedFormat, gate.ListStatus);
                return;
            }

            Refresh(fetched.Id);

            //
            // A different list entirely, which is what publishing a NEW list looks like from here.
            // The old one is left alone rather than quietly replaced - it is still a true record of
            // what that server was serving, and only the user can say whether they still want it.
            //
            if (fetched.Id != row.Id)
            {
                StatusMessage = Text(
                    Strings.ModLists_ServerPublishingDifferentFormat, gate.ServerName, fetched.Name, row.Name);
                return;
            }

            StatusMessage = DescribeRefresh(before, fetched);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRefreshFromServer() => SelectionIsFromServer && !IsBusy;

    //
    // An sp-mod Mod List, read by its address and reviewed in its own window before anything is
    // stored (CLOSED-20). The window hands back the list folded into any copy already here; storing
    // it is the same Add a file import uses, so a re-import replaces rather than duplicates.
    //
    [RelayCommand(CanExecute = nameof(CanUseSpMod))]
    private void ImportFromSpMod() => StoreFromSpMod(SpModListImportWindow.Import());

    [RelayCommand(CanExecute = nameof(CanRefreshFromSpMod))]
    private void RefreshFromSpMod()
    {
        if (Selected is not { IsFromSpMod: true } row) return;

        StoreFromSpMod(SpModListImportWindow.Refresh(row.List));
    }

    [RelayCommand(CanExecute = nameof(CanOpenOnSpMod))]
    private void OpenOnSpMod()
    {
        if (Selected?.List.SpModSource is not { } source) return;

        try
        {
            Process.Start(new ProcessStartInfo(source.Url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn("ModLists", $"couldn't open {source.Url}: {ex.Message}");
        }
    }

    private bool CanUseSpMod() => !IsBusy;

    private bool CanRefreshFromSpMod() => SelectionIsFromSpMod && !IsBusy;

    private bool CanOpenOnSpMod() => SelectionIsFromSpMod;

    private void StoreFromSpMod(SpModListUpdate? update)
    {
        if (update is null) return;

        var stored = AppServices.ModLists.Add(update.List);
        Refresh(stored.Id);

        StatusMessage = update.IsNew
            ? Strings.ModLists_SpModImported(stored.Entries.Count, stored.Entries.Count, stored.Name)
            : update.HasChanges
                ? Text(Strings.ModLists_SpModUpdatedFormat, stored.Name, update.Previous!.Revision, stored.Revision)
                : Text(Strings.ModLists_SpModUnchangedFormat, stored.Name);
    }

    //
    // What the refresh actually brought back, named rather than counted - the whole reason for
    // pressing it is not being sure the copy in hand is the one being served, and "3 changes" does
    // not answer that.
    //
    private static string DescribeRefresh(List<ModListEntry> before, ModList after)
    {
        var added = after.Entries
            .Where(e => !ModListEntries.Contains(before, e))
            .Select(e => e.Name)
            .ToList();

        var removed = before
            .Where(b => !ModListEntries.Contains(after.Entries, b))
            .Select(b => b.Name)
            .ToList();

        // Version and scope, because those are the two things an operator changes without the list
        // getting longer or shorter, and the two a client is wrong about silently.
        var changed = after.Entries
            .Select(e => (Now: e, Was: before.FirstOrDefault(b => ModListEntries.SameMod(b, e))))
            .Where(p => p.Was is not null
                        && (p.Was.VersionId != p.Now.VersionId
                            || !string.Equals(p.Was.Version, p.Now.Version, StringComparison.OrdinalIgnoreCase)
                            || p.Was.EffectiveScope != p.Now.EffectiveScope))
            .Select(p => p.Now.Name)
            .ToList();

        if (added.Count == 0 && removed.Count == 0 && changed.Count == 0)
        {
            return Strings.ModLists_AlreadyPublishing(
                after.Entries.Count, after.Name, after.Revision, after.Entries.Count);
        }

        var parts = new List<string>();
        if (added.Count > 0) parts.Add(Text(Strings.ModLists_DiffAddedFormat, Named(added)));
        if (removed.Count > 0) parts.Add(Text(Strings.ModLists_DiffRemovedFormat, Named(removed)));
        if (changed.Count > 0) parts.Add(Text(Strings.ModLists_DiffChangedFormat, Named(changed)));

        return Text(
            Strings.ModLists_RefreshedFromServerFormat,
            after.Name,
            string.Join(Strings.Common_ClauseSeparator, parts));
    }

    // Five names and then a count, so a wholesale change does not become a paragraph.
    private static string Named(List<string> names) =>
        names.Count <= 5
            ? string.Join(Strings.Common_ListSeparator, names.Order(StringComparer.OrdinalIgnoreCase))
            : string.Join(Strings.Common_ListSeparator, names.Order(StringComparer.OrdinalIgnoreCase).Take(5))
              + Text(Strings.ModLists_AndMoreNamesFormat, names.Count - 5);

    [RelayCommand]
    private void StopFollowing()
    {
        if (Selected?.IsActiveServer == true) AppServices.ModLists.SetActiveServer(null);
        else AppServices.ModLists.SetActive(null);
        Refresh();
        StatusMessage = Strings.ModLists_StoppedFollowing;
    }

    // Writes a manifest, never mod files - "install mod 2426 at version 5", not somebody's archive.
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Export()
    {
        if (Selected is not { } row) return;

        var dialog = new SaveFileDialog
        {
            Title = Strings.ModLists_ExportDialogTitle,
            Filter = ModListFileDialog.Filter,
            FileName = ModListFile.SuggestedFileName(row.List),
            AddExtension = true,
            DefaultExt = ModListFile.Extension,
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            ModListFile.Save(row.List, dialog.FileName);
            StatusMessage = Text(Strings.ModLists_ExportedFormat, row.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = Text(Strings.ModLists_ExportFailedFormat, ex.Message);
        }
    }

    [RelayCommand]
    private void Import()
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.ModLists_ImportDialogTitle,
            Filter = ModListFileDialog.Filter,
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true) return;

        var read = ModListFile.Load(dialog.FileName);

        if (!read.Succeeded)
        {
            StatusMessage = Text(Strings.ModLists_ImportReadFailedFormat, read.Error);
            return;
        }

        var list = read.List!;
        var existing = AppServices.ModLists.Find(list.Id);

        AppServices.ModLists.Add(list);
        Refresh(list.Id);

        StatusMessage = existing is null
            ? Text(Strings.ModLists_ImportedFormat, list.Name)
            : Text(Strings.ModLists_ImportUpdatedFormat, list.Name, existing.Revision, list.Revision);
    }

    private async Task RunAsync(Func<Task> work)
    {
        if (IsBusy) return;

        IsBusy = true;

        try
        {
            await work();
        }
        catch (ModInstallException ex)
        {
            // Reverting an apply touches the install, so it can be refused like any other move -
            // and ModInstallException's Message is the reason name, not a sentence.
            AppLog.Error("ModLists", ex.ToString());
            StatusMessage = ModInstallProblems.Describe(ex);
        }
        catch (Exception ex)
        {
            AppLog.Error("ModLists", ex.ToString());
            StatusMessage = Text(Strings.ModLists_SomethingWentWrongFormat, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
