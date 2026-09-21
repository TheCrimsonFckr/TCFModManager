using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// Whether the "read the mod's page first" gate is currently switched off, and the wording every
// install button uses to say so.
//
// A shared singleton rather than a property on each mod's card view model: the setting is one global
// choice, so a hundred Browse cards shouldn't each be reading settings.json to answer the same
// question - and when it changes, every button needs to re-read it at once.
//
public sealed partial class ModPageGateViewModel : LocalizedViewModel
{
    // Read on every get, so a language change relabels the tooltips with the rest of the app.
    private static string SkipNotice => $"\n\n{Strings.ModPageGate_SkipNotice}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallToolTip))]
    [NotifyPropertyChangedFor(nameof(RedownloadToolTip))]
    [NotifyPropertyChangedFor(nameof(UpdateToolTip))]
    [NotifyPropertyChangedFor(nameof(SettingToolTip))]
    private bool _isSkipping;

    public string InstallToolTip => Strings.ModPageGate_Install + (IsSkipping ? SkipNotice : string.Empty);

    public string RedownloadToolTip =>
        Strings.ModPageGate_Redownload + (IsSkipping ? SkipNotice : string.Empty);

    public string UpdateToolTip => Strings.ModPageGate_Update + (IsSkipping ? SkipNotice : string.Empty);

    // The Options page's own switch, kept here so there is one description of what the setting
    // currently means rather than two that can drift apart.
    public string SettingToolTip => IsSkipping
        ? Strings.Options_ModPagesToolTipSkipping
        : Strings.Options_ModPagesToolTipAsking;

    // Re-reads the setting. Called at startup and whenever the Options page changes it.
    public void Refresh() => IsSkipping = new SettingsService().Load().SkipModPageConfirmation;
}
