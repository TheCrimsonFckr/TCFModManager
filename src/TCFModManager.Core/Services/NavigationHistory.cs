namespace TCFModManager.Core.Services;

//
// OPEN-56 N18: Back and Forward through the places a window has shown, as a browser does - going
// somewhere new drops what was ahead. Holds at most Capacity places behind the current one.
//
public sealed class NavigationHistory<T> where T : class
{
    private readonly List<T> _back = [];
    private readonly List<T> _forward = [];

    public NavigationHistory(int capacity) => Capacity = Math.Max(1, capacity);

    public int Capacity { get; }

    public T? Current { get; private set; }

    public bool CanGoBack => _back.Count > 0;

    public bool CanGoForward => _forward.Count > 0;

    /// <summary>The places behind the current one, the nearest first.</summary>
    public IReadOnlyList<T> Back => Nearest(_back);

    /// <summary>The places ahead of the current one, the nearest first.</summary>
    public IReadOnlyList<T> Forward => Nearest(_forward);

    /// <summary>A new place: what was ahead is dropped. False when it is the place already current.</summary>
    public bool Visit(T place)
    {
        if (Equals(Current, place)) return false;

        if (Current is not null)
        {
            _back.Add(Current);
            if (_back.Count > Capacity) _back.RemoveAt(0);
        }

        _forward.Clear();
        Current = place;
        return true;
    }

    /// <summary>Steps back; the place now current, or null when there aren't that many.</summary>
    public T? GoBack(int steps = 1) => Step(_back, _forward, steps);

    /// <summary>Steps forward; the place now current, or null when there aren't that many.</summary>
    public T? GoForward(int steps = 1) => Step(_forward, _back, steps);

    private T? Step(List<T> from, List<T> to, int steps)
    {
        if (steps < 1 || steps > from.Count || Current is null) return null;

        for (var i = 0; i < steps; i++)
        {
            to.Add(Current!);
            Current = from[^1];
            from.RemoveAt(from.Count - 1);
        }

        if (to == _back && _back.Count > Capacity) _back.RemoveRange(0, _back.Count - Capacity);
        return Current;
    }

    private static List<T> Nearest(List<T> stack)
    {
        var nearest = new List<T>(stack.Count);
        for (var i = stack.Count - 1; i >= 0; i--) nearest.Add(stack[i]);
        return nearest;
    }
}
