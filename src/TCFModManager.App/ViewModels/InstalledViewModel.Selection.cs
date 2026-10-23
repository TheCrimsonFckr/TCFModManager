using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;

namespace TCFModManager.App.ViewModels;

public partial class InstalledViewModel
{
    // ON turns the flat grid's cards into a tick-list so several can be disabled/enabled at once;
    // a card click toggles its tick instead of opening the details dialog while this is on.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionModeOff))]
    private bool _selectionMode;

    /// <summary>Inverse of SelectionMode, for controls that only show while it's off.</summary>
    public bool SelectionModeOff => !SelectionMode;

    public int SelectedCount => _all.Count(m => m.IsSelected);

    public string SelectedCountLabel => Text(Strings.Installed_SelectedFormat, SelectedCount);

    //
    // Everything the current filters match, not just the page on screen - which is why the count is
    // in the label. "Select all" over a paginated list is ambiguous otherwise, and someone who has
    // filtered to "Update available" means all of them, not the first twelve.
    //
    public string SelectAllLabel => Text(Strings.Installed_SelectAllFormat, _filtered.Count);

    //
    // Whether every mod the filters match is already ticked. Tints Select all, the same way
    // SelectionMode tints Multi select and ViewMode tints the view switcher: a Primary button on
    // this page means "this is already the case".
    //
    // It reads over _filtered rather than _all deliberately, because that is what Select all acts
    // on - with a filter narrowing the list, ticking everything it matches is "all of them".
    //
    public bool AllSelected => _filtered.Count > 0 && _filtered.All(m => m.IsSelected);

    private bool HasSelection() => SelectedCount > 0;

    //
    // No CanExecute, deliberately. Gating this on "something is still unticked" turned the button
    // off at exactly the moment AllSelected turns it Primary, and a disabled button takes the
    // theme's disabled fill rather than the accent - so the tint would never once have been seen.
    // Pressing it again when everything is already ticked is a no-op, which is a smaller cost than
    // a state the UI cannot show.
    //
    [RelayCommand]
    private void SelectAll()
    {
        foreach (var mod in _filtered) mod.IsSelected = true;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var mod in _all) mod.IsSelected = false;
        _selectionAnchor = null;
    }

    // ---- OPEN-12 A1: Ctrl-click, Shift-click and Ctrl+A in every view -----------------------------

    // The mod a Shift-click range runs from: the last one ticked or unticked by a click.
    private InstalledModCardViewModel? _selectionAnchor;

    // The mods in the order the current view shows them - what a Shift-click range runs along.
    private List<InstalledModCardViewModel> VisibleOrder() => ViewMode switch
    {
        InstalledViewMode.Groups => [.. Sections.SelectMany(s => s.Items)],
        InstalledViewMode.List => [.. ListItems],
        _ => [.. Results],
    };

    //
    // A click that picks rather than opens: Ctrl-click, Shift-click, or any click while Multi select
    // is on. Switches Multi select on. A range ticks every mod between the anchor and this one, in the
    // order on screen, and adds to what is ticked; anything else toggles this one and moves the anchor.
    //
    public void ClickSelect(InstalledModCardViewModel mod, bool range)
    {
        SelectionMode = true;

        if (range && _selectionAnchor is { } anchor)
        {
            var order = VisibleOrder();
            var from = order.IndexOf(anchor);
            var to = order.IndexOf(mod);

            if (from >= 0 && to >= 0)
            {
                for (var i = Math.Min(from, to); i <= Math.Max(from, to); i++) order[i].IsSelected = true;
                return;
            }
        }

        mod.IsSelected = !mod.IsSelected;
        _selectionAnchor = mod;
    }

    // Ctrl+A: everything the filters match, as Select all does.
    public void SelectAllFromKeyboard()
    {
        SelectionMode = true;
        SelectAll();
    }
}
