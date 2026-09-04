using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// One mod a fetch couldn't get.
public sealed record ModListFetchFailure(string ModName, string Reason);

// What the caller reports back after fetching the plan's Install/Update actions.
public sealed record ModListFetchOutcome(
    IReadOnlyList<ModListAction> Fetched,
    IReadOnlyList<ModListFetchFailure> Failed,
    bool Cancelled)
{
    public static ModListFetchOutcome Empty { get; } = new([], [], false);

    public bool Succeeded => Failed.Count == 0 && !Cancelled;
}

//
// Downloads and installs the actions handed to it, completing once they have all finished.
//
// This is the seam between the two halves of an apply. The moves belong to Core, but the download
// queue lives in the App, so the App supplies this and the applier decides when to call it.
//
public delegate Task<ModListFetchOutcome> ModListFetch(
    IReadOnlyList<ModListAction> fetches,
    CancellationToken ct);

public sealed record ModListApplyOptions
{
    //
    // Name for the automatic "put me back" list capturing the install as it stands before anything
    // moves. Null takes no snapshot. The applier only builds it - storing it is the caller's job,
    // so an apply can be run without touching the store at all.
    //
    public string? SnapshotName { get; init; }

    public ModListCapture.VersionLookup? SnapshotVersions { get; init; }

    // The addon equivalent, so a snapshot pins an installed addon's version the same way it pins a
    // mod's - without it, reverting would offer to reinstall every addon at whatever is newest.
    public ModListCapture.AddonVersionLookup? SnapshotAddonVersions { get; init; }

    public string? SptVersion { get; init; }
}
//
// Why an apply stopped before it finished.
//
// A reason and its values, never a sentence: Core has no UI, and this one used to build the only
// piece of user-facing English left on this path. App/Services/ModListProblems does the wording -
// see [[feedback-core-no-user-prose]] for the convention and AppUpdateFailure for the first of these.
//
public enum ModListStop
{
    // SPT or its server was already running when the apply started, so nothing was moved at all.
    // Carries Running.
    InstallInUse,

    // A download was cancelled. Nothing had been disabled yet, by the applier's own ordering.
    FetchCancelled,

    // At least one download failed, so nothing was disabled. Carries FailedFetches.
    FetchFailed,

    // A move was refused part way through - the install was locked between the pre-flight check and
    // the move itself. Carries Refusal.
    MovesRefused,
}


public sealed record ModListApplyResult
{
    public required Guid ListId { get; init; }

    // The pre-apply snapshot, when one was asked for. Never stored here.
    public ModList? Snapshot { get; init; }

    public required ModDisableOutcome Enabled { get; init; }
    public required ModDisableOutcome Disabled { get; init; }
    public required ModListFetchOutcome Fetched { get; init; }

    // Mods the list names that nobody can fetch - reported so the UI can list them, never acted on.
    public required IReadOnlyList<ModListAction> Manual { get; init; }

    // Null when the apply ran to the end. Otherwise why it stopped - everything that did happen is
    // still reported in the fields above, and Moves is what has to be put back.
    public ModListStop? Stopped { get; init; }

    // What has to be closed, for InstallInUse.
    public IReadOnlyList<string> Running { get; init; } = [];

    // How many downloads failed, for FetchFailed.
    public int FailedFetches { get; init; }

    // The refusal itself, for MovesRefused - it already carries which operation and what is holding
    // the install, so the App words it exactly as it words every other refusal.
    public ModInstallException? Refusal { get; init; }

    // Derived rather than stored: the two cannot disagree, and a construction site cannot forget it.
    public bool Completed => Stopped is null;

    // Every move made, in the order it was made - hand straight to ModDisableService.Revert to undo.
    public IReadOnlyList<ModMove> Moves => [.. Enabled.Moved, .. Disabled.Moved];
}

//
// Turns a ModListPlan into an applied install.
//
// The order is the whole point, and it is not the order the plan lists things in:
//
//   1. refuse to start at all if SPT is running and the plan has moves
//   2. snapshot what's here now, before anything changes
//   3. enable   - a mod that also needs updating has to be back in its live container first
//   4. fetch    - installs, updates, and those pending updates
//   5. disable  - last, and only if every fetch worked
//
// Step 5 running last is the safety property: a failed download leaves the install as it was plus
// whatever did arrive, rather than a half-built set with the old one already torn down.
//
public static class ModListApplier
{
    public static async Task<ModListApplyResult> ApplyAsync(
        ModListPlan plan,
        IEnumerable<ModListCandidate> installed,
        ModListFetch fetch,
        ModListApplyOptions? options = null,
        DateTimeOffset? timestamp = null,
        CancellationToken ct = default)
    {
        options ??= new ModListApplyOptions();

        var candidates = installed.ToList();
        var manual = plan.Manual.ToList();
        var enables = plan.Enable.ToList();
        var disables = plan.Disable.ToList();

        if ((enables.Count > 0 || disables.Count > 0) && ModInstallService.RunningBlockers() is { Count: > 0 } blockers)
        {
            return Stopped(plan, manual, null, ModListStop.InstallInUse, running: blockers);
        }

        var snapshot = options.SnapshotName is null
            ? null
            : ModListCapture.Build(
                options.SnapshotName,
                candidates,
                timestamp ?? DateTimeOffset.UtcNow,
                options.SnapshotVersions,
                options.SptVersion,
                plan.Policy,
                isSnapshot: true,
                addonVersions: options.SnapshotAddonVersions);

        var enabled = ModDisableOutcome.Empty;

        if (enables.Count > 0)
        {
            try
            {
                enabled = ModDisableService.Apply(Entries(enables), disable: false);
            }
            //
            // ModInstallException, not InvalidOperationException: EnsureInstallNotInUse threw the
            // latter until 1.10.0 gave it a type of its own, and this catch quietly stopped
            // matching - which let a refusal escape ApplyAsync altogether and skipped the unwind
            // that puts already-enabled mods back.
            //
            catch (ModInstallException ex)
            {
                return Stopped(plan, manual, snapshot, ModListStop.MovesRefused, refusal: ex);
            }
        }

        var fetches = Fetchable(plan, enabled);

        var fetched = fetches.Count == 0
            ? ModListFetchOutcome.Empty
            : await fetch(fetches, ct).ConfigureAwait(false);

        if (!fetched.Succeeded)
        {
            return new ModListApplyResult
            {
                ListId = plan.ListId,
                Snapshot = snapshot,
                Enabled = enabled,
                Disabled = ModDisableOutcome.Empty,
                Fetched = fetched,
                Manual = manual,
                Stopped = fetched.Cancelled ? ModListStop.FetchCancelled : ModListStop.FetchFailed,
                FailedFetches = fetched.Failed.Count,
            };
        }

        var disabled = ModDisableOutcome.Empty;

        if (disables.Count > 0)
        {
            try
            {
                disabled = ModDisableService.Apply(Entries(disables), disable: true);
            }
            catch (ModInstallException ex)
            {
                return new ModListApplyResult
                {
                    ListId = plan.ListId,
                    Snapshot = snapshot,
                    Enabled = enabled,
                    Disabled = ModDisableOutcome.Empty,
                    Fetched = fetched,
                    Manual = manual,
                    Stopped = ModListStop.MovesRefused,
                    Refusal = ex,
                };
            }
        }

        return new ModListApplyResult
        {
            ListId = plan.ListId,
            Snapshot = snapshot,
            Enabled = enabled,
            Disabled = disabled,
            Fetched = fetched,
            Manual = manual,
        };
    }

    //
    // Every fetch the plan asks for, minus any pending update whose enable didn't work. Downloading
    // into a mod that is still sitting in its .disabled container would place the new files in the
    // live container while the old copy stays disabled - the duplicate ModDisableService.DuplicatePairs
    // exists to clean up.
    //
    private static List<ModListAction> Fetchable(ModListPlan plan, ModDisableOutcome enabled)
    {
        if (enabled.Failed.Count == 0) return [.. plan.Actions.Where(a => a.IsFetch)];

        var failed = enabled.Failed.Select(f => f.ModName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return
        [
            .. plan.Actions.Where(a => a.IsFetch
                && !(a.NeedsUpdateAfterEnable
                    && (a.Installed?.Entries.Any(e => failed.Contains(e.Name)) ?? false)))
        ];
    }

    private static IEnumerable<InstalledMod> Entries(IEnumerable<ModListAction> actions) =>
        actions.SelectMany(a => a.Installed?.Entries ?? []);

    private static ModListApplyResult Stopped(
        ModListPlan plan,
        IReadOnlyList<ModListAction> manual,
        ModList? snapshot,
        ModListStop reason,
        IReadOnlyList<string>? running = null,
        ModInstallException? refusal = null) =>
        new()
        {
            ListId = plan.ListId,
            Snapshot = snapshot,
            Enabled = ModDisableOutcome.Empty,
            Disabled = ModDisableOutcome.Empty,
            Fetched = ModListFetchOutcome.Empty,
            Manual = manual,
            Stopped = reason,
            Running = running ?? [],
            Refusal = refusal,
        };
}
