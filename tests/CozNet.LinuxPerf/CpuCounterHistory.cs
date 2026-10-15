using System.Collections;

sealed class CpuCounterHistory : IReadOnlyList<CpuCounterTransition>
{
    private readonly CpuCounterHistory? previous;
    private readonly CpuCounterTransition transition;
    public static CpuCounterHistory Empty { get; } = new();
    public int Count { get; }

    private CpuCounterHistory() { }

    private CpuCounterHistory(CpuCounterHistory previous, CpuCounterTransition transition)
    {
        this.previous = previous;
        this.transition = transition;
        Count = checked(previous.Count + 1);
    }

    public CpuCounterHistory Append(CpuCounterTransition value) => new(this, value);

    public CpuCounterTransition this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            var node = this;
            while (node.Count - 1 != index) node = node.previous!;
            return node.transition;
        }
    }

    public IEnumerator<CpuCounterTransition> GetEnumerator()
    {
        var values = new CpuCounterTransition[Count];
        for (var node = this; node.previous is not null; node = node.previous)
            values[node.Count - 1] = node.transition;
        return ((IEnumerable<CpuCounterTransition>)values).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static IReadOnlyList<CpuCounterTransition> Freeze(IReadOnlyList<CpuCounterTransition> values) =>
        values is CpuCounterHistory ? values : Array.AsReadOnly(values.ToArray());

    public static bool ExtendsByOne(IReadOnlyList<CpuCounterTransition> values, IReadOnlyList<CpuCounterTransition> prefix) =>
        values.Count == (long)prefix.Count + 1
        && (values is CpuCounterHistory history && ReferenceEquals(history.previous, prefix)
            || values.Take(prefix.Count).SequenceEqual(prefix));

    public static bool Same(IReadOnlyList<CpuCounterTransition> left, IReadOnlyList<CpuCounterTransition> right) =>
        ReferenceEquals(left, right) || left.SequenceEqual(right);
}
