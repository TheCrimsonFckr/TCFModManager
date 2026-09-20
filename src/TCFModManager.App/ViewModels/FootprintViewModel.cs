using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
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

public sealed class FootprintSortItem(string key, FootprintSortOption value) : LocalizedViewModel
{
    public FootprintSortOption Value { get; } = value;

    // Read on every get, so a language change relabels the dropdown rather than rebuilding it.
    public string Label => LocalizationService.Get(key);

    public override string ToString() => Label;
}

//
// Where a finding's cost lands: which half of the install, and which resource.
//
// Held as a value rather than as the words on the badge, because both the label and the badge
// colour are read off it. Deriving either from displayed text would break the moment the text is
// translated.
//
public enum FootprintArea
{
    Client,
    ClientCpu,
    ClientGpu,
    ClientMemory,
    ClientStartup,
    Server,
    ServerCpu,
    ServerMemory,
    Disk,
    Unknown,
    Note,
    NotLoaded,
}

//
// One line of a mod's breakdown: where the cost lands, what was found, and what that does and does
// not tell you.
//
// Where is the answer to "client or server, and which resource" - the thing that makes a footprint
// actionable rather than a number. Effect always includes the limit of what was read, because
// every one of these is an opportunity to cost something rather than a measurement of it.
//
public sealed record FootprintFinding(FootprintArea Where, string What, string Effect)
{
    // Read on every get, so the badges relabel with the rest of the page on a language change.
    public string Area => LocalizationService.Get(Where switch
    {
        FootprintArea.ClientCpu => nameof(Strings.Footprint_AreaClientCpu),
        FootprintArea.ClientGpu => nameof(Strings.Footprint_AreaClientGpu),
        FootprintArea.ClientMemory => nameof(Strings.Footprint_AreaClientMemory),
        FootprintArea.ClientStartup => nameof(Strings.Footprint_AreaClientStartup),
        FootprintArea.Server => nameof(Strings.Footprint_AreaServer),
        FootprintArea.ServerCpu => nameof(Strings.Footprint_AreaServerCpu),
        FootprintArea.ServerMemory => nameof(Strings.Footprint_AreaServerMemory),
        FootprintArea.Disk => nameof(Strings.Footprint_AreaDisk),
        FootprintArea.Unknown => nameof(Strings.Footprint_AreaUnknown),
        FootprintArea.Note => nameof(Strings.Footprint_AreaNote),
        FootprintArea.NotLoaded => nameof(Strings.Footprint_AreaNotLoaded),
        _ => nameof(Strings.Footprint_AreaClient),
    });

    //
    // The resource half of the area - "CPU", "GPU", "Memory", "Start-up", "Disk", or empty for the
    // whole-side areas that name no resource.
    //
    // The page colours each badge by this rather than by side. Client or Server is the first word
    // of every label and reads for free; the resource is the half you have to look for, so it is
    // the half worth colouring - and it puts a mod's disk and start-up findings where the eye
    // lands rather than in the middle of a column of identical grey chips.
    //
    // These are match values for the page's triggers, not text anyone reads, so they stay in
    // English whatever the badge says.
    //
    public string Resource => Where switch
    {
        FootprintArea.ClientCpu or FootprintArea.ServerCpu => "CPU",
        FootprintArea.ClientGpu => "GPU",
        FootprintArea.ClientMemory or FootprintArea.ServerMemory => "Memory",
        FootprintArea.ClientStartup => "Start-up",
        FootprintArea.Disk => "Disk",
        _ => string.Empty,
    };
}

//
// One mod's row. This is where counts become sentences - Core deliberately emits flags and numbers
// and no prose, so every word the user reads about a footprint is written here.
//
public sealed partial class ModFootprintRowViewModel(ModFootprintResult result) : LocalizedViewModel
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

    public string Title => Version is null ? Name : Text(Strings.Footprint_TitleFormat, Name, Version);

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
                    ? Strings.Footprint_DetailPatchOne
                    : Text(Strings.Footprint_DetailPatchManyFormat, Footprint.PatchClassCount));
            }

            if (Footprint.PerFrameTypeCount > 0)
            {
                parts.Add(Footprint.PerFrameTypeCount == 1
                    ? Strings.Footprint_DetailComponentOne
                    : Text(Strings.Footprint_DetailComponentManyFormat, Footprint.PerFrameTypeCount));
            }

            parts.Add(Size(Footprint.TotalBytes));

            if (IsDisabled) parts.Add(Strings.Footprint_DetailDisabled);

            return string.Join(Strings.Footprint_FactSeparator, parts);
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
                    FootprintArea.Client,
                    print.PerFrameTypeCount == 1
                        ? Strings.Footprint_ComponentsOne
                        : Text(Strings.Footprint_ComponentsManyFormat, print.PerFrameTypeCount),
                    Strings.Footprint_ComponentsEffect));
            }

            if (print.FrameUpdateTypeCount > 0)
            {
                findings.Add(new FootprintFinding(
                    FootprintArea.ClientCpu,
                    Of(print.FrameUpdateTypeCount,
                        Strings.Footprint_FrameUpdateOne,
                        Strings.Footprint_FrameUpdateManyFormat),
                    Strings.Footprint_FrameUpdateEffect));
            }

            if (print.PhysicsTypeCount > 0)
            {
                findings.Add(new FootprintFinding(
                    FootprintArea.ClientCpu,
                    Of(print.PhysicsTypeCount,
                        Strings.Footprint_PhysicsOne,
                        Strings.Footprint_PhysicsManyFormat),
                    Strings.Footprint_PhysicsEffect));
            }

            if (print.GuiTypeCount > 0)
            {
                findings.Add(new FootprintFinding(
                    FootprintArea.ClientCpu,
                    Of(print.GuiTypeCount, Strings.Footprint_GuiOne, Strings.Footprint_GuiManyFormat),
                    Strings.Footprint_GuiEffect));
            }

            if (print.ImageEffectTypeCount > 0)
            {
                findings.Add(new FootprintFinding(
                    FootprintArea.ClientGpu,
                    Of(print.ImageEffectTypeCount,
                        Strings.Footprint_ImageEffectOne,
                        Strings.Footprint_ImageEffectManyFormat),
                    Strings.Footprint_ImageEffectEffect));
            }

            if (print.CameraCallbackTypeCount > 0)
            {
                findings.Add(new FootprintFinding(
                    FootprintArea.ClientCpu,
                    Of(print.CameraCallbackTypeCount,
                        Strings.Footprint_CameraOne,
                        Strings.Footprint_CameraManyFormat),
                    Strings.Footprint_CameraEffect));
            }

            if (print.PatchClassCount > 0)
            {
                findings.Add(new FootprintFinding(
                    FootprintArea.ClientCpu,
                    print.PatchClassCount == 1
                        ? Strings.Footprint_PatchesOne
                        : Text(Strings.Footprint_PatchesManyFormat, print.PatchClassCount),
                    Strings.Footprint_PatchesEffect));
            }

            if (print.BundleCount > 0)
            {
                findings.Add(new FootprintFinding(
                    FootprintArea.ClientMemory,
                    Text(Strings.Footprint_BundlesFormat, Size(print.BundleBytes), Files(print.BundleCount)),
                    Strings.Footprint_BundlesEffect));
            }

            if (print.HasPatcher)
            {
                findings.Add(new FootprintFinding(
                    FootprintArea.ClientStartup,
                    Strings.Footprint_Patcher,
                    Strings.Footprint_PatcherEffect));
            }

            if (print.HasServerHalf)
            {
                findings.Add(new FootprintFinding(
                    FootprintArea.Server,
                    Strings.Footprint_ServerHalf,
                    Strings.Footprint_ServerHalfEffect));
            }

            if (print.ServerPatchClassCount > 0)
            {
                findings.Add(new FootprintFinding(
                    FootprintArea.ServerCpu,
                    Of(print.ServerPatchClassCount,
                        Strings.Footprint_ServerPatchesOne,
                        Strings.Footprint_ServerPatchesManyFormat),
                    Strings.Footprint_ServerPatchesEffect));
            }

            if (print.ServerBundleCount > 0)
            {
                findings.Add(new FootprintFinding(
                    FootprintArea.ServerMemory,
                    Text(
                        Strings.Footprint_BundlesFormat,
                        Size(print.ServerBundleBytes),
                        Files(print.ServerBundleCount)),
                    Strings.Footprint_ServerBundlesEffect));
            }

            findings.Add(new FootprintFinding(
                FootprintArea.Disk,
                Text(Strings.Footprint_DiskFormat, Size(print.TotalBytes), Files(print.FileCount)),
                string.Join(
                    Strings.Common_SentenceSeparator,
                    print.AssemblyCount switch
                    {
                        0 => Strings.Footprint_DiskAssembliesNone,
                        1 => Strings.Footprint_DiskAssembliesOne,
                        _ => Text(Strings.Footprint_DiskAssembliesManyFormat, print.AssemblyCount),
                    },
                    Strings.Footprint_DiskEffectTail)));

            if (print.UnreadableAssemblyCount > 0)
            {
                findings.Add(new FootprintFinding(
                    FootprintArea.Unknown,
                    Text(
                        Strings.Footprint_UnreadableFormat,
                        print.UnreadableAssemblyCount,
                        print.AssemblyCount),
                    Strings.Footprint_UnreadableEffect));
            }

            if (print.PerFrameMethodCount > print.PerFrameMethods.Count)
            {
                findings.Add(new FootprintFinding(
                    FootprintArea.Note,
                    Text(
                        Strings.Footprint_ShowingNamesFormat,
                        print.PerFrameMethods.Count,
                        print.PerFrameMethodCount),
                    Strings.Footprint_ShowingNamesEffect));
            }

            if (print.ServerPerFrameTypeCount > 0)
            {
                findings.Add(new FootprintFinding(
                    FootprintArea.Note,
                    print.ServerPerFrameTypeCount == 1
                        ? Strings.Footprint_ServerComponentsOne
                        : Text(
                            Strings.Footprint_ServerComponentsManyFormat,
                            print.ServerPerFrameTypeCount),
                    Strings.Footprint_ServerComponentsEffect));
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
                    FootprintArea.Client,
                    Strings.Footprint_NothingFound,
                    Strings.Footprint_NothingFoundEffect));
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
                    FootprintArea.NotLoaded,
                    Strings.Footprint_NotLoadedWhat,
                    Strings.Footprint_NotLoadedEffect));
            }

            return findings;
        }
    }

    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    private static string Files(int count) =>
        count == 1 ? Strings.Footprint_FilesOne : Text(Strings.Footprint_FilesManyFormat, count);

    // A whole sentence per count rather than a shared "n of those" with a verb phrase dropped in:
    // the verb has to agree with the count, and in English it already did not.
    private static string Of(int count, string one, string manyFormat) =>
        count == 1 ? one : Text(manyFormat, count);

    //
    // The actual method names found, so nobody has to take the counts above on trust - this is the
    // raw evidence they were derived from.
    //
    public string PerFrameTooltip => Footprint.PerFrameMethods.Count == 0
        ? string.Empty
        : Text(
            Footprint.PerFrameMethodCount > Footprint.PerFrameMethods.Count
                ? Strings.Footprint_CallbacksTruncatedFormat
                : Strings.Footprint_CallbacksFormat,
            string.Join(Strings.Common_ListSeparator, Footprint.PerFrameMethods));

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

public sealed partial class FootprintViewModel : LocalizedViewModel
{
    private readonly ModFootprintService _footprints = new();
    private List<ModFootprintRowViewModel> _all = [];

    public ObservableCollection<ModFootprintRowViewModel> Rows { get; } = [];

    // Name first, and the default: the page opens as a list of what is installed rather than as an
    // ordering of it. The count-based sorts are there for someone who went looking for them.
    public IReadOnlyList<FootprintSortItem> SortOptions { get; } =
    [
        new(nameof(Strings.Sort_FootprintName), FootprintSortOption.NameAscending),
        new(nameof(Strings.Sort_FootprintMostPatches), FootprintSortOption.MostPatches),
        new(nameof(Strings.Sort_FootprintMostComponents), FootprintSortOption.MostComponents),
        new(nameof(Strings.Sort_FootprintLargestOnDisk), FootprintSortOption.LargestOnDisk),
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
        Status = force ? Strings.Footprint_StatusRereading : Strings.Footprint_StatusReading;

        try
        {
            var results = await _footprints.ReadAsync(force);
            _all = [.. results.Select(r => new ModFootprintRowViewModel(r))];
            ApplySort();

            Status = _all.Count switch
            {
                0 => Strings.Footprint_StatusNone,
                1 => Strings.Footprint_StatusOne,
                _ => LocalizationService.Text(Strings.Footprint_StatusManyFormat, _all.Count),
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
