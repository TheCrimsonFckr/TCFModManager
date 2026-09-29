using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Help;
using TCFModManager.App.Localization;
using TCFModManager.Core.Services;
using Wpf.Ui.Controls;

namespace TCFModManager.App.ViewModels;

//
// The Help page: every section of HelpCatalog, filtered by the search box.
//
// The search matches how-to TITLES only, in the language on screen. Steps are left out on purpose:
// nearly every step names a page, so searching them would turn "Installed" into half the page.
//
public partial class HelpViewModel : LocalizedViewModel
{
    public HelpViewModel()
    {
        Sections = new ObservableCollection<HelpSectionViewModel>(
            HelpCatalog.Sections.Select(s => new HelpSectionViewModel(s)));

        // Getting started opens expanded, so a first visit lands on steps rather than a list of titles.
        foreach (var topic in Sections[0].Topics) topic.IsExpanded = true;

        AppLanguage.Changed += (_, _) => ApplyFilter();
    }

    public ObservableCollection<HelpSectionViewModel> Sections { get; }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _hasMatches = true;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        var compare = CultureInfo.CurrentCulture.CompareInfo;
        var any = false;

        foreach (var section in Sections)
        {
            var sectionHasMatch = false;

            foreach (var topic in section.Topics)
            {
                topic.IsVisible = query.Length == 0
                    || compare.IndexOf(topic.Title, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

                // A search opens what it found, so the answer is on screen without a second click.
                if (query.Length > 0) topic.IsExpanded = topic.IsVisible;

                sectionHasMatch |= topic.IsVisible;
            }

            section.IsVisible = sectionHasMatch;
            any |= sectionHasMatch;
        }

        HasMatches = any;
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private void OpenGuide()
    {
        try
        {
            Process.Start(new ProcessStartInfo(SelfMod.ModPageUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn("Help", $"couldn't open the guide: {ex.Message}");
        }
    }

    // The section the "?" opens on for this page; Getting started when the page has none of its own.
    internal HelpSectionViewModel SectionFor(Type? pageType) =>
        Sections.FirstOrDefault(s => pageType is not null && s.PageType == pageType) ?? Sections[0];
}

public partial class HelpSectionViewModel : LocalizedViewModel
{
    private readonly HelpSection _section;

    internal HelpSectionViewModel(HelpSection section)
    {
        _section = section;
        Topics = section.Topics.Select(t => new HelpTopicViewModel(t)).ToList();
    }

    public string Id => _section.Id;

    public string Title => _section.Title();

    public SymbolRegular Icon => _section.Icon;

    internal Type? PageType => _section.PageType;

    public IReadOnlyList<HelpTopicViewModel> Topics { get; }

    [ObservableProperty]
    private bool _isVisible = true;
}

public partial class HelpTopicViewModel : LocalizedViewModel
{
    private readonly HelpTopic _topic;

    internal HelpTopicViewModel(HelpTopic topic)
    {
        _topic = topic;
        Steps = topic.Steps.Select((s, i) => new HelpStepViewModel(s, i + 1)).ToList();
        Note = topic.Note is null ? null : new HelpStepViewModel(topic.Note, 0);
    }

    public string Id => _topic.Id;

    public string Title => _topic.Title();

    public IReadOnlyList<HelpStepViewModel> Steps { get; }

    public HelpStepViewModel? Note { get; }

    public bool HasNote => Note is not null;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private bool _isExpanded;
}

public class HelpStepViewModel : LocalizedViewModel
{
    private readonly HelpStep _step;

    internal HelpStepViewModel(HelpStep step, int number)
    {
        _step = step;
        Number = number;
    }

    public int Number { get; }

    public string NumberLabel => LocalizationService.Text(Strings.Help_StepNumberFormat, Number);

    // Read by the HelpInlines attached property, which turns them into Runs with the labels bold.
    internal IReadOnlyList<HelpRun> Runs => HelpText.Runs(_step);

    // Same text without the bold, for the screen reader and for anyone copying a step.
    public string PlainText => HelpText.Plain(_step);
}
