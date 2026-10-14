sealed class ControllerPhaseCapture(IThreadCpuClock clock, bool retainRecent = false)
{
    internal sealed record Entry(string Stage, string Phase, int Iteration, ControllerCpuSnapshot Before,
        ControllerCpuSnapshot After, bool Completed);
    private sealed record Active(string Stage, string Phase, int Iteration, ControllerCpuSnapshot Before);
    private readonly List<Entry> entries = new(128);
    private Active? active;
    private int next;
    public long Overwritten { get; private set; }
    public IReadOnlyList<Entry> Entries => next == 0 ? entries : entries.Skip(next).Concat(entries.Take(next)).ToArray();

    public void Begin(string stage, string phase, int iteration)
    {
        if (active is not null) throw new InvalidOperationException("A controller phase is already active.");
        if (entries.Count == 128 && !retainRecent) return;
        active = new(stage, phase, iteration, ControllerCpuSnapshot.Capture(clock));
    }

    public void End(bool completed = true, bool retain = true)
    {
        if (active is not { } current) return;
        active = null;
        var after = ControllerCpuSnapshot.Capture(clock);
        if (after.CpuNanoseconds < current.Before.CpuNanoseconds)
            throw new InvalidDataException("The controller phase CPU clock regressed.");
        if (retain || !completed)
        {
            var entry = new Entry(current.Stage, current.Phase, current.Iteration, current.Before, after, completed);
            if (entries.Count < 128) entries.Add(entry);
            else
            {
                entries[next] = entry;
                next = (next + 1) % 128;
                Overwritten++;
            }
        }
    }
}
