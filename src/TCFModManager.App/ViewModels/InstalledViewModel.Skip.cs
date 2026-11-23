using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;

namespace TCFModManager.App.ViewModels;

public partial class InstalledViewModel
{
    public static string? SkipMenuLabel(InstalledModCardViewModel mod) =>
        mod is { UpdateAvailable: true, ModId: not null } && (mod.UpdateVersion ?? mod.LatestPublishedVersion) is { } version
            ? Text(Strings.Installed_SkipUpdateFormat, version)
            : null;

    [RelayCommand]
    private async Task SkipUpdateAsync(InstalledModCardViewModel? mod)
    {
        if (mod?.ModId is not { } id || (mod.UpdateVersion ?? mod.LatestPublishedVersion) is not { } version) return;

        AppServices.SkippedUpdates.Skip(mod.IsAddon, id, version, mod.Name);
        await ScanAsync();
        StatusMessage = Text(Strings.Installed_SkipUpdateDoneFormat, mod.DisplayTitle, version);
    }

    [RelayCommand]
    private async Task StopSkippingAsync(InstalledModCardViewModel? mod)
    {
        if (mod?.ModId is not { } id || mod.SkippedVersion is null) return;

        AppServices.SkippedUpdates.StopSkipping(mod.IsAddon, id, mod.Name);
        await ScanAsync();
        StatusMessage = Text(Strings.Installed_StoppedSkippingFormat, mod.DisplayTitle);
    }

    // OPEN-64 Q5: offer this mod's updates straight away, or wait for them again.
    public static string? WaitMenuLabel(InstalledModCardViewModel mod) =>
        mod.ModId is not { } id || AppServices.UpdateWait.Days == 0
            ? null
            : AppServices.UpdateWait.IsExempt(mod.IsAddon, id)
                ? Strings.Installed_WaitForThisMod
                : Strings.Installed_DontWaitForThisMod;

    [RelayCommand]
    private void ToggleUpdateWait(InstalledModCardViewModel? mod)
    {
        if (mod?.ModId is not { } id) return;

        var exempt = !AppServices.UpdateWait.IsExempt(mod.IsAddon, id);
        AppServices.UpdateWait.SetExempt(mod.IsAddon, id, exempt, mod.Name);
        StatusMessage = Text(exempt ? Strings.Installed_DontWaitDoneFormat : Strings.Installed_WaitAgainDoneFormat, mod.DisplayTitle);
    }
}
