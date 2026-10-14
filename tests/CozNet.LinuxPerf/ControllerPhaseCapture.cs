sealed class ControllerPhaseCapture(IThreadCpuClock clock)
{
    internal sealed record Entry(string Stage, string Phase, int Iteration, ControllerCpuSnapshot Before,
        ControllerCpuSnapshot After, bool Completed);
    private sealed record Active(string Stage, string Phase, int Iteration, ControllerCpuSnapshot Before);
    private readonly List<Entry> entries = new(128);
    private Active? active;
    public IReadOnlyList<Entry> Entries => entries;

    public void Begin(string stage, string phase, int iteration)
    {
        if (active is not null) throw new InvalidOperationException("A controller phase is already active.");
        if (entries.Count == 128) return;
        active = new(stage, phase, iteration, ControllerCpuSnapshot.Capture(clock));
    }

    public void End(bool completed = true)
    {
        if (active is null) return;
        var after = ControllerCpuSnapshot.Capture(clock);
        if (after.CpuNanoseconds < active.Before.CpuNanoseconds)
            throw new InvalidDataException("The controller phase CPU clock regressed.");
        entries.Add(new(active.Stage, active.Phase, active.Iteration, active.Before, after, completed));
        active = null;
    }
}
