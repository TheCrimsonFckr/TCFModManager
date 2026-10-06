using CommunityToolkit.Mvvm.Input;

namespace TCFModManager.App.ViewModels;

//
// One filter that is narrowing the list, shown as a pill above the results on Installed and Browse
// (Chris, 2026-10-06, after Steam's workshop search): "Category: Weapons", or a Show only tick box by
// its own name. Its X puts that one filter back to no restriction, without opening the panel.
//
public sealed class ActiveFilterPill(string label, Action remove)
{
    public string Label { get; } = label;

    public IRelayCommand RemoveCommand { get; } = new RelayCommand(remove);
}
