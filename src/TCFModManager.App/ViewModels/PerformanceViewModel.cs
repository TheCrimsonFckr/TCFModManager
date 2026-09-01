using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;

namespace TCFModManager.App.ViewModels;

// Performance page's "Sort by" dropdown.
public enum FootprintSortOption
{
    // Heaviest first - the order the page exists to show.
    Footprint,
    NameAscending,
    LargestOnDisk,
    MostPatches,
}

public sealed record FootprintSortItem(string Label, FootprintSortOption Value)
{
    public override string ToString() => Label;
}

//
// One mod's row. This is where a ModFootprintSignal becomes a sentence - Core deliberately emits
// flags and counts and no prose, so every word the user reads about a footprint is written here.
//
public sealed class ModFootprintRowViewModel(ModFootprintResult result)
{
    public ModFootprint Footprint { get; } = result.Footprint;

    public string Name { get; } = result.Name;

    public string? Version { get; } = result.Version;

    public bool IsDisabled { get; } = result.IsDisabled;

    public ModFootprintLevel Level => Footprint.Level;

    public string LevelText => Level switch
    {
        ModFootprintLevel.Heavy => "Heavy",
        ModFootprintLevel.Moderate => "Moderate",
        ModFootprintLevel.Light => "Light",
        _ => "Unreadable",
    };

    public string Title => Version is null ? Name : $"{Name}  {Version}";

    //
    // The counts, in the order they matter. Kept to what was actually found - a mod with no patches
    // and no per-frame code says so by omission rather than by a row of zeroes.
    //
    public string Detail
    {
        get
        {
            var parts = new List<string>();

            if (Footprint.PatchClassCount > 0)
            {
                parts.Add(Footprint.PatchClassCount == 1
                    ? "1 patch class"
                    : $"{Footprint.PatchClassCount} patch classes");
            }

            if (Footprint.PerFrameTypeCount > 0)
            {
                parts.Add(Footprint.PerFrameTypeCount == 1
                    ? "1 component runs every frame"
                    : $"{Footprint.PerFrameTypeCount} components run every frame");
            }

            parts.Add(Size(Footprint.TotalBytes));

            if (IsDisabled) parts.Add("disabled");

            return string.Join("  ·  ", parts);
        }
    }

    //
    // The incidental facts, which never move the level on their own but are what someone chasing
    // load time or memory is actually looking for.
    //
    public string Notes
    {
        get
        {
            var signals = Footprint.Signals;
            var notes = new List<string>();

            if (signals.HasFlag(ModFootprintSignal.LargeBundles))
            {
                notes.Add($"{Size(Footprint.BundleBytes)} of asset bundles, which cost memory rather than frame time");
            }

            if (signals.HasFlag(ModFootprintSignal.Patcher))
            {
                notes.Add("Ships a patcher, which runs before the game's own assemblies load");
            }

            if (signals.HasFlag(ModFootprintSignal.ServerHalf))
            {
                notes.Add("Has a server half, which affects load times rather than frame rate");
            }

            if (signals.HasFlag(ModFootprintSignal.Unreadable))
            {
                notes.Add($"{Footprint.UnreadableAssemblyCount} of {Footprint.AssemblyCount} assemblies could not be read, so these counts are a floor");
            }

            return string.Join(". ", notes);
        }
    }

    public bool HasNotes => Notes.Length > 0;

    public string PerFrameTooltip => Footprint.PerFrameMethods.Count == 0
        ? string.Empty
        : string.Join("\n", Footprint.PerFrameMethods);

    // Duplicated from DownloadQueueItemViewModel rather than shared: pulling it out would mean
    // editing a file this feature otherwise doesn't touch, for three lines.
    private static string Size(double bytes) => bytes switch
    {
        >= 1024d * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.#} GB",
        >= 1024d * 1024 => $"{bytes / (1024d * 1024):0.#} MB",
        >= 1024d => $"{bytes / 1024d:0.#} KB",
        _ => $"{bytes:0} B",
    };
}

public sealed partial class PerformanceViewModel : ObservableObject
{
    private readonly ModFootprintService _footprints = new();
    private List<ModFootprintRowViewModel> _all = [];

    public ObservableCollection<ModFootprintRowViewModel> Rows { get; } = [];

    public IReadOnlyList<FootprintSortItem> SortOptions { get; } =
    [
        new("Footprint (heaviest first)", FootprintSortOption.Footprint),
        new("Most patch classes", FootprintSortOption.MostPatches),
        new("Largest on disk", FootprintSortOption.LargestOnDisk),
        new("Name (A-Z)", FootprintSortOption.NameAscending),
    ];

    [ObservableProperty]
    private FootprintSortItem? _selectedSort;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    // Bound to Rescan's IsEnabled - the page needs the negation and a binding cannot invert a bool.
    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private bool _hasRows;

    public PerformanceViewModel()
    {
        SelectedSort = SortOptions[0];
    }

    partial void OnSelectedSortChanged(FootprintSortItem? value) => ApplySort();

    //
    // Called from the page's Loaded handler, so returning to the page picks up anything installed
    // since - but off the cache, so the common case costs a directory walk rather than a re-read.
    //
    public async Task RefreshAsync(bool force = false)
    {
        if (IsBusy) return;

        IsBusy = true;
        Status = force ? "Re-reading every mod..." : "Reading installed mods...";

        try
        {
            var results = await _footprints.ReadAsync(force);
            _all = [.. results.Select(r => new ModFootprintRowViewModel(r))];
            ApplySort();

            Status = _all.Count switch
            {
                0 => "No installed mods found. Set your SPT install folder on the Options page.",
                1 => "1 mod read from what it ships on disk.",
                _ => $"{_all.Count} mods read from what they ship on disk.",
            };
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task Rescan() => RefreshAsync(force: true);

    private void ApplySort()
    {
        var option = SelectedSort?.Value ?? FootprintSortOption.Footprint;

        // Name is the tie-break on every ordering, so a page of mods that score the same doesn't
        // shuffle between visits.
        IEnumerable<ModFootprintRowViewModel> sorted = option switch
        {
            FootprintSortOption.NameAscending =>
                _all.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase),
            FootprintSortOption.LargestOnDisk =>
                _all.OrderByDescending(r => r.Footprint.TotalBytes)
                    .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase),
            FootprintSortOption.MostPatches =>
                _all.OrderByDescending(r => r.Footprint.PatchClassCount)
                    .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase),
            _ =>
                _all.OrderByDescending(r => r.Footprint.Score)
                    .ThenByDescending(r => r.Footprint.PatchClassCount)
                    .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase),
        };

        Rows.Clear();
        foreach (var row in sorted) Rows.Add(row);
        HasRows = Rows.Count > 0;
    }
}
