using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class InstalledViewModel
{
    // "Go back to 1.2.0 - kept until 3 December" for an app-installed mod whose last update's
    // replaced version is still held; null when there is none.
    public static string? GoBackMenuLabel(InstalledModCardViewModel mod)
    {
        if (Held(mod) is not { } held || held.Log.Record?.Version is not { } version) return null;

        return RemovedMods.KeepFor(new SettingsService().Load().RemovedModsRetention) is { } keepFor
            ? Text(Strings.Installed_GoBackUntilFormat, version, (held.Log.RemovedAt + keepFor).ToLocalTime().ToString("M", CultureInfo.CurrentCulture))
            : Text(Strings.Installed_GoBackFormat, version);
    }

    private static (string Folder, RemovalLog Log)? Held(InstalledModCardViewModel mod)
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath)
            || mod is not { IsAppManaged: true, ModId: { } id, InstalledVersion: { } current }
            || mod.Entries.Any(e => e.IsDisabled))
        {
            return null;
        }

        try
        {
            return RemovedMods.LatestReplaced(installPath, id, mod.IsAddon, current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Debug("Installed", $"couldn't read held updates: {ex.Message}");
            return null;
        }
    }

    [RelayCommand]
    private async Task GoBackAsync(InstalledModCardViewModel? mod)
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (mod is null || string.IsNullOrWhiteSpace(installPath) || mod.ModId is not { } id || mod.InstalledVersion is not { } current) return;

        var record = AppServices.InstallManifest.Load().Find(id, mod.IsAddon);
        if (Held(mod) is not { } held || held.Log.Record is not { } old || record is null)
        {
            StatusMessage = Strings.Installed_UndoNothing;
            return;
        }

        var title = mod.DisplayTitle;
        if (AskWindow.Show(new AskRequest
            {
                Title = Text(Strings.Installed_GoBackTitleFormat, old.Version),
                Lead = Text(Strings.Installed_GoBackLeadFormat, title, old.Version, current),
                Detail = [Strings.Installed_GoBackConfigs, Text(Strings.Installed_GoBackForwardFormat, current)],
                First = Strings.Installed_GoBack,
            }) != AskChoice.First)
        {
            return;
        }

        if (AppServices.DownloadQueue.Items.Any(i => i.Status == DownloadQueueItemStatus.Installing))
        {
            StatusMessage = Strings.Installed_GoBackWaitForInstall;
            return;
        }

        IsBusy = true;
        try
        {
            var result = await Task.Run(() => AppServices.ModInstall.RevertUpdate(installPath, record, held.Folder));

            StatusMessage = !result.Ran
                ? Strings.Installed_UndoNothing
                : result.Blocked.Count == 0
                    ? Text(Strings.Installed_WentBackFormat, title, old.Version)
                    : Text(Strings.Installed_WentBackBlockedFormat, title, old.Version, held.Folder);

            ModRemoved?.Invoke(this, EventArgs.Empty);
        }
        catch (ModInstallException ex)
        {
            StatusMessage = ModInstallProblems.Describe(ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = Text(Strings.Installed_RemoveFailedFormat, title, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }

        var shown = StatusMessage;
        await ScanAsync();
        StatusMessage = shown;
    }
}
