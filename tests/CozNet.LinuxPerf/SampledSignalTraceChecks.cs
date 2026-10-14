using System.Diagnostics;
using System.Diagnostics.Tracing;

internal static class SampledSignalTraceChecks
{
    private sealed class MarkerListener : EventListener
    {
        public List<(int Id, string Phase, long Timestamp)> Markers { get; } = new();
        public List<string> Errors { get; } = new();

        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "CozNet-SampledSignals")
                EnableEvents(source, EventLevel.Informational);
        }

        protected override void OnEventWritten(EventWrittenEventArgs value)
        {
            if (value.EventSource.Name != "CozNet-SampledSignals") return;
            if (value.EventId == 1 && value.Payload is { Count: 2 } payload
                && payload[0] is int id && payload[1] is string phase)
                Markers.Add((id, phase, Stopwatch.GetTimestamp()));
            else Errors.Add($"Unexpected marker event {value.EventId}.");
        }
    }

    public static void Run()
    {
        ControllerCpuBracketsEncloseRead();
        DisabledProviderFails();
        EnabledMarkersMatchBrackets();
        DisabledProviderFails();
    }

    private sealed class RecordedClock(long value) : IThreadCpuClock
    {
        public long Timestamp { get; private set; }
        public long ReadNanoseconds()
        {
            Timestamp = Stopwatch.GetTimestamp();
            return value;
        }
    }

    private static void ControllerCpuBracketsEncloseRead()
    {
        var clock = new RecordedClock(123);
        var snapshot = ControllerCpuSnapshot.Capture(clock);
        if (snapshot.CpuNanoseconds != 123 || snapshot.BeforeTimestamp > clock.Timestamp || snapshot.AfterTimestamp < clock.Timestamp)
            throw new InvalidOperationException("Controller CPU timestamps must enclose the clock read.");
        bool rejected = false;
        try { ControllerCpuSnapshot.Capture(new RecordedClock(-1)); }
        catch (InvalidOperationException) { rejected = true; }
        if (!rejected) throw new InvalidOperationException("A negative controller CPU observation must fail.");
    }

    private static void DisabledProviderFails()
    {
        var trace = new SampledSignalTrace();
        bool rejected = false;
        try { trace.Mark("run-start"); }
        catch (InvalidOperationException) { rejected = true; }
        if (!rejected || trace.Anchors.Count != 0)
            throw new InvalidOperationException("A disabled marker provider must fail without recording an anchor.");
    }

    private static void EnabledMarkersMatchBrackets()
    {
        using var listener = new MarkerListener();
        var trace = new SampledSignalTrace();
        string[] phases = ["run-start", "measurement-start", "measurement-end", "run-end"];
        foreach (string phase in phases) trace.Mark(phase);
        if (listener.Errors.Count != 0 || listener.Markers.Count != phases.Length || trace.Anchors.Count != phases.Length)
            throw new InvalidOperationException("The marker provider must emit each typed event without EventSource errors.");
        for (int i = 0; i < phases.Length; i++)
        {
            var marker = listener.Markers[i];
            var anchor = trace.Anchors[i];
            if (marker.Id != i || anchor.Id != i || marker.Phase != phases[i] || anchor.Phase != phases[i]
                || marker.Timestamp < anchor.BeforeTimestamp || marker.Timestamp > anchor.AfterTimestamp
                || (i > 0 && anchor.BeforeTimestamp < trace.Anchors[i - 1].AfterTimestamp))
                throw new InvalidOperationException($"Marker {i} does not match its ordered clock bracket.");
        }
    }
}
