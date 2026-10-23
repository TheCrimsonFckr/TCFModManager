using TCFModManager.App.Services;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

// One row of the update dialog's "Your addons with …" panel (OPEN-32 D5).
public sealed class AddonImpactRowViewModel(AddonImpactItem item)
{
    public string Name => item.Addon.Name;

    public string? InstalledVersion => item.Addon.InstalledVersion;

    public string Verdict => AddonImpactCheck.Verdict(item.Result);

    public bool IsWarning => item.Result.Verdict != AddonImpactVerdict.Unaffected;
}
