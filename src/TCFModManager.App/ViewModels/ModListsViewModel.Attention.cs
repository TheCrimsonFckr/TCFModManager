using System.ComponentModel;
using System.Windows.Data;
using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

// OPEN-44: the Saved lists panel in sections, with a dot on a list that needs a look.
public partial class ModListsViewModel
{
    private ICollectionView? _listsView;

    public ICollectionView ListsView
    {
        get
        {
            if (_listsView is not null) return _listsView;

            var view = new CollectionViewSource { Source = Lists }.View;
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ModListRowViewModel.SectionLabel)));
            view.SortDescriptions.Add(new SortDescription(nameof(ModListRowViewModel.SectionOrder), ListSortDirection.Ascending));
            view.SortDescriptions.Add(new SortDescription(nameof(ModListRowViewModel.UpdatedAt), ListSortDirection.Descending));
            return _listsView = view;
        }
    }

    private readonly Dictionary<Guid, string> _attention = [];

    private string? AttentionAtRefresh(Core.Models.ModList list, bool published)
    {
        var known = _attention.GetValueOrDefault(list.Id);
        if (!published || known?.Contains(Strings.ModLists_AttentionChangedSinceServed) == true) return known;

        return UnpublishedSince(list) is null
            ? known
            : known is null ? Strings.ModLists_AttentionChangedSinceServed : Strings.ModLists_AttentionChangedSinceServed + Environment.NewLine + known;
    }

    // Worked out once this install has been checked against the selected list, never half-way through.
    private void UpdateAttention()
    {
        if (Selected is not { } row || !ReferenceEquals(_standingList, row.List)) return;

        var reasons = new List<string>();

        if (row.IsPublished && _unpublishedSince is not null) reasons.Add(Strings.ModLists_AttentionChangedSinceServed);
        if (_behind is not null) reasons.Add(Strings.ModLists_AttentionNewerHere);
        if (Entries.Any(e => e.AttentionRank < 2)) reasons.Add(Strings.ModLists_AttentionWarnings);
        if (row.IsFollowed && _standingPlan is { } plan && !(plan.IsNoOp && !plan.Manual.Any()))
            reasons.Add(Strings.ModLists_AttentionNotMatching);

        var text = reasons.Count == 0 ? null : string.Join(Environment.NewLine, reasons);

        if (text is null) _attention.Remove(row.Id);
        else _attention[row.Id] = text;

        row.Attention = text;
    }
}
