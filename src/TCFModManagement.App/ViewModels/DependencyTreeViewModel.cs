using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManagement.Core.Models;
using TCFModManagement.Core.Services;

namespace TCFModManagement.App.ViewModels;

// One installed mod's resolved dependency tree - the header row of the Dependencies page's
// expander, plus its flattened, indented dependency rows.
public sealed partial class DependencyTreeViewModel : ObservableObject
{
    public required string ModName { get; init; }

    // The installed version the tree was resolved for.
    public required string ModVersion { get; init; }

    public required Mod Mod { get; init; }

    public ObservableCollection<DependencyRow> Rows { get; } = [];

    // Starts expanded when something needs attention, so problems are visible without clicking.
    [ObservableProperty]
    private bool _isExpanded;

    // The most severe status among this mod's dependencies, used for the header icon.
    public ModStatus Worst { get; private set; } = ModStatus.Installed;

    public bool NeedsAttention => Worst != ModStatus.Installed;

    public string Glyph => ModStatusDisplay.Glyph(Worst);

    public string Summary
    {
        get
        {
            var missing = Rows.Count(r => r.Status == ModStatus.NotInstalled);
            var outdated = Rows.Count(r => r.Status == ModStatus.UpdateAvailable);
            var conflicts = Rows.Count(r => r.Status == ModStatus.Conflict);
            var unresolved = Rows.Count(r => r.Status == ModStatus.NoCompatibleVersion);

            var parts = new List<string>();
            if (missing > 0) parts.Add($"{missing} missing");
            if (outdated > 0) parts.Add($"{outdated} outdated");
            if (conflicts > 0) parts.Add($"{conflicts} conflicting");
            if (unresolved > 0) parts.Add($"{unresolved} unresolved");

            return parts.Count == 0
                ? $"{Rows.Count} dependenc{(Rows.Count == 1 ? "y" : "ies")}, all satisfied"
                : string.Join(", ", parts);
        }
    }

    // Recomputes Worst/Summary and the initial expansion. Call once Rows is populated, and
    // again after a row's status changes.
    public void Refresh()
    {
        Worst = DependencyStatusResolver.Worst(Rows.Select(r => r.Status));

        IsExpanded = NeedsAttention;

        OnPropertyChanged(nameof(Worst));
        OnPropertyChanged(nameof(NeedsAttention));
        OnPropertyChanged(nameof(Glyph));
        OnPropertyChanged(nameof(Summary));
    }

}
