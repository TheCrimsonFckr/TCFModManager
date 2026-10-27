using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

// Display wrapper for the Installed page's card grid, merging a mod's client and server entries and matching it against the sp-mod.com catalog.
public sealed partial class InstalledModCardViewModel : LocalizedViewModel
{
    public required string Name { get; init; }

    // The version to show as "installed". The install manifest's version when this app
    // installed the mod, since that is what was actually fetched; otherwise whatever the files on
    // disk report. See InstalledVersionDetail for the file-reported versions when they differ.
    public string? InstalledVersion { get; init; }

    //
    // The versions the files themselves report, shown only when they disagree with each
    // other or with the version recorded at install time. Null when there is nothing to explain.
    //
    // Held as a key and the values it takes rather than as finished text: everything else on this
    // card is read on every get, so composing the line when the card is built would leave it in
    // whatever language the last scan ran in. A null value stands for a version that could not be
    // read, which is the only thing a null means in any of these lines.
    //
    public (string Key, object?[] Values)? VersionDetail { get; init; }

    // Wraps whatever VersionDetail says rather than replacing it, so it is carried separately.
    public bool IsVersionManuallyConfirmed { get; init; }

    // True when InstalledVersion was read off the mod's files rather than an install record. A file
    // can't carry a label (-beta, -hotfix), so it is compared with published versions by its numbers.
    public bool InstalledVersionFromFiles { get; init; }

    public string? InstalledVersionDetail
    {
        get
        {
            var detail = VersionDetail is { } held
                ? Text(
                    LocalizationService.Get(held.Key),
                    [.. held.Values.Select(v => v ?? Strings.Common_Unknown)])
                : null;

            if (!IsVersionManuallyConfirmed) return detail;

            return detail is null
                ? Strings.Installed_DetailManuallyConfirmed
                : Text(Strings.Installed_DetailManuallyConfirmedFormat, detail);
        }
    }

    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    // When this app last installed or updated the mod, from its record; for a mod installed by hand,
    // the earliest folder date across the merged entries (see InstallDate).
    public DateTimeOffset? InstalledAt { get; init; }

    // True when any half of this mod is a client one - a BepInEx plugin, a patcher, or both.
    public bool HasClient { get; init; }

    // True specifically for a BepInEx\plugins half, as opposed to a patcher one. HasClient without
    // this is a patcher standing on its own.
    public bool HasPlugin { get; init; }

    // True when any half of this mod sits in BepInEx\patchers. Usually alongside a plugin; on its
    // own for a patcher that couldn't be tied back to a mod.
    public bool HasPatcher { get; init; }

    public bool HasServer { get; init; }

    //
    // Which halves of the install this mod occupies. A patcher is called out rather than folded
    // into "Client", since on its own it's the difference between a card that reads as a mod whose
    // identity couldn't be worked out and one that reads as what it actually is.
    //
    public string TargetSummary
    {
        get
        {
            //
            // Every combination is its own whole phrase rather than the three words joined with a
            // plus. A language can put them in another order or join them another way, and "Client
            // only" is not "Client" with a word appended to it.
            //
            return (HasPlugin, HasPatcher, HasServer) switch
            {
                (true, false, false) => Strings.Installed_TargetClientOnly,
                (false, true, false) => Strings.Installed_TargetPatcherOnly,
                (false, false, true) => Strings.Installed_TargetServerOnly,
                (true, true, false) => Strings.Installed_TargetClientPatcher,
                (true, false, true) => Strings.Installed_TargetClientServer,
                (false, true, true) => Strings.Installed_TargetPatcherServer,
                (true, true, true) => Strings.Installed_TargetClientPatcherServer,
                _ => Strings.Installed_TargetUnknown,
            };
        }
    }

    // The newest version published on sp-mod.com that runs on the installed SPT, else the newest
    // overall when none does. Null when the catalog hasn't loaded yet or nothing matched.
    public string? LatestPublishedVersion { get; init; }

    // The version an update would install - the newest release that runs on the installed SPT,
    // which is not always the newest published. Null when no update is available.
    public string? UpdateVersion { get; init; }

    //
    // OPEN-12 F11: sp-mod holds back the release this card would otherwise offer, because it would
    // break another installed mod. UpdateVersion is then the newest release those mods all accept,
    // when that is still newer than the one installed, and no update otherwise - see HeldBackUpdates.
    //
    public HeldBackResolution? HeldBack { get; init; }

    public string? HeldBackNote => HeldBack is { } held ? HeldBackUpdates.Describe(held) : null;

    //
    // OPEN-13: whether the installed version is made for the installed SPT, and from where that is
    // known. Null when nothing could say. Built with the card.
    //
    public SptFit? SptFit { get; init; }

    public string? InstalledSptVersion { get; init; }

    // R5: Fika runs on this install and sp-mod marks the installed release as not working with it.
    public bool IsFikaIncompatible { get; init; }

    // sp-mod marks the installed release as not working with Fika, whether or not Fika runs here.
    public bool IsReleaseFikaIncompatible { get; init; }

    // Matched on sp-mod, but the installed release has no Fika answer there.
    public bool IsReleaseFikaUnknown { get; init; }

    // Either reason to warn, whether or not the user dismissed it.
    public bool HasSptWarning => SptFit is { Fits: false } || IsFikaIncompatible;

    //
    // The user said this version works on this SPT after all (Chris, 2026-10-12) - per mod, installed
    // version and SPT, so the warning comes back when either changes. SptWarningKey is the stored key.
    //
    public bool IsSptWarningDismissed { get; init; }

    public string SptWarningKey { get; init; } = "";

    public bool CanDismissSptWarning => HasSptWarning && !IsSptWarningDismissed;

    public string? SptDismissedNote => HasSptWarning && IsSptWarningDismissed
        ? Text(Strings.Installed_SptDismissedFormat, InstalledVersion, InstalledSptVersion)
        : null;

    // The Not for this SPT filter (D5) - either reason, unless dismissed.
    public bool IsNotForThisSpt => HasSptWarning && !IsSptWarningDismissed;

    //
    // The opened card's compatibility line, and the tooltip's when the version isn't for this SPT:
    // "Your version is for SPT 4.0.x, not 4.1.6. 3.0.6 runs on it." R2: sp-mod decides, and the
    // mod's own files are named when they say otherwise.
    //
    public string? SptNote
    {
        get
        {
            var parts = new List<string>();

            if (SptFit is { Fits: false } fit && !IsSptWarningDismissed)
            {
                parts.Add(fit.ForConstraint is { } range
                    ? Text(Strings.Installed_SptNotForFormat, SptVersionRangeFormatter.Format(range) ?? range, InstalledSptVersion)
                    : Text(Strings.Installed_SptNotForPlainFormat, InstalledSptVersion));

                parts.Add(fit.FittingVersion is not { } fitting
                    ? Strings.Installed_SptNoneFits
                    : fit.FittingIsNewer
                        ? Text(Strings.Installed_SptNewerFitsFormat, fitting)
                        : Text(Strings.Installed_SptOlderFitsFormat, fitting));
            }

            if (SptFit is { DeclaredDisagrees: true, DeclaredConstraint: { } declared })
                parts.Add(Text(Strings.Installed_SptDeclaredDisagreesFormat, SptVersionRangeFormatter.Format(declared) ?? declared));

            if (IsFikaIncompatible && !IsSptWarningDismissed) parts.Add(Text(Strings.Installed_FikaIncompatibleFormat, InstalledVersion));

            return parts.Count == 0 ? null : string.Join(" ", parts);
        }
    }

    //
    // OPEN-12 A2: the Update button on the card header and List row - the same mods Update selected
    // would update (InstalledViewModel.IsUpdatable): an enabled, catalogued mod or addon with an
    // update (OPEN-32 D1).
    //
    public bool CanQuickUpdate =>
        UpdateAvailable == true && !IsDisabled && ModId is not null && LatestPublishedVersion is not null;

    public string QuickUpdateToolTip => Text(Strings.Installed_UpdateOneToolTipFormat, UpdateVersion ?? LatestPublishedVersion);

    // The matched sp-mod.com listing's UpdatedAt. Null under the same conditions as LatestPublishedVersion.
    public DateTimeOffset? LatestUpdatedAt { get; init; }

    //
    // What the "Latest published" line shows. A patcher standing on its own gets its own wording:
    // not every patcher in BepInEx\patchers is an sp-mod.com mod at all - some are general BepInEx
    // utilities from elsewhere that a mod bundles alongside itself (FixPluginTypesSerialization is
    // the common one) - so for those "not found on sp-mod.com" reads as a failed lookup when
    // nothing actually went wrong. Everything else keeps the wording it had.
    //
    public string LatestPublishedText => (LatestPublishedVersion, ModId, HasPlugin || HasServer) switch
    {
        ({ } version, _, _) => version,
        (null, _, _) when IsAddon => IsAddonListed ? Strings.Common_Unknown : Strings.Installed_LatestAddonUnlisted,
        (null, not null, _) => Strings.Common_Unknown,
        (null, null, false) when HasPatcher => Strings.Installed_LatestPatcherNotListed,
        _ => Strings.Installed_LatestNotFound,
    };

    // The matched sp-mod.com listing's actual display Name, often different from the installed
    // folder/package Name. Null when nothing matched.
    public string? MatchedModName { get; init; }

    //
    // The name the mod gives itself in its own DLL - an SPT 4.x server mod's declared Name, or its
    // [BepInPlugin] name. Only ever set on a card nothing in the catalog matched: a matched card
    // keeps the catalog's name (R2 of the server mod metadata design).
    //
    public string? DeclaredName { get; init; }

    // The title the card leads with: the real sp-mod.com display name when there is one, then the
    // name the mod declares for itself, otherwise the folder/package Name.
    public string DisplayTitle => MatchedModName ?? DeclaredName ?? Name;

    //
    // The hover preview (OPEN-12 A6, App.xaml ModHoverPreview): the catalog listing's picture and
    // teaser when this mod is matched to one, looked up only when the preview opens.
    //
    private Mod? CatalogListing => ModId is { } id && !IsAddon
        ? AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == id)
        : null;

    public string? PreviewThumbnail => CatalogListing?.Thumbnail;
    public string PreviewTitle => DisplayTitle;
    public string? PreviewAuthor => Author;
    public string? PreviewTeaser => CatalogListing?.Teaser is { Length: > 0 } teaser ? teaser : null;
    public int? PreviewDownloads => CatalogListing?.Downloads;
    public string? PreviewSpt => null;

    // The raw installed folder/package name, shown as a secondary line under DisplayTitle only
    // when the title came from somewhere else and differs from the folder name.
    public string? FolderNameIfDifferent =>
        (MatchedModName ?? DeclaredName) is { } title && !string.Equals(title, Name, StringComparison.OrdinalIgnoreCase)
            ? Name
            : null;

    // The matched sp-mod.com listing's GUID. Null when nothing matched.
    public string? Guid { get; init; }

    // The matched sp-mod.com listing's primary author. Null when nothing matched. Backs the
    // "@author" search syntax in InstalledViewModel's search box.
    public string? Author { get; init; }

    // The author's sp-mod id, for their page and Follow (OPEN-12 A4); null for a mod not matched to
    // a listing.
    public int? AuthorId { get; init; }

    // The matched sp-mod.com listing's category title, for the Installed page's Category filter.
    // Null when nothing matched, or for an addon - addons carry no category of their own.
    public string? CategoryTag { get; init; }

    //
    // How many addons this mod has published. Answered from the cached addon catalog during the
    // same pass that builds the card, so the Installed page's "Has addons" filter needs no lookup.
    //
    public int AddonCount { get; init; }

    public bool HasAddons => AddonCount > 0;

    //
    // True when this mod declares a dependency on another installed mod. Filled in after the cards
    // are built, from the dependency graph InstalledViewModel builds off the same scan - the graph
    // reads BepInEx's own metadata, so unlike Browse's badge this answer is complete and offline.
    //
    [ObservableProperty]
    private bool _hasDependencies;

    // True when the matched catalog listing is flagged Fika compatible.
    public bool IsFikaCompatible { get; init; }

    // True when the matched catalog listing is flagged as containing ads.
    public bool ContainsAds { get; init; }

    // True when a newer, SPT-compatible version than InstalledVersion is published. False when
    // there's no newer or no compatible newer version. Null when it can't be determined (no catalog match,
    // unparsable version, or no installed SPT version detected).
    public bool? UpdateAvailable { get; init; }

    // Client half's folder (or, for a loose top-level DLL, the DLL itself). Null when there's no client half.
    public string? ClientFolderPath { get; init; }

    // Where the "Client folder" button goes - see FolderToOpen.
    public string? ClientFolderLink => FolderToOpen(ClientFolderPath);

    // Server half's folder. Null when there's no server half.
    public string? ServerFolderPath { get; init; }

    // Where the "Server folder" button goes - see FolderToOpen.
    public string? ServerFolderLink => FolderToOpen(ServerFolderPath);

    //
    // The folder a path should actually open. A mod that ships as a single loose DLL has no folder
    // of its own, and the scanner names it after the file - so its recorded path is
    // "...\BepInEx\plugins\AOReplacer.dll", and opening that would ask Windows to run the DLL
    // rather than show where it lives. Anything ending in .dll opens its parent instead.
    //
    // Matched on the .dll suffix specifically, NOT on "does it have an extension": plenty of real
    // mod folders are named with dots in them (Tyfon.AutoDeposit, IcyClawz.MunitionsExpert,
    // RaiRai.ColorConverterAPI), and Path.HasExtension would send every one of those to its parent.
    //
    private static string? FolderToOpen(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return path;

        // Cut at the last separator by hand rather than going through Path.GetDirectoryName, which
        // does not treat "\" as a separator anywhere but Windows - the app only ever runs there,
        // but these view models are exercised headless on Linux and the Path version silently
        // returned an empty string for every Windows path it was given.
        var cut = path.LastIndexOfAny(['\\', '/']);
        return cut > 0 ? path[..cut] : null;
    }

    // The client/server halves' folder (or loose-DLL) *names*, as InstalledModScanner reports them -
    // what a manual version override records as this mod's Folders, so a later rescan still ties the
    // override back to this card. Null when there's no corresponding half.
    public string? ClientFolderName { get; init; }
    public string? ServerFolderName { get; init; }

    // A single path for this card for existing callers/diagnostics; client wins when both exist.
    public required string FolderPath { get; init; }

    // The matched sp-mod.com listing's numeric id. Null when nothing matched. Holds an ADDON id
    // when IsAddon is true - the two are separate sequences, so never compare it to a mod id
    // without checking IsAddon first.
    public int? ModId { get; init; }

    //
    // True when this card is an addon rather than a mod. Only ever set from an install record this
    // app wrote: addons carry no GUID and their ids don't overlap mods', so there is nothing for
    // the catalog matcher to recognise a hand-installed addon by - one of those shows up as an
    // ordinary unmatched card, exactly as a hand-installed mod that matches nothing does.
    //
    public bool IsAddon { get; init; }

    // True for an addon still listed on sp-mod.com, whether or not any of its versions fits.
    public bool IsAddonListed { get; init; }

    // The name of the mod this addon attaches to. Null for a mod, or when the parent isn't in the
    // cached catalog.
    public string? ParentModName { get; init; }

    // The sp-mod id of the mod this addon attaches to. Null for a mod, or when the addon isn't in the
    // cached catalog.
    public int? ParentModId { get; init; }

    // The parent mod's installed version - what every one of this addon's version constraints is
    // measured against. Null for a mod, or when the parent isn't installed.
    public string? ParentInstalledVersion { get; init; }

    // Whether this addon's installed version fits its mod's installed version: null when it can't be
    // told (no parent installed, or the installed release isn't in the cached listing).
    public bool? FitsParent { get; init; }

    // OPEN-32 D11: the installed version doesn't fit its mod - shown as "nothing fits" rather than "?".
    public bool DoesntFitParent { get; init; }

    // OPEN-37 D3: installed anyway, though its listed range doesn't include its mod's version.
    public bool IsFitOverridden { get; init; }

    private bool IsInstalledAnyway => IsAddon && DoesntFitParent && IsFitOverridden;

    // The card's "Addon for X" line, or null for an ordinary mod.
    public string? AddonSubtitle => IsAddon
        ? ParentModName is not null ? Text(Strings.Installed_AddonForFormat, ParentModName) : Strings.Installed_Addon
        : null;

    // True when this mod has an install record this app itself wrote, meaning Remove can delete
    // exactly those files. False for anything installed by hand or from outside the app, including a
    // mod with only a manually-confirmed version (see IsManualOverride).
    public bool IsAppManaged { get; init; }

    // True when InstalledVersion comes from the user manually confirming/overriding it (Installed
    // page's "manage version" controls) rather than from an app install or the files on disk.
    public bool IsManualOverride { get; init; }

    //
    // Folders this app's install record placed that the scan can no longer find, and whether the
    // install itself gave up part way through (InstalledModRecord.Incomplete, recorded since
    // partial installs were tracked and never shown anywhere until now).
    //
    // Either one means the mod on disk is not the mod that was installed - and the version it
    // reports is still the recorded one, so nothing else gives it away. Reinstalling is the fix,
    // which is what a mod list now does for it too - see ModListCandidate.IsIncomplete.
    //
    public IReadOnlyList<string> MissingFolders { get; init; } = [];

    public bool WasPartlyInstalled { get; init; }

    //
    // Set after a scan by InstalledViewModel when this folder is one a removal had to leave because
    // it holds files the removed mod didn't install. Null otherwise.
    //
    [ObservableProperty]
    private string? _leftoverSummary;

    //
    // Set after a scan by InstalledViewModel (ModConflicts.Apply): what this mod clashes with at load
    // time and where to see more, or null when it clashes with nothing (OPEN-11).
    //
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConflicts))]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyPropertyChangedFor(nameof(StatusGlyph))]
    [NotifyPropertyChangedFor(nameof(StatusTooltip))]
    private string? _conflictSummary;

    //
    // OPEN-23 S4: set after a scan (DependencyVersions.Apply) when this mod's own files ask for a
    // dependency the install doesn't meet - SPT or BepInEx won't load it. Null otherwise.
    //
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConflicts))]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyPropertyChangedFor(nameof(StatusGlyph))]
    [NotifyPropertyChangedFor(nameof(StatusTooltip))]
    private string? _versionSummary;

    public bool HasConflicts => ConflictSummary is not null || VersionSummary is not null;

    private string? JoinConflicts() => ConflictSummary is null
        ? VersionSummary
        : VersionSummary is null ? ConflictSummary : ConflictSummary + "\n\n" + VersionSummary;

    public bool IsIncompleteInstall => MissingFolders.Count > 0 || WasPartlyInstalled;

    // One whole sentence per count: the verb has to agree with how many folders are missing, and
    // a verb spliced into a sentence cannot be translated.
    public string? IncompleteSummary => !IsIncompleteInstall
        ? null
        : MissingFolders.Count == 0
            ? Strings.Installed_IncompletePartial
            : Strings.Installed_IncompleteMissing(
                MissingFolders.Count,
                string.Join(Strings.Common_ListSeparator, MissingFolders));

    //
    // A Monitor mode download of this mod that a scan found on disk but nobody has confirmed yet -
    // installed as far as its files show, or only partly. Set after a scan by InstalledViewModel,
    // the same way Lists is; null when there is none, which is every card outside Monitor mode.
    //
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingDownload))]
    [NotifyPropertyChangedFor(nameof(CanConfirmDownload))]
    [NotifyPropertyChangedFor(nameof(DownloadSummary))]
    [NotifyPropertyChangedFor(nameof(StatusTooltip))]
    private PendingDownload? _pendingDownload;

    public bool HasPendingDownload => PendingDownload is not null;

    // Only a download whose every file is on disk at its size. A partial one is never confirmed,
    // by the prompt or by hand - a half-copied install is exactly what the note is there to show.
    public bool CanConfirmDownload => PendingDownload?.Match.Kind == DownloadMatchKind.Installed;

    public string? DownloadSummary => PendingDownload switch
    {
        null => null,
        { Match.Kind: DownloadMatchKind.Installed } p =>
            Text(Strings.Installed_DownloadLooksInstalledFormat, p.Download.Version),
        { } p => Text(Strings.Installed_DownloadPartialFormat, p.Download.Version),
    };

    //
    // Every scan entry merged into this card: both halves of a client+server mod, and both copies
    // of a mod left in a container and in that container's ".disabled" sibling. What the disable
    // commands actually move, and what ModDependencyGraph is keyed on.
    //
    public IReadOnlyList<InstalledMod> Entries { get; init; } = [];

    //
    // OPEN-28: the folder this mod shares with other mods (the folder's own name, "Realism"), and the
    // display titles of those other mods. Null and empty for a mod with folders of its own.
    //
    public string? SharedFolderName { get; private set; }

    public IReadOnlyList<string> SharedWith { get; private set; } = [];

    // The card's "Installed in the Realism folder, shared with ..." line, or null for a mod with
    // folders of its own.
    public string? SharedFolderLine => SharedFolderName is null
        ? null
        : SharedWith.Count == 0
            ? Text(Strings.Installed_SharedFolderFormat, SharedFolderName)
            : Text(Strings.Installed_SharedFolderWithFormat, SharedFolderName, string.Join(Strings.Common_ListSeparator, SharedWith));

    //
    // The mod lists that name this mod, filled in after a scan by InstalledViewModel (the card
    // itself knows nothing about lists). One badge each, so a glance at the card says which sets
    // it belongs to - and therefore which list switches won't cost a download.
    //
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInAnyList))]
    [NotifyPropertyChangedFor(nameof(ShowLists))]
    private IReadOnlyList<string> _lists = [];

    // The page-wide toggle, pushed down per card so one binding answers "draw the chips or not"
    // rather than every template having to combine two conditions itself.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLists))]
    private bool _showBadges = true;

    public bool IsInAnyList => Lists.Count > 0;

    public bool ShowLists => ShowBadges && Lists.Count > 0;

    // Pinned against a list's Exclusive sweep - see ModListStore.SetPinned. Filled in after a scan
    // by InstalledViewModel, like Lists.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinLabel))]
    [NotifyPropertyChangedFor(nameof(PinGlyph))]
    [NotifyPropertyChangedFor(nameof(PinTooltip))]
    private bool _isPinned;

    public string PinLabel => IsPinned ? Strings.Installed_Unpin : Strings.Installed_Pin;

    public string PinGlyph => IsPinned ? "PinOff24" : "Pin24";

    public string PinTooltip => IsPinned ? Strings.Installed_UnpinToolTip : Strings.Installed_PinToolTip;

    // True when every one of this mod's folders sits under a ".disabled" container, so SPT loads none of it.
    public bool IsDisabled => Entries.Count > 0 && Entries.All(e => e.IsDisabled);

    // True when some of this mod's folders are disabled and some aren't - one half of a
    // client+server mod parked on its own, or a half-completed move leaving copies in both places.
    public bool IsMixedState => Entries.Any(e => e.IsDisabled) && Entries.Any(e => !e.IsDisabled);

    // The same folder sitting in both a container and its ".disabled" sibling, which the disable
    // toggle can't settle on its own - one of the two copies has to be set aside first. Worked out
    // once rather than per read: Entries never changes, and HasDuplicateFolders is bound on every
    // card and every list row, so a computed property here is evaluated constantly.
    private IReadOnlyList<ModDuplicatePair>? _duplicateFolders;

    public IReadOnlyList<ModDuplicatePair> DuplicateFolders =>
        _duplicateFolders ??= ModDisableService.DuplicatePairs(Entries);

    public bool HasDuplicateFolders => DuplicateFolders.Count > 0;

    // Ticked in the flat grid's multi-select mode. Cards are rebuilt on every scan, so a selection
    // doesn't survive one.
    [ObservableProperty]
    private bool _isSelected;

    //
    // Whether this mod is open in Cards view, and separately in List view.
    //
    // Held here rather than left to the CardExpander because every scan replaces every card:
    // closing the versions dialog triggers a rescan, which would otherwise snap shut the card you
    // opened it from. InstalledViewModel carries the open sets across a rebuild.
    //
    // Two flags rather than one because both views render the SAME card objects - Results and
    // ListItems are both built from _filtered - so a single shared flag meant opening a card in one
    // view silently opened the same mod in the other.
    //
    [ObservableProperty]
    private bool _isCardExpanded;

    [ObservableProperty]
    private bool _isRowExpanded;

    //
    // The user-defined group this mod is assigned to, or null when it's in none. Filled in by
    // InstalledViewModel from the group store after each scan and after every group change, rather
    // than read here - the card knows nothing about where groups are stored.
    //
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GroupLabel))]
    [NotifyPropertyChangedFor(nameof(IsGrouped))]
    private string? _groupName;

    // The same assignment as GroupName, by id - what the Group filter matches on, since two groups
    // can be given the same name.
    [ObservableProperty]
    private Guid? _groupId;

    public string GroupLabel => GroupName ?? Strings.Filter_Ungrouped;

    public bool IsGrouped => GroupName is not null;

    // The catalog listing's content flags as one line, or null when it carries none of them.
    public string? FlagsSummary
    {
        get
        {
            var flags = new List<string>();
            if (IsFikaCompatible) flags.Add(Strings.Common_FlagFikaCompatible);
            if (ContainsAds) flags.Add(Strings.Common_FlagContainsAds);

            return flags.Count == 0 ? null : string.Join(Strings.Common_FlagSeparator, flags);
        }
    }

    // Where this mod's recorded version comes from, spelled out for the details view.
    public string SourceLabel => IsAppManaged
        ? Strings.Installed_SourceAppManaged
        : IsManualOverride
            ? Strings.Installed_SourceManualConfirmed
            : Strings.Installed_SourceManual;

    public string DisableToggleGlyph => IsDisabled ? "PlugConnected24" : "PlugDisconnected24";

    public string DisableToggleTooltip => IsDisabled
        ? Strings.Installed_CardEnableToolTip
        : Strings.Installed_CardDisableToolTip;

    // This mod's status, using the same vocabulary and icons as the Browse and Dependencies pages.
    // Everything here is installed by definition, so it's only ever disabled, up-to-date or outdated.
    // A conflict outranks the update state: the mod may not load at all.
    //
    // OPEN-13 R1: a version not for this SPT is an update when a newer release is (UpdateAvailable
    // already says so), and otherwise "nothing fits" - also when only an older release does (that one
    // is named, never offered as an update).
    //
    public ModStatus Status => IsDisabled
        ? ModStatus.Disabled
        : HasConflicts
            ? ModStatus.Conflict
            : IsInstalledAnyway && UpdateAvailable != true
                ? ModStatus.InstalledAnyway
            : IsAddon && DoesntFitParent && !IsFitOverridden
                ? ModStatus.NoCompatibleVersion
            : SptFit is { Fits: false } && !IsSptWarningDismissed && UpdateAvailable != true
                ? ModStatus.NoCompatibleVersion
            : UpdateAvailable switch
        {
            true => ModStatus.UpdateAvailable,
            false => ModStatus.Installed,
            _ => ModStatus.Unknown,
        };

    // A mixed state is neither cleanly enabled nor cleanly disabled, so it gets the conflict icon
    // rather than either side's.
    public string StatusGlyph => IsMixedState || IsIncompleteInstall
        ? "ErrorCircle24"
        : ModStatusDisplay.Glyph(Status);

    // Missing files first: it is the only one of these the card gives no other sign of.
    // A pending download next: the collapsed card's only sign of one is this tooltip.
    public string StatusTooltip =>
        IncompleteSummary
        ?? JoinConflicts()
        ?? DownloadSummary
        ?? (IsInstalledAnyway && !IsDisabled
            ? Text(Strings.Installed_AddonInstalledAnywayToolTipFormat, ParentModName ?? Strings.Addon_ParentFallback, ParentInstalledVersion)
            : null)
        ?? (IsAddon && DoesntFitParent && !IsDisabled
            ? Text(Strings.Installed_AddonNoFitToolTipFormat, ParentModName, ParentInstalledVersion)
            : null)
        ?? (SptFit is { Fits: false } && !IsSptWarningDismissed ? SptNote : null)
        ?? (UpdateAvailable == true ? null : HeldBackNote)
        ?? (HasDuplicateFolders
            ? Strings.Installed_StatusDuplicate
            : IsMixedState
                ? Strings.Installed_StatusMixed
                : UpdateAvailable == true && !IsDisabled && (UpdateVersion ?? LatestPublishedVersion) is { } target
                    ? Text(Strings.Installed_StatusUpdateFormat, target)
                    : ModStatusWording.Tooltip(Status));
}

// A download from the ledger and what the last scan made of it.
public sealed record PendingDownload(DownloadedModRecord Download, DownloadMatch Match);
