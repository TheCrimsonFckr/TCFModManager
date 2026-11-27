using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Services;
using TCFModManager.Core.Services;

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

    private bool _launchingForSearch;

    private bool RefusedWhileSearching()
    {
        if (!IsSearching || _launchingForSearch) return false;

        HasError = true;
        Message = Localization.Strings.Search_PlayLocked;
        return true;
    }

    [RelayCommand]
    private static void FindBrokenMod() => AppServices.BrokenModFinder.OpenAndStart();

    //
    // OPEN-54 N27c: one round of the client search - the game as Quick play starts it: Play for the
    // picked profile, or else the server and the SPT launcher, where the user presses Play. Null when
    // it started; otherwise what went wrong, as the card says it.
    //
    public async Task<string?> StartGameForSearchAsync()
    {
        _launchingForSearch = true;
        try
        {
            Refresh();

            if (QuickPlaysDirect)
            {
                await PlayAsync();
                return PlayStatusIsError ? PlayStatus ?? Localization.Strings.Search_GameNotStarted : null;
            }

            if (Client is not { ExePath: not null } launcher) return Localization.Strings.Search_GameNotStarted;

            HasError = false;
            if (Server?.IsRunning != true) await StartServerAndWaitAsync(new SettingsService().Load(), openLauncher: !launcher.IsRunning);
            else if (!launcher.IsRunning) Start(SptLaunchTarget.Client);

            return HasError ? Message ?? Localization.Strings.Search_GameNotStarted : null;
        }
        finally
        {
            _launchingForSearch = false;
        }
    }
}
