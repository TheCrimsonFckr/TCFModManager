using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;

namespace TCFModManager.App.ViewModels;

public sealed partial class ConfigModRow : LocalizedViewModel
{
    public ConfigModRow(string title, IReadOnlyList<ConfigEntryViewModel> files, string? version, string? author, string? guid)
    {
        Title = title;
        Files = files;
        Tabs = files.Select(f => new ConfigFileTab(f)).ToList();
        Details = string.Join(" · ", new[] { version, author, guid }.Where(s => !string.IsNullOrWhiteSpace(s)));
        Section = files.MinBy(f => f.Section.Rank)!.Section;
        LastFile = files[0];
    }

    public string Title { get; }

    public IReadOnlyList<ConfigEntryViewModel> Files { get; }

    public IReadOnlyList<ConfigFileTab> Tabs { get; }

    public bool HasTabs => Tabs.Count > 1;

    public string Details { get; }

    public bool HasDetails => Details.Length > 0;

    public ConfigSectionHeader Section { get; }

    public ConfigEntryViewModel LastFile { get; set; }

    public string Glyph => Files[0].Glyph;

    public string Subtitle => Files.Count == 1
        ? Files[0].Subtitle
        : string.Join(", ", Files.Select(f => f.FileName));

    public string? Badge => Files.Any(f => f.Entry.IsModDisabled) ? Strings.Configs_TagModDisabled : null;

    public bool IsShippedDefaultOnly => Files.All(f => f.IsShippedDefault);

    public double RowOpacity => IsShippedDefaultOnly ? 0.6 : 1.0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChanges))]
    private int _changedCount;

    public bool HasChanges => ChangedCount > 0;

    [ObservableProperty]
    private bool _isDirty;

    public bool MatchesName(string term) =>
        Title.Contains(term, StringComparison.OrdinalIgnoreCase)
        || Files.Any(f => f.MatchesSearch(term));
}

public sealed partial class ConfigFileTab(ConfigEntryViewModel entry) : ObservableObject
{
    public ConfigEntryViewModel Entry { get; } = entry;

    public string Label => Entry.FileName;

    public string Glyph => Entry.Glyph;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isDirty;
}
