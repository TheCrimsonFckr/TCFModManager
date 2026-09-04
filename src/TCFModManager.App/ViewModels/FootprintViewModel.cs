using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;

namespace TCFModManager.App.ViewModels;

// Mod footprint page's "Sort by" dropdown.
//
// Every ordering here is by a counted fact. There is deliberately no "heaviest first" - that would
// rank other people's mods by a verdict this app has no standing to make, and a list sorted that
// way reads as a leaderboard however carefully the rest of the page is worded.
//
public enum FootprintSortOption
{
    NameAscending,
    LargestOnDisk,
    MostPatches,
    MostComponents,
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
public sealed record FootprintFinding(string Area, string What, string Effect)
{
    //
    // The resource half of Area - "CPU", "GPU", "Memory", "Start-up", "Disk", or empty for the
    // whole-side areas ("Client", "Server") that name no resource.
    //
    // The page colours each badge by this rather than by side. Client or Server is the first word
    // of every label and reads for free; the resource is the half you have to look for, so it is
    // the half worth colouring - and it puts a mod's disk and start-up findings where the eye
    // lands rather than in the middle of a column of identical grey chips.
    //
    // Derived here rather than passed in, so the thirteen places that build a finding are
    // untouched and an Area and its colour cannot drift apart.
    //
    public string Resource
    {
        get
        {
            var separator = Area.IndexOf('·');

            if (separator >= 0) return Area[(separator + 1)..].Trim();

            // "Disk" is a resource on its own; "Client" and "Server" name a side and no resource.
            return Area.Trim() == "Disk" ? "Disk" : string.Empty;
        }
    }
}

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

    //
    // ModFootprint still derives a Level, a Score and Signals, and they are still tested and
    // calibrated - but NOTHING ON THIS PAGE SHOWS THEM, deliberately.
    //
    // The counts are facts about files that anyone can check. A level is a verdict on somebody
    // else's work, and it is the one thing on the page that survives being screenshotted without
    // its explanation - "this app says SAIN is Heavy" travels, the caveats do not. This app has no
    // standing to grade other people's mods, so it reports what it found and leaves the reader to
    // draw the conclusion. Chris's call, 2026-09-02.
    //
    // If it is ever surfaced again, the wording has to survive being read alone.
    //

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
                    ? "1 component with an engine callback"
                    : $"{Footprint.PerFrameTypeCount} components with engine callbacks");
            }

            parts.Add(Size(Footprint.TotalBytes));

            if (IsDisabled) parts.Add("disabled");

            return string.Join("  ·  ", parts);
        }
    }

    //
    // The expanded breakdown.
    //
    // EVERY LINE HERE HAS TO BE TRUE OF THE FILES, NOT OF A PLAYING SESSION. The analyzer reads
    // declarations; it cannot see whether a component is ever instantiated, whether a patch sits on
    // a hot method, or how much any of it does. So these say "declares" and "ships", never "runs",
    // and each one names the limit of what it establishes. If a future edit makes one of these
    // sound like a measurement, it is wrong however plausible it reads.
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
                    "Client",
                    print.PerFrameTypeCount == 1
                        ? "Declares 1 component the engine would call on a timer"
                        : $"Declares {print.PerFrameTypeCount} components the engine would call on a timer",
                    "This is what the mod contains, not what happens when you play: a declared component "
                    + "only runs if an instance of it is created, attached to an active object and "
                    + "enabled, and none of that is visible in the files. The lines below say which kind "
                    + "of callback each one uses - a component declaring more than one appears in more "
                    + "than one line, so those can add up to more than this total."));
            }

            if (print.FrameUpdateTypeCount > 0)
            {
                findings.Add(new FootprintFinding(
                    "Client · CPU",
                    Of(print.FrameUpdateTypeCount, "declare a per-frame update method"),
                    "Unity calls Update or LateUpdate on a component once a frame while it is alive and "
                    + "enabled. This is the kind of work that tracks frame rate most directly - though "
                    + "how much it costs depends entirely on what the code inside does."));
            }

            if (print.PhysicsTypeCount > 0)
            {
                findings.Add(new FootprintFinding(
                    "Client · CPU",
                    Of(print.PhysicsTypeCount, "declare a physics-step method"),
                    "FixedUpdate runs on the physics timestep - fifty times a second by default - and not "
                    + "once a frame. It can run several times in one frame or not at all in another, so "
                    + "its cost does not track your frame rate the way an Update does."));
            }

            if (print.GuiTypeCount > 0)
            {
                findings.Add(new FootprintFinding(
                    "Client · CPU",
                    Of(print.GuiTypeCount, "draw with immediate-mode GUI"),
                    "Unity calls OnGUI more than once a frame - a layout pass, then once per input event - "
                    + "and immediate-mode code typically allocates on each call, which adds garbage "
                    + "collection pressure. What it actually draws decides how much that matters."));
            }

            if (print.ImageEffectTypeCount > 0)
            {
                findings.Add(new FootprintFinding(
                    "Client · GPU",
                    Of(print.ImageEffectTypeCount, "are full-screen image effects"),
                    "OnRenderImage takes the rendered frame and writes a new one, for each camera the "
                    + "component is attached to. That work is done by the graphics card and scales with "
                    + "resolution rather than with processor speed."));
            }

            if (print.CameraCallbackTypeCount > 0)
            {
                findings.Add(new FootprintFinding(
                    "Client · CPU",
                    Of(print.CameraCallbackTypeCount, "hook a camera's render"),
                    "OnPreRender and OnPostRender run either side of a camera drawing, once per camera "
                    + "per frame. They run on the processor rather than the graphics card, whatever the "
                    + "code inside them may go on to set up."));
            }

            if (print.PatchClassCount > 0)
            {
                findings.Add(new FootprintFinding(
                    "Client · CPU",
                    print.PatchClassCount == 1
                        ? "Ships 1 patch class"
                        : $"Ships {print.PatchClassCount} patch classes",
                    "A patch class alters one of the game's own methods - usually a single one, though "
                    + "one class can target several, so this is a floor rather than a total. Where a "
                    + "patch sits is what decides its cost, and that is exactly what reading the files "
                    + "cannot establish: a patch on a method the game calls thousands of times a second "
                    + "is indistinguishable here from one on a menu button. Read this as how far the mod "
                    + "reaches, not as what it costs."));
            }

            if (print.BundleCount > 0)
            {
                findings.Add(new FootprintFinding(
                    "Client · Memory",
                    $"{Size(print.BundleBytes)} in {Files(print.BundleCount)} with a .bundle extension",
                    "Asset bundles hold textures, models, sounds and shaders, and take up memory once "
                    + "loaded - video memory too, for anything the graphics card needs. These are "
                    + "identified by their file extension and measured on disk; when and whether the mod "
                    + "loads them is its own business."));
            }

            if (print.HasPatcher)
            {
                findings.Add(new FootprintFinding(
                    "Client · Start-up",
                    "Ships a BepInEx patcher",
                    "BepInEx runs patchers in its preloader, before the game's own assemblies are loaded. "
                    + "That is a start-up cost, and it is not running once you are in a raid."));
            }

            if (print.HasServerHalf)
            {
                findings.Add(new FootprintFinding(
                    "Server",
                    "Installs files under user\\mods",
                    "These load in the SPT server process rather than in the game. Their cost falls on "
                    + "server start-up and raid loading; they are not in the client's frame loop, so they "
                    + "cannot cost you frame rate directly."));
            }

            if (print.ServerPatchClassCount > 0)
            {
                findings.Add(new FootprintFinding(
                    "Server · CPU",
                    print.ServerPatchClassCount == 1
                        ? "1 of those is a patch class"
                        : $"{print.ServerPatchClassCount} of those are patch classes",
                    "Found in the server half, so this alters the server rather than the game. It is "
                    + "counted separately from the client figure above and does not affect this mod's "
                    + "level, which describes the client only."));
            }

            if (print.ServerBundleCount > 0)
            {
                findings.Add(new FootprintFinding(
                    "Server · Memory",
                    $"{Size(print.ServerBundleBytes)} in {Files(print.ServerBundleCount)} with a .bundle extension",
                    "These sit in the server half, so they are the server's memory rather than the "
                    + "game's."));
            }

            findings.Add(new FootprintFinding(
                "Disk",
                $"{Size(print.TotalBytes)} across {Files(print.FileCount)}",
                (print.AssemblyCount switch
                {
                    0 => "None of them are managed assemblies, so there was no compiled code here to read.",
                    1 => "1 of them is a managed assembly, which is what everything above was read from.",
                    _ => $"{print.AssemblyCount} of them are managed assemblies, which is what everything above was read from.",
                })
                + " This is everything in the mod's own folders, so anything installed into them - an "
                + "addon that has no folder of its own, for instance - is counted here too."));

            if (print.UnreadableAssemblyCount > 0)
            {
                findings.Add(new FootprintFinding(
                    "Unknown",
                    $"{print.UnreadableAssemblyCount} of {print.AssemblyCount} assemblies could not be read",
                    "Every count above is a floor rather than a total. An assembly can be unreadable "
                    + "because it is obfuscated, packed, or built in a way this doesn't parse."));
            }

            if (print.PerFrameMethodCount > print.PerFrameMethods.Count)
            {
                findings.Add(new FootprintFinding(
                    "Note",
                    $"Showing {print.PerFrameMethods.Count} of {print.PerFrameMethodCount} callback names",
                    "The counts above are complete; only the list of method names below is shortened."));
            }

            if (print.ServerPerFrameTypeCount > 0)
            {
                findings.Add(new FootprintFinding(
                    "Note",
                    print.ServerPerFrameTypeCount == 1
                        ? "1 type in the server half looks like a game component"
                        : $"{print.ServerPerFrameTypeCount} types in the server half look like game components",
                    "Server code has no reason to use them, and the check matches on type name alone, so "
                    + "this is more likely a coincidence of naming than real work. It is reported rather "
                    + "than counted as client work either way."));
            }

            //
            // Only when there was something readable to find nothing in. Without the unreadable
            // check this would tell someone a fully obfuscated mod contains no code, in the same
            // breath as the row above saying nothing could be read - a flat contradiction, and the
            // half that sounds most confident is the one that is wrong.
            //
            if (print is { PerFrameTypeCount: 0, PatchClassCount: 0, HasServerHalf: false, UnreadableAssemblyCount: 0 })
            {
                findings.Insert(0, new FootprintFinding(
                    "Client",
                    "Nothing that patches the game or runs on a timer",
                    "No patch classes, and no components declaring an engine callback. Whatever this mod "
                    + "does, it does when something else calls it - or it is data and assets rather than "
                    + "code."));
            }

            //
            // First, above everything: a disabled mod's files are still on disk and still readable,
            // and every line below describes what they would do if they were loaded. None of it is
            // happening while the mod is set aside, and a reader who misses that draws exactly the
            // wrong conclusion about their install.
            //
            if (IsDisabled)
            {
                findings.Insert(0, new FootprintFinding(
                    "Not loaded",
                    "This mod is currently disabled",
                    "Its files are still on disk, which is why they can still be read, but SPT is not "
                    + "loading any of them. Everything below describes what this mod would do once you "
                    + "enable it again."));
            }

            return findings;
        }
    }

    private static string Files(int count) => count == 1 ? "1 file" : $"{count} files";

    private static string Of(int count, string suffix) =>
        count == 1 ? $"1 of those {suffix}" : $"{count} of those {suffix}";

    //
    // The actual method names found, so nobody has to take the counts above on trust - this is the
    // raw evidence they were derived from.
    //
    public string PerFrameTooltip => Footprint.PerFrameMethods.Count == 0
        ? string.Empty
        : "Engine callbacks found: " + string.Join(", ", Footprint.PerFrameMethods)
          + (Footprint.PerFrameMethodCount > Footprint.PerFrameMethods.Count ? ", ..." : string.Empty);

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

    // Name first, and the default: the page opens as a list of what is installed rather than as an
    // ordering of it. The count-based sorts are there for someone who went looking for them.
    public IReadOnlyList<FootprintSortItem> SortOptions { get; } =
    [
        new("Name (A-Z)", FootprintSortOption.NameAscending),
        new("Most patch classes", FootprintSortOption.MostPatches),
        new("Most components", FootprintSortOption.MostComponents),
        new("Largest on disk", FootprintSortOption.LargestOnDisk),
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
        var option = SelectedSort?.Value ?? FootprintSortOption.NameAscending;

        // Name is the tie-break on every ordering, so a page of mods with equal counts doesn't
        // shuffle between visits.
        IEnumerable<ModFootprintRowViewModel> sorted = option switch
        {
            FootprintSortOption.LargestOnDisk =>
                _all.OrderByDescending(r => r.Footprint.TotalBytes)
                    .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase),
            FootprintSortOption.MostPatches =>
                _all.OrderByDescending(r => r.Footprint.PatchClassCount)
                    .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase),
            FootprintSortOption.MostComponents =>
                _all.OrderByDescending(r => r.Footprint.PerFrameTypeCount)
                    .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase),
            _ =>
                _all.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase),
        };

        Rows.Clear();
        foreach (var row in sorted) Rows.Add(row);
        HasRows = Rows.Count > 0;
    }
}
