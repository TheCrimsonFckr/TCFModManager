using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// One installed mod in the SPT upgrade check on the Dependencies page (OPEN-12 F12). Shown with the
// shared ModStatus icons: ready reads as installed, an update first as an update, nothing yet as no
// compatible version, and unknown as unknown.
//
public sealed class SptUpgradeRowViewModel(SptUpgradeRow row)
{
    public SptUpgradeRow Row { get; } = row;

    public string Name => Row.Name;

    public ModStatus Status => Row.Standing switch
    {
        SptUpgradeStanding.Ready => ModStatus.Installed,
        SptUpgradeStanding.UpdateNeeded => ModStatus.UpdateAvailable,
        SptUpgradeStanding.NotYet => ModStatus.NoCompatibleVersion,
        _ => ModStatus.Unknown,
    };

    public string StatusGlyph => ModStatusDisplay.Glyph(Status);

    public string Detail => Row.Standing switch
    {
        SptUpgradeStanding.Ready => LocalizationService.Text(Strings.Upgrade_ReadyFormat, Row.Installed ?? Row.Version),
        SptUpgradeStanding.UpdateNeeded => LocalizationService.Text(Strings.Upgrade_UpdateFormat, Row.Version),
        SptUpgradeStanding.NotYet => Strings.Upgrade_NotYet,
        _ when Row.ModId is null => Strings.Upgrade_UnknownNotListed,
        _ => Strings.Upgrade_UnknownConstraint,
    };
}
