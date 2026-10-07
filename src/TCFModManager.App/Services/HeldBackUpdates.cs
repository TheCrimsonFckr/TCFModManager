using TCFModManager.App.Localization;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// OPEN-12 F11: the updates sp-mod holds back because they would break another installed mod - its
// /mods/updates "blocked_updates", asked about the whole install after each Installed scan and on
// every update-notification check.
//
// A held-back update is not an update (Chris, 2026-10-06): InstalledModCardViewModel.BuildFrom reads
// this and leaves the card at Installed with a caution note naming what it would break, so nothing -
// the Update available filter, the badge, a toast, Update selected - offers it. When an older release
// that every one of those mods accepts is still newer than the installed one, that is offered instead
// (HeldBackVersions). The update dialog still lets you pick the held release, with the same note.
//
// Held only for this session. A failed or offline check leaves the last answer in place.
//
public sealed class HeldBackUpdates
{
    private readonly UpdateCheckService _checker = new(AppServices.SpModApi);

    // Swapped whole, never changed in place, so BuildFrom can read it from a background thread.
    private IReadOnlyDictionary<int, HeldBackUpdate> _held = new Dictionary<int, HeldBackUpdate>();

    // The SPT version the answer is for; an answer for another SPT holds nothing back.
    private string? _sptVersion;

    // Bumped per check, so an older check answering after a newer one is dropped.
    private int _request;

    // Raised, on the caller's thread, when what is held back changed.
    public event EventHandler? Changed;

    // The update held back for this mod on this SPT, or null.
    public HeldBackUpdate? For(int? modId, string? sptVersion) =>
        modId is { } id
        && string.Equals(sptVersion, _sptVersion, StringComparison.OrdinalIgnoreCase)
        && _held.TryGetValue(id, out var held)
            ? held
            : null;

    // Every update held back on this SPT - empty for an answer about another SPT. OPEN-23 reads the
    // blockers' ranges from it.
    public IReadOnlyCollection<HeldBackUpdate> All(string? sptVersion) =>
        string.Equals(sptVersion, _sptVersion, StringComparison.OrdinalIgnoreCase)
            ? _held.Values.ToList()
            : [];

    //
    // Takes an answer an update check already has (UpdateWatcher). Returns whether it changed what is
    // held back, and raises Changed when it did.
    //
    public bool Set(IEnumerable<HeldBackUpdate> heldBack, string sptVersion)
    {
        var next = heldBack.GroupBy(h => h.ModId).ToDictionary(g => g.Key, g => g.First());

        var same = string.Equals(sptVersion, _sptVersion, StringComparison.OrdinalIgnoreCase)
            && next.Count == _held.Count
            && next.All(n => _held.TryGetValue(n.Key, out var old) && Same(old, n.Value));

        _held = next;
        _sptVersion = sptVersion;

        if (same) return false;

        AppLog.Info("Updates", next.Count == 0
            ? "sp-mod holds no updates back"
            : $"sp-mod holds back: {string.Join(", ", next.Values.Select(h => $"{h.ModId} {h.Version}"))}");

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    // Asks sp-mod about these installed mods (id and installed version) on this SPT.
    public async Task RefreshAsync(IReadOnlyList<(int ModId, string Version)> installed, string? sptVersion)
    {
        if (string.IsNullOrWhiteSpace(sptVersion)) return;

        var request = ++_request;

        try
        {
            var answer = installed.Count == 0 ? null : await _checker.FindUpdatedModsAsync(installed, sptVersion);
            if (request != _request) return;

            Set(answer?.HeldBack ?? [], sptVersion);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Updates", $"couldn't ask sp-mod which updates it holds back: {ex.Message}");
        }
    }

    //
    // One sentence saying what is held back and why: each mod it would break, with the newest version
    // that mod accepts where one is published ("needs up to 2.0.31"), else the range sp-mod gives.
    //
    public static string Describe(HeldBackResolution resolved)
    {
        var held = resolved.Held;

        var blockers = held.Blockers
            .Select((b, i) => (Blocker: b, Newest: resolved.PerBlocker.ElementAtOrDefault(i)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Blocker.Name))
            .Select(x => x.Newest is not null
                ? LocalizationService.Text(Strings.Installed_HeldBackNeedsUpToFormat, x.Blocker.Name, x.Newest)
                : string.IsNullOrWhiteSpace(x.Blocker.Constraint)
                    ? x.Blocker.Name!
                    : LocalizationService.Text(Strings.Installed_HeldBackNeedsFormat, x.Blocker.Name, x.Blocker.Constraint))
            .ToList();

        if (blockers.Count > 0)
            return LocalizationService.Text(Strings.Installed_HeldBackFormat, held.Version, TextLists.Join(blockers));

        return held.Reason == HeldBackUpdate.ChainReason
            ? LocalizationService.Text(Strings.Installed_HeldBackChainFormat, held.Version)
            : LocalizationService.Text(Strings.Installed_HeldBackPlainFormat, held.Version);
    }

    private static bool Same(HeldBackUpdate a, HeldBackUpdate b) =>
        a.Holds(b.Version)
        && a.Reason == b.Reason
        && a.Blockers.SequenceEqual(b.Blockers);
}
