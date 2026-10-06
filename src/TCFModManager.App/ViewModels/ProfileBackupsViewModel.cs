using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

// One copy of the SPT profiles on the Options page.
public sealed class ProfileBackupRow(ProfileBackup backup)
{
    public ProfileBackup Backup { get; } = backup;

    public string When => Backup.TakenAt.ToString("g", CultureInfo.CurrentCulture);

    public string Why => ProfileBackupsViewModel.Reason(Backup.Reason);

    public string Detail => LocalizationService.Text(Strings.Profiles_DetailFormat,
        Backup.Files, DownloadQueueItemViewModel.SizeLabel(Backup.Bytes));
}

//
// OPEN-12 F4: the copies of the SPT profiles taken before the app changes the install (see Core's
// ProfileBackups) - listed, taken by hand, put back. Lives on the Options page.
//
public sealed partial class ProfileBackupsViewModel : LocalizedViewModel
{
    public ObservableCollection<ProfileBackupRow> Backups { get; } = [];

    public bool IsEmpty => Backups.Count == 0;

    // The line on show; the full explanation is the card's tooltip.
    public string Summary => LocalizationService.Text(Strings.Profiles_SummaryFormat, AppServices.ProfileBackups.Keep);

    public string Description => LocalizationService.Text(Strings.Profiles_DescriptionFormat, AppServices.ProfileBackups.Keep);

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackUpNowCommand), nameof(RestoreCommand))]
    private bool _isBusy;

    private static string? InstallPath =>
        AppServices.SptEnvironment.InstallPath is { Length: > 0 } path ? path : null;

    public static string Reason(string reason) => reason switch
    {
        ProfileBackups.BeforeInstall => Strings.Profiles_ReasonInstall,
        ProfileBackups.BeforeRemove => Strings.Profiles_ReasonRemove,
        ProfileBackups.BeforeList => Strings.Profiles_ReasonList,
        ProfileBackups.BeforeDisable => Strings.Profiles_ReasonDisable,
        ProfileBackups.BeforeRestore => Strings.Profiles_ReasonRestore,
        _ => Strings.Profiles_ReasonManual,
    };

    // Run each time the Options page opens: every install, removal and list apply can add a copy.
    public void Refresh()
    {
        Backups.Clear();

        if (InstallPath is { } install)
        {
            try
            {
                foreach (var backup in AppServices.ProfileBackups.List(install)) Backups.Add(new ProfileBackupRow(backup));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Profiles", $"couldn't list the profile backups: {ex.Message}");
            }
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    // The rows carry text of their own, so a language change rebuilds them.
    protected internal override void RefreshText()
    {
        base.RefreshText();
        Refresh();
    }

    private bool CanChange => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanChange))]
    private async Task BackUpNowAsync()
    {
        if (InstallPath is not { } install)
        {
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        IsBusy = true;
        try
        {
            var taken = await Task.Run(() => AppServices.ProfileBackups.BackupNow(install));
            StatusMessage = taken is null ? Strings.Profiles_NoneFound : Strings.Profiles_BackedUp;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AppLog.Error("Profiles", "backing up by hand failed", ex);
            StatusMessage = LocalizationService.Text(Strings.Profiles_FailedFormat, ex.Message);
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private async Task RestoreAsync(ProfileBackupRow? row)
    {
        if (row is null) return;

        if (InstallPath is not { } install)
        {
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        var running = ModInstallService.RunningBlockers(install);
        if (running.Count > 0)
        {
            StatusMessage = ModInstallProblems.InstallInUse(running, ModInstallAction.RestoreProfiles);
            return;
        }

        if (MessageBox.Show(
                LocalizationService.Text(Strings.Profiles_RestoreConfirmFormat, row.When, row.Why),
                Strings.Profiles_RestoreTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            await Task.Run(() => AppServices.ProfileBackups.Restore(row.Backup, install));
            StatusMessage = LocalizationService.Text(Strings.Profiles_RestoredFormat, row.When);
        }
        catch (ModInstallException ex)
        {
            StatusMessage = ModInstallProblems.Describe(ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AppLog.Error("Profiles", "putting a profile backup back failed", ex);
            StatusMessage = LocalizationService.Text(Strings.Profiles_FailedFormat, ex.Message);
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (InstallPath is not { } install) return;

        var folder = AppServices.ProfileBackups.FolderFor(install);
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn("Profiles", $"couldn't open {folder}: {ex.Message}");
            StatusMessage = Strings.Common_FolderOpenFailed;
        }
    }
}
