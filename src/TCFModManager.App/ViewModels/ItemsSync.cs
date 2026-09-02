using System.Collections.ObjectModel;

namespace TCFModManager.App.ViewModels;

//
// Keeps a bound ObservableCollection in step with a freshly computed list.
//
// Its own file, and not private to InstalledViewModel, because it is pure index arithmetic with no
// UI in it - which means it can be exercised headless, and index arithmetic is exactly the kind of
// thing that compiles happily while being wrong.
//
public static class ItemsSync
{
    //
    // Brings an items collection in line with what it should hold by removing, moving and inserting
    // - never by clearing and refilling.
    //
    // A Clear regenerates every item container, and a regenerated ui:CardExpander replays the
    // 0.333s open animation its template fires from a Trigger on IsExpanded becoming true. That is
    // why every filter change, page turn and rescan made every open card flash back open. Anything
    // that survives the change keeps its container and stays still.
    //
    // Reference equality throughout: these view models are plain classes that don't override
    // Equals, and identity is exactly the question being asked - "is this the same card object the
    // list already has a container for".
    //
    public static void Apply<T>(ObservableCollection<T> target, IReadOnlyList<T> wanted) where T : class
    {
        var keep = new HashSet<T>(wanted);

        for (var i = target.Count - 1; i >= 0; i--)
            if (!keep.Contains(target[i]))
                target.RemoveAt(i);

        for (var i = 0; i < wanted.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], wanted[i])) continue;

            var found = -1;
            for (var j = i + 1; j < target.Count; j++)
                if (ReferenceEquals(target[j], wanted[i]))
                {
                    found = j;
                    break;
                }

            if (found >= 0) target.Move(found, i);
            else target.Insert(i, wanted[i]);
        }

        while (target.Count > wanted.Count) target.RemoveAt(target.Count - 1);
    }
}
