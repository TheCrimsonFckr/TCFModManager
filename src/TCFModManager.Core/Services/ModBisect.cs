namespace TCFModManager.Core.Services;

//
// OPEN-54 N27: which of a list of mods makes something fail, found by halving. The mods are numbered
// so each comes after what it needs; the search looks for the shortest failing run "the first k",
// then tries the last mod of it with only its needs, and when that works looks for a second mod.
// Every set is tried once, so a fault that comes and goes still ends the search.
//
public sealed class ModBisect
{
    private enum Phase
    {
        Run,
        Alone,
        Partner,
        Done,
    }

    private readonly int _count;
    private readonly Func<int, IEnumerable<int>> _needs;
    private readonly Dictionary<string, bool> _answers = new(StringComparer.Ordinal);

    private Phase _phase = Phase.Run;
    private int _low;
    private int _high;
    private IReadOnlyList<int> _alone = [];
    private IReadOnlyList<int>? _next;

    /// <param name="count">How many mods, numbered 0 to count - 1, each after what it needs.</param>
    /// <param name="needs">The mods a mod cannot load without, by number - direct ones are enough.</param>
    public ModBisect(int count, Func<int, IEnumerable<int>>? needs = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        _count = count;
        _needs = needs ?? (_ => []);
        _high = count;
        Advance();
    }

    /// <summary>The mods to have on for the next try, in order; null once the search is over.</summary>
    public IReadOnlyList<int>? Next => _next;

    public bool IsDone => _phase == Phase.Done;

    /// <summary>The mod the fault comes with, once the search is over.</summary>
    public int? Culprit { get; private set; }

    /// <summary>The second mod the fault takes, when the first is fine with only what it needs.</summary>
    public int? Partner { get; private set; }

    /// <summary>The mods the fault can still be, as far as the answers so far say: the ones the
    /// first mod is among, then that mod with the ones its partner is among.</summary>
    public IReadOnlyList<int> Suspects => _phase switch
    {
        Phase.Run => [.. Enumerable.Range(_low, _high - _low)],
        Phase.Alone => [Culprit!.Value],
        Phase.Partner => [.. Enumerable.Range(_low, _high - _low), Culprit!.Value],
        _ => Partner is { } partner ? [partner, Culprit!.Value] : [Culprit!.Value],
    };

    /// <summary>About how many tries are left - one more may follow when the fault takes two mods.</summary>
    public int TriesLeft => _phase switch
    {
        Phase.Run => Halvings(_high - _low) + 1,
        Phase.Alone => 1,
        Phase.Partner => Math.Max(1, Halvings(_high - _low)),
        _ => 0,
    };

    /// <summary>About which mods the tries after <see cref="Next"/> will have on, one list per try as
    /// many as <see cref="TriesLeft"/> counts after it: the halving goes on from about where it is,
    /// and the try with the first mod alone has next to nothing on.</summary>
    public IReadOnlyList<IReadOnlyList<int>> Ahead
    {
        get
        {
            if (_next is null) return [];

            var after = Math.Max(0, TriesLeft - 1);
            return _phase switch
            {
                Phase.Run => [.. Enumerable.Repeat<IReadOnlyList<int>>(_next, Math.Max(0, after - 1)), .. after > 0 ? new IReadOnlyList<int>[] { [] } : []],
                Phase.Partner => [.. Enumerable.Repeat<IReadOnlyList<int>>(_next, after)],
                _ => [],
            };
        }
    }

    /// <summary>What the try with <see cref="Next"/> on showed.</summary>
    public void Report(bool failed)
    {
        if (_next is null) throw new InvalidOperationException("the search is over");

        _answers[Key(_next)] = failed;
        Advance();
    }

    private void Advance()
    {
        while (true)
        {
            switch (_phase)
            {
                case Phase.Run:
                    if (_high - _low <= 1)
                    {
                        Culprit = _high - 1;
                        _alone = WithNeeds(_high - 1);
                        _phase = Phase.Alone;
                        continue;
                    }

                    var middle = (_low + _high) / 2;
                    if (!Ask(First(middle), out var runFails)) return;
                    if (runFails) _high = middle;
                    else _low = middle;
                    continue;

                case Phase.Alone:
                    if (_alone.Count == Culprit + 1 && _alone[^1] == Culprit) { Finish(); continue; }
                    if (!Ask(_alone, out var aloneFails)) return;
                    if (aloneFails) { Finish(); continue; }

                    _low = 0;
                    _high = Culprit!.Value;
                    _phase = Phase.Partner;
                    continue;

                case Phase.Partner:
                    if (_high - _low <= 1)
                    {
                        Partner = _high - 1;
                        Finish();
                        continue;
                    }

                    var half = (_low + _high) / 2;
                    if (!Ask(Merge(First(half), _alone), out var pairFails)) return;
                    if (pairFails) _high = half;
                    else _low = half;
                    continue;

                default:
                    _next = null;
                    return;
            }
        }
    }

    private void Finish() => _phase = Phase.Done;

    private bool Ask(IReadOnlyList<int> set, out bool failed)
    {
        if (_answers.TryGetValue(Key(set), out failed)) return true;

        _next = set;
        return false;
    }

    private static List<int> First(int count) => [.. Enumerable.Range(0, count)];

    private static List<int> Merge(IEnumerable<int> one, IEnumerable<int> other) => [.. one.Concat(other).Distinct().Order()];

    private List<int> WithNeeds(int mod)
    {
        var seen = new HashSet<int> { mod };
        var open = new Queue<int>([mod]);

        while (open.Count > 0)
            foreach (var needed in _needs(open.Dequeue()))
                if (needed >= 0 && needed < _count && seen.Add(needed)) open.Enqueue(needed);

        return [.. seen.Order()];
    }

    private static string Key(IEnumerable<int> set) => string.Join(',', set);

    private static int Halvings(int span)
    {
        var tries = 0;
        while (span > 1)
        {
            span = (span + 1) / 2;
            tries++;
        }

        return tries;
    }
}
