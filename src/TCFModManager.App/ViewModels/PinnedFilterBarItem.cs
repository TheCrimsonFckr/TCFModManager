using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Input;

namespace TCFModManager.App.ViewModels;

//
// One pinned Filters panel section as a dropdown in the row under the search bar (OPEN-24): its
// button reads Label ("Category: Weapons") and its popup shows the section's own body, the same
// template the panel uses, over Owner - the page view model.
//
public sealed partial class PinnedFilterBarItem(string section, object owner, ICommand unpin) : ObservableObject
{
    public string Section { get; } = section;

    public object Owner { get; } = owner;

    public ICommand UnpinCommand { get; } = unpin;

    [ObservableProperty]
    private string _label = string.Empty;

    // False while the page's current view has no use for the section (Installed's Sort groups
    // outside Groups view, Per page outside Cards), the same as its panel section.
    [ObservableProperty]
    private bool _isShown = true;
}
