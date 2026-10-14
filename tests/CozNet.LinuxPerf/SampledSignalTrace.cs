using System.Diagnostics;
using System.Diagnostics.Tracing;

internal sealed record SignalClockAnchor(int Id, string Phase, long BeforeTimestamp, long AfterTimestamp);
internal sealed record SignalTraceAbort(int AnchorId, long FailureTimestamp, string Stage);

[EventSource(Name = "CozNet-SampledSignals")]
internal sealed class SampledSignalEvents : EventSource
{
    internal static readonly SampledSignalEvents Log = new();

    [Event(1, Level = EventLevel.Informational)]
    public void ClockAnchor(int id, string phase) => WriteEvent(1, id, phase);
}

internal sealed class SampledSignalTrace
{
    private readonly List<SignalClockAnchor> anchors = new(16);
    public IReadOnlyList<SignalClockAnchor> Anchors => anchors;
    public SignalTraceAbort? AbortBoundary { get; private set; }

    public void Abort(long failureTimestamp, string stage)
    {
        if (AbortBoundary is not null || anchors.Count == 0 || failureTimestamp < anchors[^1].AfterTimestamp
            || failureTimestamp > Stopwatch.GetTimestamp() || anchors[^1].Phase == "run-end" || string.IsNullOrWhiteSpace(stage))
            throw new InvalidOperationException("The trace abort boundary is invalid or duplicated.");
        Mark("run-abort");
        AbortBoundary = new(anchors[^1].Id, failureTimestamp, stage);
    }

    public void Mark(string phase)
    {
        if (!SampledSignalEvents.Log.IsEnabled(EventLevel.Informational, EventKeywords.None))
            throw new InvalidOperationException("Enable the CozNet-SampledSignals provider before running the traced workload.");
        int id = anchors.Count;
        long before = Stopwatch.GetTimestamp();
        SampledSignalEvents.Log.ClockAnchor(id, phase);
        long after = Stopwatch.GetTimestamp();
        anchors.Add(new(id, phase, before, after));
    }
}
