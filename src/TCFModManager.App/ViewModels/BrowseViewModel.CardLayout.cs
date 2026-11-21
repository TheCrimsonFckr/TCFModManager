using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.Core.Models;

namespace TCFModManager.App.ViewModels;

public partial class BrowseViewModel
{
    private double _lastLayoutWidth;

    private bool _syncingCardLayouts;

    public ObservableCollection<CardLayoutView> CardLayoutChoices { get; } = [];

    [ObservableProperty]
    private CardLayoutView _cardLayout = CardLayoutView.Default(CardLayoutPage.Browse);

    [ObservableProperty]
    private CardLayoutView? _selectedCardLayout;

    private void InitCardLayouts()
    {
        RefreshCardLayoutChoices(AppServices.CardLayouts.Resolve(CardLayoutPage.Browse, _defaults?.CardLayout).Id);
        AppServices.CardLayouts.Changed += (_, _) => RefreshCardLayoutChoices(CardLayout.Id);
    }

    public void UseCardLayout(Guid? id) => RefreshCardLayoutChoices(id);

    public ModCardViewModel? SampleCardForLayouts() =>
        Results.OrderByDescending(c => (c.Thumbnail is null ? 0 : 2) + (c.Endorsements is null ? 0 : 1)).FirstOrDefault();

    private void RefreshCardLayoutChoices(Guid? keep)
    {
        _syncingCardLayouts = true;
        try
        {
            CardLayoutChoices.Clear();
            foreach (var choice in AppServices.CardLayouts.ChoicesFor(CardLayoutPage.Browse)) CardLayoutChoices.Add(choice);

            var pick = CardLayoutChoices.FirstOrDefault(c => c.Id == keep) ?? CardLayoutChoices[0];
            SelectedCardLayout = pick;
            CardLayout = pick;
        }
        finally
        {
            _syncingCardLayouts = false;
        }
    }

    private void ApplySavedCardLayout(string? id)
    {
        if (id is null) return;
        RefreshCardLayoutChoices(AppServices.CardLayouts.Resolve(CardLayoutPage.Browse, id).Id);
    }

    partial void OnSelectedCardLayoutChanged(CardLayoutView? value)
    {
        if (_syncingCardLayouts || value is null) return;
        CardLayout = value;
    }

    partial void OnCardLayoutChanged(CardLayoutView value)
    {
        if (_lastLayoutWidth > 0) UpdateLayoutForWidth(_lastLayoutWidth);
    }
}
