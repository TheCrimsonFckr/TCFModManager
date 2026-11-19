namespace TCFModManager.Core.Services;

public static class DependencyOrdering
{
    //
    // <paramref name="items"/> with each after the items it needs (indexes from <paramref name="needs"/>),
    // otherwise in the order given. A cycle is broken where it closes.
    //
    public static IReadOnlyList<T> Order<T>(IReadOnlyList<T> items, Func<int, IEnumerable<int>> needs)
    {
        var placed = new bool[items.Count];
        var visiting = new bool[items.Count];
        var order = new List<T>(items.Count);

        void Visit(int i)
        {
            if (placed[i] || visiting[i]) return;
            visiting[i] = true;

            foreach (var j in needs(i))
                if (j >= 0 && j < items.Count && j != i) Visit(j);

            visiting[i] = false;
            placed[i] = true;
            order.Add(items[i]);
        }

        for (var i = 0; i < items.Count; i++) Visit(i);
        return order;
    }
}
