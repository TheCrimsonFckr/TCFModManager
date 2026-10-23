using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Views;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class InstalledViewModel
{
    // "N conflicts - see Dependencies and Conflicts" on the status line, or null when there are none.
    [ObservableProperty]
    private string? _conflictCountLabel;

    [RelayCommand]
    private static void ShowConflicts() => AppNavigation.Navigate(typeof(DependenciesPage));

    // OPEN-13 D5: "3 mods aren't for SPT 4.1.6" on the status line, or null when none.
    [ObservableProperty]
    private string? _notForSptLabel;

    // The keys of the warnings Dismiss all would hide - the enabled mods the status line counts.
    private List<string> _notForSptKeys = [];

    //
    // "It works for me" on one card (Chris, 2026-10-12): the warning goes for this version on this
    // SPT, and comes back when either changes. Rescans so the status, filter and count follow.
    //
    [RelayCommand]
    private async Task DismissSptWarningAsync(InstalledModCardViewModel? card)
    {
        if (card is not { CanDismissSptWarning: true }) return;

        AppServices.DismissedSptWarnings.Dismiss([card.SptWarningKey]);
        AppLog.Info("Installed", $"dismissed the SPT warning for {card.DisplayTitle} {card.InstalledVersion}");
        await ScanCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task RestoreSptWarningAsync(InstalledModCardViewModel? card)
    {
        if (card is not { IsSptWarningDismissed: true }) return;

        AppServices.DismissedSptWarnings.Restore(card.SptWarningKey);
        AppLog.Info("Installed", $"restored the SPT warning for {card.DisplayTitle} {card.InstalledVersion}");
        await ScanCommand.ExecuteAsync(null);
    }

    // The dismissed warnings on the mods installed now - Show all warnings brings these back.
    private List<string> _dismissedSptKeys = [];

    [ObservableProperty]
    private bool _hasDismissedSptWarnings;

    [RelayCommand]
    private async Task ShowAllSptWarningsAsync()
    {
        var keys = _dismissedSptKeys;
        if (keys.Count == 0) return;

        foreach (var key in keys) AppServices.DismissedSptWarnings.Restore(key);
        AppLog.Info("Installed", $"restored {keys.Count} SPT warnings");
        await ScanCommand.ExecuteAsync(null);
    }

    // Dismiss all, after asking: every warning the status line counts.
    [RelayCommand]
    private async Task DismissAllSptWarningsAsync()
    {
        var keys = _notForSptKeys;
        if (keys.Count == 0) return;

        var question = Strings.Installed_SptDismissAllQuestion(keys.Count, keys.Count, AppServices.SptEnvironment.InstalledVersion);
        if (System.Windows.MessageBox.Show(question, Strings.Installed_SptDismissAllTitle,
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question, System.Windows.MessageBoxResult.No)
            != System.Windows.MessageBoxResult.Yes) return;

        AppServices.DismissedSptWarnings.Dismiss(keys);
        AppLog.Info("Installed", $"dismissed {keys.Count} SPT warnings");
        await ScanCommand.ExecuteAsync(null);
    }

    // The status-line link: ticks Not for this SPT so only those mods show.
    [RelayCommand]
    private void ShowNotForSpt()
    {
        foreach (var option in AttributeOptions.Where(o => o.Value == ModAttributeFilter.NotForThisSpt)) option.IsSelected = true;
    }
}
