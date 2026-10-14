readonly record struct TimerCommandInterval(ulong Generation, long SubmittedTimestamp, long ReceivedTimestamp);

static class TimerGcReach
{
    public static bool ParseMode(string mode) => mode switch
    {
        "gc" => true,
        "no-gc" => false,
        _ => throw new ArgumentException("Select gc or no-gc for the managed timer workload.")
    };

    public static bool ReachedCommand(IEnumerable<GcStressObservation> observations, IEnumerable<TimerCommandInterval> commands)
    {
        var captured = observations.ToArray();
        return commands.Any(command => command.Generation != 0
            && SampledGcStress.ReachedMeasurement(captured, command.SubmittedTimestamp, command.ReceivedTimestamp));
    }
}
