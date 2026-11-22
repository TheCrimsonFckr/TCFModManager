using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

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

    [RelayCommand]
    private void EditCardLayouts()
    {
        if (CardLayoutEditorWindow.Edit(CardLayoutPage.Browse, CardLayout.Id, SampleCardForLayouts()) is { } picked) UseCardLayout(picked.Id);
    }

    public ModCardViewModel? SampleCardForLayouts() =>
        Results.OrderByDescending(c => (c.Thumbnail is null ? 0 : 2) + (c.Endorsements is null ? 0 : 1)).FirstOrDefault();

    private bool CardLayoutIsNotDefault() =>
        AppServices.CardLayouts.Resolve(CardLayoutPage.Browse, _defaults?.CardLayout).Id != CardLayout.Id;

    [RelayCommand(CanExecute = nameof(CardLayoutIsNotDefault))]
    private void MakeCardLayoutDefault()
    {
        var service = new SettingsService();
        var settings = service.Load();

        settings.BrowseDefaults ??= new BrowsePageDefaults();
        settings.BrowseDefaults.CardLayout = CardLayout.Id?.ToString() ?? "default";
        _defaults = settings.BrowseDefaults;

        service.Save(settings);
        MakeCardLayoutDefaultCommand.NotifyCanExecuteChanged();

        StatusMessage = LocalizationService.Text(Strings.CardLayout_MadeDefaultFormat, CardLayout.Name);
        AppLog.Info("Browse", $"made card layout \"{CardLayout.Name}\" the page default");
    }

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
        MakeCardLayoutDefaultCommand.NotifyCanExecuteChanged();
        FilterPins?.RefreshLabels();
    }
}
