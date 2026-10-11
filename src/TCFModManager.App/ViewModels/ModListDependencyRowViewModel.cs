using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// OPEN-22: one mod the list's mods need that the list doesn't name, on the preview - ticked to go
// with the apply. Unticking it replans (the owner listens for the change).
//
public sealed partial class ModListDependencyRowViewModel(ModListDependencyOffer offer, bool ticked, Action changed)
    : ObservableObject
{
    private static string Text(string format, params object?[] values) => LocalizationService.Text(format, values);

    public ModListDependencyOffer Offer { get; } = offer;

    public int ModId => Offer.Dependency.ModId;

    public string Name => Offer.Dependency.Name;

    public string Detail => Text(Strings.ModLists_DepsNeededByFormat,
        string.Join(Strings.Common_ListSeparator, Offer.Dependency.NeededBy));

    public string Need => Offer.Need switch
    {
        ModListDependencyNeed.Install => Text(Strings.ModLists_DepsNeedInstallFormat, Offer.Dependency.Version),
        ModListDependencyNeed.Enable => Strings.ModLists_DepsNeedEnable,
        _ => Strings.ModLists_DepsNeedKeep,
    };

    [ObservableProperty]
    private bool _isTicked = ticked;

    partial void OnIsTickedChanged(bool value) => changed();
}
