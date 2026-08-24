using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

// One dependency in a resolved tree, with its status against the current install and
// whatever's needed to queue it. Rendered as an indented row inside its mod's expander.
public sealed partial class DependencyRow : ObservableObject
{
    public required string Name { get; init; }

    // Nesting level within its tree; 0 is a direct dependency of the mod.
    public int Depth { get; init; }

    public Thickness Indent => new(Depth * 24, 0, 0, 0);

    public required ModStatus Status { get; init; }

    // The version on disk, when there is one.
    public string? InstalledVersion { get; init; }

    // The newest version that satisfies both this dependency and the installed SPT version.
    // Null when the API couldn't resolve one.
    public string? RequiredVersion { get; init; }

    // The catalog listing, when the dependency matched one. Needed to queue an install.
    public Mod? CatalogMod { get; init; }

    // Set once this row has been queued, so the button doesn't invite a second click.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotQueued))]
    private bool _isQueued;

    public bool IsNotQueued => !IsQueued;

    public string Glyph => ModStatusDisplay.Glyph(Status);

    public string StatusText => Status switch
    {
        ModStatus.Installed => InstalledVersion is null ? "installed" : $"installed {InstalledVersion}",
        ModStatus.UpdateAvailable => $"needs {RequiredVersion} - {InstalledVersion ?? "unknown"} installed",
        ModStatus.NotInstalled => RequiredVersion is null ? "not installed" : $"not installed - needs {RequiredVersion}",
        ModStatus.NoCompatibleVersion => "no version compatible with your SPT",
        ModStatus.Disabled => InstalledVersion is null
            ? "installed but disabled - SPT won't load it"
            : $"installed {InstalledVersion} but disabled - SPT won't load it",
        _ => "conflict - two mods need incompatible versions",
    };

    // Whether this row can be queued: something is actually missing or outdated, and there's
    // a resolved version and catalog listing to install.
    public bool CanInstall =>
        Status is ModStatus.NotInstalled or ModStatus.UpdateAvailable
        && CatalogMod is not null
        && !string.IsNullOrWhiteSpace(RequiredVersion);

    public string InstallButtonText => Status == ModStatus.UpdateAvailable ? "Update" : "Install";
}
