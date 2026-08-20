using CommunityToolkit.Mvvm.ComponentModel;

namespace TCFModManagement.App.ViewModels;

// One checkable entry in Browse's SPT version filter dropdown. Label is the major.minor shown to the user (e.g. "3.11"); Value is what's passed to SptVersionMatcher when filtering.
public partial class SptVersionOption(string label, string value, bool isSelected) : ObservableObject
{
    public string Label { get; } = label;

    public string Value { get; } = value;

    // True for the option matching the detected SPT install; used to restore selection on "Clear filters".
    public bool IsDefault { get; } = isSelected;

    [ObservableProperty]
    private bool _isSelected = isSelected;
}
