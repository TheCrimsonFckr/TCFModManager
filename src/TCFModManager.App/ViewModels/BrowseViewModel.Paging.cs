using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Services;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class BrowseViewModel
{
    // How many matching cards make up one page, when nothing has been saved as this page's
    // default - see DefaultPageSize().
    private const int DefaultPageSizeValue = 12;

    // OPEN-12 A8: 48 and 64 too, and Endless (0) - no pager, more cards as the list nears its end.
    public List<int> PageSizeOptions { get; } = [8, DefaultPageSizeValue, 16, 24, 32, 48, 64, Converters.PageSizeLabelConverter.Endless];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEndless))]
    private int _pageSize = DefaultPageSizeValue;

    public bool IsEndless => PageSize == Converters.PageSizeLabelConverter.Endless;

    // How many cards Endless shows so far, and how many more each step adds.
    private const int EndlessStep = 24;

    private int _endlessShown = EndlessStep;

    // Endless only: the next step of cards, once the list has been scrolled near its end.
    public void LoadMore()
    {
        if (!IsEndless || Results.Count >= _filtered.Count || Results.Count < _endlessShown) return;

        _endlessShown += EndlessStep;
        _endlessAppending = true;
        try
        {
            GoToPage(1);
        }
        finally
        {
            _endlessAppending = false;
        }
    }

    private bool _endlessAppending;

    /// <summary>How many columns the results grid should show, driven by the results area's available width.</summary>
    [ObservableProperty]
    private int _columns = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _currentPage = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _totalPages = 1;

    public void UpdateLayoutForWidth(double availableWidth)
    {
        Columns = availableWidth switch
        {
            < 700 => 1,   // small
            < 1050 => 2,  // medium
            < 1400 => 3,  // large
            _ => 4,       // extra large
        };
    }

    private bool CanGoToPreviousPage() => CurrentPage > 1;

    [RelayCommand(CanExecute = nameof(CanGoToPreviousPage))]
    private void PreviousPage() => GoToPage(CurrentPage - 1);

    private bool CanGoToNextPage() => CurrentPage < TotalPages;

    [RelayCommand(CanExecute = nameof(CanGoToNextPage))]
    private void NextPage() => GoToPage(CurrentPage + 1);

    /// <summary>Replaces Results with exactly one page's worth of cards - never grows it, so only one page's thumbnails are ever in flight.</summary>
    private void GoToPage(int page)
    {
        var sw = Stopwatch.StartNew();
        CurrentPage = Math.Clamp(page, 1, TotalPages);
        var installedVersion = AppServices.SptEnvironment.InstalledVersion;

        var pins = AppServices.ModLists.GetPins();

        // Endless shows its first _endlessShown cards; a LoadMore keeps those and adds the next step.
        var first = _endlessAppending ? Results.Count : IsEndless ? 0 : (CurrentPage - 1) * PageSize;
        var take = IsEndless ? Math.Max(0, _endlessShown - first) : PageSize;

        var cards = new List<ModCardViewModel>();
        foreach (var mod in _filtered.Skip(first).Take(take))
        {
            var installed = FindInstalledMatch(mod);

            var card = ModCardViewModel.From(
                mod, installedVersion, installed, _selectedLines, AppServices.SptCatalog.Releases,
                AppServices.Addons.CountFor(mod.Id),
                installed is null ? null : ModListPlanner.PinKeys(ModListCandidates.From(installed)),
                _addonMatchesByMod.GetValueOrDefault(mod.Id));

            card.RefreshPin(pins);
            cards.Add(card);
        }

        // Behind the startup panel the first page is built a couple of cards per frame (see
        // SearchAsync); with the page on screen it is replaced in one go, as it always was.
        if (IsStartingUp)
        {
            _startupFill.Apply(Results, cards);
        }
        else if (_endlessAppending)
        {
            _startupFill.Cancel();
            foreach (var card in cards) Results.Add(card);
        }
        else
        {
            _startupFill.Cancel();
            Results.Clear();
            foreach (var card in cards) Results.Add(card);
        }

        AppLog.Debug("Browse", $"GoToPage: page {CurrentPage}/{TotalPages} rendered in {sw.ElapsedMilliseconds}ms");
    }
}
