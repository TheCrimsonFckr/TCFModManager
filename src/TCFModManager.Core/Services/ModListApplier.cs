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

    public string? SptVersion { get; init; }
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

    // False when the apply stopped early; StoppedBecause says why, and everything that did happen
    // is still reported in the fields above.
    public required bool Completed { get; init; }

    public string? StoppedBecause { get; init; }

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
            return Stopped(plan, manual, null, Running(blockers));
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
                isSnapshot: true);

        var enabled = ModDisableOutcome.Empty;

        if (enables.Count > 0)
        {
            try
            {
                enabled = ModDisableService.Apply(Entries(enables), disable: false);
            }
            catch (InvalidOperationException ex)
            {
                return Stopped(plan, manual, snapshot, ex.Message);
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
                Completed = false,
                StoppedBecause = fetched.Cancelled
                    ? "cancelled before anything was disabled"
                    : $"{fetched.Failed.Count} mod(s) couldn't be fetched, so nothing was disabled",
            };
        }

        var disabled = ModDisableOutcome.Empty;

        if (disables.Count > 0)
        {
            try
            {
                disabled = ModDisableService.Apply(Entries(disables), disable: true);
            }
            catch (InvalidOperationException ex)
            {
                return new ModListApplyResult
                {
                    ListId = plan.ListId,
                    Snapshot = snapshot,
                    Enabled = enabled,
                    Disabled = ModDisableOutcome.Empty,
                    Fetched = fetched,
                    Manual = manual,
                    Completed = false,
                    StoppedBecause = ex.Message,
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
            Completed = true,
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

    private static string Running(IReadOnlyList<string> blockers) =>
        $"SPT is running ({string.Join(", ", blockers)}) - close it and apply again";

    private static ModListApplyResult Stopped(
        ModListPlan plan,
        IReadOnlyList<ModListAction> manual,
        ModList? snapshot,
        string reason) =>
        new()
        {
            ListId = plan.ListId,
            Snapshot = snapshot,
            Enabled = ModDisableOutcome.Empty,
            Disabled = ModDisableOutcome.Empty,
            Fetched = ModListFetchOutcome.Empty,
            Manual = manual,
            Completed = false,
            StoppedBecause = reason,
        };
}
