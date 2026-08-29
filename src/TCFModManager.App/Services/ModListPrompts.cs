using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

// A fetch with its version already resolved, ready to be queued.
public sealed record ModListDownload(ModListAction Action, Mod Mod, ModVersion Version, bool IsSubstitute);

// A fetch whose list-named version is no longer published, and the newest one that is.
public sealed record ModListVersionChange(ModListAction Action, Mod Mod, string Wanted, ModVersion Available);

//
// Every fetch in a plan, sorted into what can be queued as-is, what would need a different version
// than the list names, and what can't be had at all.
//
public sealed record ModListResolution(
    IReadOnlyList<ModListDownload> Ready,
    IReadOnlyList<ModListVersionChange> Changes,
    IReadOnlyList<ModListFetchFailure> Unavailable);

//
// The two questions applying a list has to ask before it downloads anything.
//
// They are delegates rather than direct calls into Views so an apply can be driven headlessly, and
// so the answers are visible at the call site rather than buried in a service. `Default` wires the
// real windows; `Reject` is what happens when nothing is wired.
//
public sealed record ModListPrompts(
    Func<IReadOnlyList<ModListVersionChange>, IReadOnlyList<ModListVersionChange>> ApproveVersionChanges,
    Func<IReadOnlyList<ModListDownload>, bool> ConfirmModPages)
{
    //
    // What an apply does when it has nobody to ask: substitute nothing, download nothing.
    //
    // Fail-closed on purpose. This app has never placed a file without asking, and a service with
    // no UI wired to it is exactly the case where a silent yes would go unnoticed.
    //
    public static ModListPrompts Reject { get; } = new(_ => [], _ => false);

    public static ModListPrompts Default { get; } = new(
        ModListVersionChangeWindow.Approve,
        downloads => ReadModPageConfirmationWindow.ConfirmAll(
            [.. downloads.Select(d => new ModPageLink(d.Mod.Name ?? d.Action.Name, d.Mod.DetailUrl))]));
}
