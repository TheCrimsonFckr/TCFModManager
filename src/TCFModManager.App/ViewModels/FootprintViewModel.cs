using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;

namespace TCFModManager.App.ViewModels;

// Mod footprint page's "Sort by" dropdown.
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
// One line of a mod's breakdown: where the cost lands, what was found, and what that does and does
// not tell you.
//
// Area is the answer to "client or server, and which resource" - the thing that makes a footprint
// actionable rather than a number. Effect always includes the limit of what was read, because
// every one of these is an opportunity to cost something rather than a measurement of it.
//
public sealed record FootprintFinding(string Area, string What, string Effect);

//
// One mod's row. This is where counts become sentences - Core deliberately emits flags and numbers
// and no prose, so every word the user reads about a footprint is written here.
//
public sealed partial class ModFootprintRowViewModel(ModFootprintResult result) : ObservableObject
{
    public ModFootprint Footprint { get; } = result.Footprint;

    public string Name { get; } = result.Name;

    public string? Version { get; } = result.Version;

    public bool IsDisabled { get; } = result.IsDisabled;

    // Two-way bound to the expander, the same as the Dependencies page's trees.
    [ObservableProperty]
    private bool _isExpanded;

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
    // The collapsed line. Kept to what was actually found - a mod with no patches and no per-frame
    // code says so by omission rather than by a row of zeroes.
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
    // The expanded breakdown, in the order someone chasing a problem would want it: what runs every
    // frame, then what it patches, then memory, then the things that cost time somewhere other than
    // the frame loop.
    //
    public IReadOnlyList<FootprintFinding> Findings
    {
        get
        {
            var findings = new List<FootprintFinding>();
            var print = Footprint;

            if (print.PerFrameTypeCount > 0)
            {
                findings.Add(new FootprintFinding(
                    "Client · CPU",
                    print.PerFrameTypeCount == 1
                        ? "1 component runs code every frame"
                        : $"{print.PerFrameTypeCount} components run code every frame",
                    "This runs on the main thread on every frame the component is alive, which is the "
                    + "one kind of work that can affect frame rate whatever else the mod does. How much "
                    + "it costs depends on what the code inside does, which reading the files can't tell you."));
            }

            if (print.RenderHookTypeCount > 0)
            {
                findings.Add(new FootprintFinding(
                    "Client · GPU",
                    print.RenderHookTypeCount == 1
                        ? "1 of those hooks a camera render callback"
                        : $"{print.RenderHookTypeCount} of those hook a camera render callback",
                    "A camera callback such as OnRenderImage runs a pass over the rendered frame, once "
                    + "per camera per frame. That is GPU work, so it tends to show up as a cost that "
                    + "gets worse at higher resolutions rather than one that tracks your CPU."));
            }

            if (print.GuiTypeCount > 0)
            {
                findings.Add(new FootprintFinding(
                    "Client · CPU",
                    print.GuiTypeCount == 1
                        ? "1 of those draws with immediate-mode GUI"
                        : $"{print.GuiTypeCount} of those draw with immediate-mode GUI",
                    "OnGUI runs several times per frame - once for layout and again for each event - "
                    + "and allocates on every pass, so it costs more than its component count suggests "
                    + "and adds garbage collection pressure."));
            }

            if (print.PatchClassCount > 0)
            {
                findings.Add(new FootprintFinding(
                    "Client · CPU",
                    print.PatchClassCount == 1
                        ? "Patches 1 place in the game"
                        : $"Patches {print.PatchClassCount} places in the game",
                    "Each patch inserts a call into the mod's code whenever the method it patches runs. "
                    + "A patch on something the game calls thousands of times a second and a patch on a "
                    + "menu button look identical from the outside, and this can't tell them apart - so "
                    + "read this as how far the mod reaches, not as what it costs."));
            }

            if (print.BundleCount > 0)
            {
                findings.Add(new FootprintFinding(
                    "Client · Memory",
                    $"{Size(print.BundleBytes)} across {(print.BundleCount == 1 ? "1 asset bundle" : $"{print.BundleCount} asset bundles")}",
                    "Asset bundles are loaded into RAM when the mod uses them, and any textures, meshes "
                    + "or shaders inside them also take video memory. This is a memory and load-time "
                    + "cost rather than a per-frame one."));
            }

            if (print.HasPatcher)
            {
                findings.Add(new FootprintFinding(
                    "Client · Start-up",
                    "Ships a BepInEx patcher",
                    "A patcher runs in the preloader, before the game's own assemblies are loaded. It "
                    + "costs start-up time and can affect what other mods see, but it is not running "
                    + "once you are in a raid."));
            }

            if (print.HasServerHalf)
            {
                findings.Add(new FootprintFinding(
                    "Server",
                    "Has a server half under user\\mods",
                    "This runs in the SPT server process, not the game. It affects how long the server "
                    + "takes to start and how long a raid takes to load, and it is not in the client's "
                    + "frame loop at all - so it cannot cost you frame rate directly."));
            }

            findings.Add(new FootprintFinding(
                "Disk",
                $"{Size(print.TotalBytes)} across {(print.FileCount == 1 ? "1 file" : $"{print.FileCount} files")}",
                print.AssemblyCount == 1
                    ? "1 of them is a managed assembly, which is what everything above was read from."
                    : $"{print.AssemblyCount} of them are managed assemblies, which is what everything above was read from."));

            if (print.UnreadableAssemblyCount > 0)
            {
                findings.Add(new FootprintFinding(
                    "Unknown",
                    $"{print.UnreadableAssemblyCount} of {print.AssemblyCount} assemblies could not be read",
                    "Every count above is a floor rather than a total. An assembly can be unreadable "
                    + "because it is obfuscated, packed, or built in a way this can't parse."));
            }

            if (print is { PerFrameTypeCount: 0, PatchClassCount: 0, HasServerHalf: false })
            {
                findings.Insert(0, new FootprintFinding(
                    "Client",
                    "No code that runs on its own",
                    "Nothing here patches the game or runs every frame. Whatever this mod does, it does "
                    + "when something else calls it - or it is data and assets rather than code."));
            }

            return findings;
        }
    }

    public string PerFrameTooltip => Footprint.PerFrameMethods.Count == 0
        ? string.Empty
        : string.Join("\n", Footprint.PerFrameMethods);

    public bool HasPerFrameTooltip => Footprint.PerFrameMethods.Count > 0;

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

public sealed partial class FootprintViewModel : ObservableObject
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

    public FootprintViewModel()
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
