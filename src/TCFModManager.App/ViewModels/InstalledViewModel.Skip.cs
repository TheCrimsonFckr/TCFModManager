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
}
