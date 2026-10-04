using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace TCFModManager.App.ViewModels;

//
// Fills a bound list a few items per frame instead of all at once, when every item in it would
// need a new container anyway.
//
// Building a row is the expensive part of showing a list - WPF instantiates its template and
// measures it - at roughly 10-20ms per Installed row or card on a real install (NavProbe, 2026-10-04).
// Done in one go that was a single freeze of ~1.3s for the 107-row List and ~450ms for a page of
// cards. Spread out, each batch gets its own layout pass and frame, so the page stays responsive,
// the first rows appear at once and the rest arrive below them. The total work is the same.
//
// Only when nothing in the list survives - the first build, or a rescan that replaced every view
// model. Anything else goes through ItemsSync as before, so a filter or sort change keeps the
// containers it can, and an open card doesn't replay its opening animation (see ItemsSync).
//
// Not virtualization on purpose: a virtualized row is rebuilt every time it scrolls back into
// view, which replays that same animation on every expanded row, and it would mean reworking the
// scroll-anywhere setup on the Installed page.
//
public sealed class GradualFill<T>(int batchSize) where T : class
{
    // Bumped by every Apply, so a fill still in progress stops as soon as a newer one starts.
    private int _generation;

    // True while batches are still being added. A page that would rather not show a list arriving
    // a few items at a time (Installed's Cards) hides it while this is set and reveals it whole.
    public bool IsFilling
    {
        get => _filling;
        private set
        {
            if (_filling == value) return;
            _filling = value;
            IsFillingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? IsFillingChanged;

    //
    // Completes once the list is whole - straight away when no fill is running. Browse's startup
    // panel waits on this so the first page of cards is built while the panel is still up.
    //
    public Task WhenFilledAsync()
    {
        if (!IsFilling) return Task.CompletedTask;

        var done = new TaskCompletionSource();
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            if (IsFilling) return;
            IsFillingChanged -= handler;
            done.TrySetResult();
        };
        IsFillingChanged += handler;
        return done.Task;
    }

    private bool _filling;

    public void Apply(ObservableCollection<T> target, IReadOnlyList<T> wanted)
    {
        var generation = ++_generation;

        //
        // A second Apply landing mid-fill - the Installed scan re-applies its filter more than once
        // in a row, for one - is not a reason to finish the job in one go. Bring what is already in
        // the list in line with the start of the new list, and carry on in batches from there.
        //
        if (_filling && wanted.Count > target.Count)
        {
            ItemsSync.Apply(target, [.. wanted.Take(target.Count)]);
            _ = ContinueAsync(target, wanted, generation, target.Count);
            return;
        }

        if (wanted.Count <= batchSize || Overlaps(target, wanted))
        {
            IsFilling = false;
            ItemsSync.Apply(target, wanted);
            return;
        }

        target.Clear();
        for (var i = 0; i < batchSize; i++) target.Add(wanted[i]);

        _ = ContinueAsync(target, wanted, generation, batchSize);
    }

    // Stops a fill still in progress, for a caller about to fill the list some other way.
    public void Cancel()
    {
        _generation++;
        IsFilling = false;
    }

    private async Task ContinueAsync(
        ObservableCollection<T> target, IReadOnlyList<T> wanted, int generation, int next)
    {
        IsFilling = true;

        while (next < wanted.Count)
        {
            // Below Render and Input, so the frame holding the last batch is drawn, and any click or
            // wheel is handled, before the next batch is built.
            await Dispatcher.Yield(DispatcherPriority.Background);
            if (generation != _generation) return;

            var end = Math.Min(next + batchSize, wanted.Count);
            for (; next < end; next++) target.Add(wanted[next]);
        }

        IsFilling = false;
    }

    private static bool Overlaps(ObservableCollection<T> target, IReadOnlyList<T> wanted)
    {
        if (target.Count == 0) return false;

        var current = new HashSet<T>(target, ReferenceEqualityComparer.Instance);
        return wanted.Any(current.Contains);
    }
}
