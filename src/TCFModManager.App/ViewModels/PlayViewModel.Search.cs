using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Services;

namespace TCFModManager.App.ViewModels;

//
// OPEN-54 N27: Play locked while Find the broken mod runs, and the search offered when the server
// stopped before it was ready.
//
public partial class PlayViewModel
{
    [ObservableProperty]
    private bool _offerBrokenModSearch;

    public bool IsSearching => AppServices.BrokenModFinder.IsRunning;

    private void WatchSearch() => AppServices.BrokenModFinder.PropertyChanged += OnSearchChanged;

    private void OnSearchChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BrokenModFinder.IsRunning)) return;

        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(CanStartServer));
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanQuickPlay));
        PlayCommand.NotifyCanExecuteChanged();
        QuickPlayCommand.NotifyCanExecuteChanged();
        if (IsSearching) OfferBrokenModSearch = false;
    }

    private bool RefusedWhileSearching()
    {
        if (!IsSearching) return false;

        HasError = true;
        Message = Localization.Strings.Search_PlayLocked;
        return true;
    }

    [RelayCommand]
    private static void FindBrokenMod() => AppServices.BrokenModFinder.OpenAndStart();
}
