using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class InstalledViewModel
{
    //
    // Which of the three views the results area is showing. All three render the same filtered,
    // sorted set - Cards paginates summary cards, Groups arranges compact rows under the user's own
    // MO2-style separators, List scrolls one expandable row per mod with its full details.
    //
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCards))]
    [NotifyPropertyChangedFor(nameof(ShowPager))]
    [NotifyPropertyChangedFor(nameof(ShowGroups))]
    [NotifyPropertyChangedFor(nameof(ShowList))]
    [NotifyPropertyChangedFor(nameof(ShowExpanders))]
    [NotifyCanExecuteChangedFor(nameof(ExpandAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(CollapseAllCommand))]
    [NotifyPropertyChangedFor(nameof(ScrollsItself))]
    private InstalledViewMode _viewMode = InstalledViewMode.Cards;

    public bool ShowCards => ViewMode == InstalledViewMode.Cards;

    public bool ShowGroups => ViewMode == InstalledViewMode.Groups;

    public bool ShowList => ViewMode == InstalledViewMode.List;

    // Cards and List are the two views made of expandable rows; group view's sections collapse on
    // their own terms and have their own controls, so expand/collapse all hides there.
    public bool ShowExpanders => ViewMode is InstalledViewMode.Cards or InstalledViewMode.List;

    /// <summary>True for the two views that scroll rather than paginate - what hides the pagination
    /// controls and the per-page picker.</summary>
    public bool ScrollsItself => !ShowCards;

    [ObservableProperty]
    private int _columns = 1;

    // Width of one card slot in the Cards WrapPanel - see UpdateLayoutForWidth for why the cards
    // are no longer in a UniformGrid.
    [ObservableProperty]
    private double _cardWidth = MinimumCardWidth;

    //
    // Whether cards and rows show which mod lists a mod belongs to. Persisted rather than reset per
    // session: it is a display preference, not a filter, which is also why ClearFilters leaves it
    // alone.
    //
    [ObservableProperty]
    private bool _showListBadges = true;

    //
    // No "show everything" option, on purpose: Cards view builds every card up front rather than
    // virtualising, so a hundred-odd of them in one page is work the app does not need to be doing.
    // List view is the cheap way to see the lot.
    //
    // OPEN-12 A8: 48 and 64 too, and Endless (0) - no pager, more cards as the list nears its end.
    public List<int> PageSizeOptions { get; } = [8, DefaultPageSizeValue, 16, 24, 32, 48, 64, Converters.PageSizeLabelConverter.Endless];

    public bool IsEndless => PageSize == Converters.PageSizeLabelConverter.Endless;

    // Cards view's pager, which Endless has no use for.
    public bool ShowPager => ShowCards && !IsEndless;

    private const int EndlessStep = 24;

    private int _endlessShown = EndlessStep;

    // Endless only: the next step of cards, once the list has been scrolled near its end.
    public void LoadMore()
    {
        if (!IsEndless || !ShowCards || Results.Count >= _filtered.Count || _endlessShown >= _filtered.Count) return;

        _endlessShown += EndlessStep;
        _cardsFill.Apply(Results, _filtered.Take(_endlessShown).ToList());
    }

    [ObservableProperty]
    private int _pageSize = DefaultPageSizeValue;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _currentPage = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _totalPages = 1;

    /// <summary>Refills whichever results collection the current view reads from, if it has fallen behind
    /// _filtered. Only the active one is rebuilt; switching views rebuilds the one being switched to, and
    /// only if something has actually changed since that view last drew.</summary>
    // No default for `page`: every caller has to say which page it means. A default of 1 sitting
    // here is what let a caller reset the page without looking like it was doing anything.
    private void RefreshActiveView(int page)
    {
        switch (ViewMode)
        {
            case InstalledViewMode.Groups:
                if (_sectionsDirty) RebuildSections();
                break;
            case InstalledViewMode.List:
                if (_listDirty) RebuildList();
                break;
            default:
                GoToPage(page);
                break;
        }
    }

    private void RebuildList()
    {
        // Synced rather than cleared, for the same reason the card grid is - see Sync.
        _listFill.Apply(ListItems, _filtered);
        _listDirty = false;
    }

    // A fresh scan builds new cards, which default to showing badges - they have to be told.
    private void ApplyBadgeVisibility()
    {
        foreach (var card in _all) card.ShowBadges = ShowListBadges;
    }

    public void UpdateLayoutForWidth(double availableWidth)
    {
        //
        // Ignore a width of zero rather than treating it as "very narrow".
        //
        // Collapsing an element makes WPF raise SizeChanged with 0 x 0, so switching to Groups or
        // List view - which collapses the card grid - reported no width and dropped Columns to 1.
        // Coming back to Cards then showed every card full width, one per row, looking like a list,
        // and it stayed that way until the window was resized and a real width arrived. The last
        // real width is still the right answer for a grid nobody can currently see.
        //
        // Also covers the first Loaded call, which can run before the list has been arranged.
        //
        if (availableWidth <= 0) return;

        Columns = availableWidth switch
        {
            < 700 => 1,
            < 1050 => 2,
            < 1400 => 3,
            _ => 4,
        };

        //
        // The cards sit in a WrapPanel now rather than a UniformGrid, because a UniformGrid forces
        // every cell to the same size - opening one card would have made every card on the page
        // that tall. A WrapPanel lets each card keep its own height, which is the whole point of
        // making them expandable, and ItemWidth is what keeps the columns lining up.
        //
        // Floored, and a pixel taken off first: ItemWidth * Columns landing even a fraction over
        // the available width wraps a card onto the next row, so a four-column layout would
        // silently become three.
        //
        CardWidth = Math.Max(MinimumCardWidth, Math.Floor((availableWidth - 1) / Columns));
    }

    // The keys of whichever mods a view currently has open.
    private bool CanToggleAllExpanded => ShowExpanders && _filtered.Count > 0;

    //
    // Whether the two buttons' work is already done, which is what tints them. Same rule as the
    // view switcher and Multi select: Primary means the page is already in that state.
    //
    // Neither is simply the other's negation - a half-open list is neither, and both sit Secondary.
    //
    public bool AllExpanded => _filtered.Count > 0 && _filtered.All(IsOpen);

    public bool AllCollapsed => _filtered.Count > 0 && !_filtered.Any(IsOpen);

    // Cards and List keep separate expansion state, so "open" means whichever one is on screen -
    // the same choice SetAllExpanded makes when it writes them.
    private bool IsOpen(InstalledModCardViewModel card) =>
        ShowCards ? card.IsCardExpanded : card.IsRowExpanded;

    private void NotifyExpandedState()
    {
        OnPropertyChanged(nameof(AllExpanded));
        OnPropertyChanged(nameof(AllCollapsed));
    }

    [RelayCommand(CanExecute = nameof(CanToggleAllExpanded))]
    private void ExpandAll() => SetAllExpanded(true);

    [RelayCommand(CanExecute = nameof(CanToggleAllExpanded))]
    private void CollapseAll() => SetAllExpanded(false);

    //
    // Opens or closes every mod the filters currently match - not just the page on screen. Paging
    // forward to find half of them shut again would make "expand all" a lie.
    //
    // Only the view being looked at: the two keep their own expansion state, so opening every card
    // shouldn't quietly open every row in List as well.
    //
    private void SetAllExpanded(bool expanded)
    {
        foreach (var card in _filtered)
        {
            if (ShowCards) card.IsCardExpanded = expanded;
            else card.IsRowExpanded = expanded;
        }
    }

    private static HashSet<string> OpenKeys(
        IEnumerable<InstalledModCardViewModel> cards,
        Func<InstalledModCardViewModel, bool> isOpen) =>
        cards.Where(isOpen)
            .Select(m => ModGroupStore.KeyFor(m.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private bool CanGoToPreviousPage() => CurrentPage > 1;

    [RelayCommand(CanExecute = nameof(CanGoToPreviousPage))]
    private void PreviousPage() => GoToPage(CurrentPage - 1);

    private bool CanGoToNextPage() => CurrentPage < TotalPages;

    [RelayCommand(CanExecute = nameof(CanGoToNextPage))]
    private void NextPage() => GoToPage(CurrentPage + 1);

    private void GoToPage(int page)
    {
        var target = Math.Clamp(page, 1, TotalPages);

        // Unlike the other two views this one also depends on which page is asked for, so being
        // clean isn't enough on its own - paging forward and back has to rebuild even though
        // _filtered hasn't moved. TotalPages only changes inside ApplyFilter, which marks this
        // dirty, so a clamp landing somewhere new can't be missed here.
        if (!_cardsDirty && target == CurrentPage) return;

        CurrentPage = target;
        _cardsFill.Apply(Results, IsEndless
            ? _filtered.Take(_endlessShown).ToList()
            : _filtered.Skip((CurrentPage - 1) * PageSize).Take(PageSize).ToList());

        _cardsDirty = false;
    }
}
