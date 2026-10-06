using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;

namespace TCFModManager.App.ViewModels;

// One checkable entry in Browse's SPT version filter (a section of Filters and view options). Label is the major.minor shown to the user (e.g. "3.11"); Value is what's passed to SptVersionMatcher when filtering.
public partial class SptVersionOption(string label, string value, bool isSelected, bool isInstalledLine) : LocalizedViewModel
{
    public string Label { get; } = label;

    public string Value { get; } = value;

    // True for the option matching the detected SPT install - the app's own default, which Clear
    // filters puts back and the pill compares against. isSelected is what the page opened at, which a
    // saved default can change.
    public bool IsInstalledLine { get; } = isInstalledLine;

    [ObservableProperty]
    private bool _isSelected = isSelected;
}
