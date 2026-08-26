using CommunityToolkit.Mvvm.ComponentModel;
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
public sealed partial class ModPageGateViewModel : ObservableObject
{
    private const string SkipNotice =
        "\n\nYou are skipping this mod's page, and its release notes with it - it is recommended you "
        + "read them first. They are where the author says what changed, what a version needs and "
        + "what it breaks.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallToolTip))]
    [NotifyPropertyChangedFor(nameof(RedownloadToolTip))]
    [NotifyPropertyChangedFor(nameof(UpdateToolTip))]
    [NotifyPropertyChangedFor(nameof(SettingToolTip))]
    private bool _isSkipping;

    public string InstallToolTip => "Install this mod." + (IsSkipping ? SkipNotice : string.Empty);

    public string RedownloadToolTip => "Redownload this mod." + (IsSkipping ? SkipNotice : string.Empty);

    public string UpdateToolTip => "Update this mod." + (IsSkipping ? SkipNotice : string.Empty);

    // The Options page's own switch, kept here so there is one description of what the setting
    // currently means rather than two that can drift apart.
    public string SettingToolTip => IsSkipping
        ? "You are skipping each mod's release notes. It is recommended you read them - they are "
          + "where an author says what changed, what a version needs and what it breaks. "
          + "This app's own updates always ask regardless."
        : "Each mod's page opens before anything downloads, so its release notes and install "
          + "instructions are in front of you first.";

    // Re-reads the setting. Called at startup and whenever the Options page changes it.
    public void Refresh() => IsSkipping = new SettingsService().Load().SkipModPageConfirmation;
}
