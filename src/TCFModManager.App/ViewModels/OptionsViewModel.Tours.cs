using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Tours;

namespace TCFModManager.App.ViewModels;

public partial class OptionsViewModel
{
    [ObservableProperty]
    private bool _showFeatureTours;

    [ObservableProperty]
    private string? _toursMessage;

    partial void OnShowFeatureToursChanged(bool value)
    {
        if (!_loaded) return;

        var settings = _settings.Load();
        settings.ShowFeatureTours = value;
        _settings.Save(settings);

        if (!value) TourRunner.Close();
    }

    [RelayCommand]
    private void ShowToursAgain()
    {
        var settings = _settings.Load();
        settings.SeenTours.Clear();
        _settings.Save(settings);

        ToursMessage = Strings.Options_ToursAgainDone;
    }
}
