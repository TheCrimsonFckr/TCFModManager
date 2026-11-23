using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;

namespace TCFModManager.App.ViewModels;

public partial class OptionsViewModel
{
    public IReadOnlyList<UpdateWaitItem> UpdateWaitOptions { get; } =
    [
        new(nameof(Strings.Options_UpdateWaitOff), 0),
        new(nameof(Strings.Options_UpdateWaitOneDay), 1),
        new(nameof(Strings.Options_UpdateWaitTwoDays), 2),
        new(nameof(Strings.Options_UpdateWaitThreeDays), 3),
        new(nameof(Strings.Options_UpdateWaitSevenDays), 7),
    ];

    [ObservableProperty]
    private UpdateWaitItem? _selectedUpdateWait;

    partial void OnSelectedUpdateWaitChanged(UpdateWaitItem? value)
    {
        if (!_loaded || value is null) return;
        AppServices.UpdateWait.SetDays(value.Days);
    }
}
