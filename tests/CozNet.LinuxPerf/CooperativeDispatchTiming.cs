using System.Diagnostics;

enum CooperativeDispatchOperation
{
    Create, PeerSubmit, PeerHeader, PeerRead, PeerRelease, SelectedSubmit, SelectedHeader,
    SelectedRead, SelectedRelease, PeerFailureCapture, SelectedFailureCapture, BeginCleanup,
    SelectedCleanupHeader, SelectedCleanupRelease, PeerCleanupHeader, PeerCleanupRelease, Close
}

sealed record CooperativeDispatchObservation(long Sequence, CooperativeDispatchOperation Operation,
    ulong Command, ulong Generation, long Submitted, long? Started, long? Finished, long Returned, bool Failed);

sealed class CooperativeDispatchTiming
{
    private sealed class Active
    {
        public long? Started;
        public long? Finished;
    }

    private readonly Func<long> readClock;
    private readonly int owner = Environment.CurrentManagedThreadId;
    private readonly Queue<CooperativeDispatchObservation> recent = new(128);
    private readonly List<CooperativeDispatchObservation> prefix = new(32);
    public int PrefixEvidenceVersion => 1;
    public IReadOnlyList<CooperativeDispatchObservation> Prefix => prefix.ToArray();
    public long Calls { get; private set; }
    public long Failed { get; private set; }
    public long Incomplete { get; private set; }
    public CooperativeDispatchObservation? Longest { get; private set; }
    public CooperativeDispatchObservation? LongestQueue { get; private set; }
    public CooperativeDispatchObservation? LongestAction { get; private set; }
    public CooperativeDispatchObservation? LongestReturn { get; private set; }
    public IReadOnlyList<CooperativeDispatchObservation> Recent => recent.ToArray();

    internal CooperativeDispatchTiming(Func<long> readClock) => this.readClock = readClock;

    public static CooperativeDispatchTiming? Parse(string? value) => value switch
    {
        null or "0" => null,
        "1" => new(Stopwatch.GetTimestamp),
        _ => throw new ArgumentException("COZNET_DISPATCH_TIMING must be 0 or 1.")
    };

    public T Run<T>(CooperativeDispatchOperation operation, ulong command, ulong generation,
        Func<T> action, Func<Func<T>, T> dispatch)
    {
        if (Environment.CurrentManagedThreadId != owner)
            throw new InvalidOperationException("Dispatch timing requires its owning thread.");
        var active = new Active();
        long submitted = readClock();
        bool failed = false;
        try
        {
            return dispatch(() =>
            {
                lock (active) active.Started = readClock();
                try { return action(); }
                finally { lock (active) active.Finished = readClock(); }
            });
        }
        catch { failed = true; throw; }
        finally
        {
            CooperativeDispatchObservation observation;
            lock (active)
                observation = new(++Calls, operation, command, generation, submitted,
                    active.Started, active.Finished, readClock(), failed);
            if (failed) Failed++;
            if (observation.Finished is null) Incomplete++;
            if (prefix.Count < 32) prefix.Add(observation);
            if (recent.Count == 128) recent.Dequeue();
            recent.Enqueue(observation);
            if (Longest is null || Duration(observation) > Duration(Longest)) Longest = observation;
            if (observation.Started is not null && (LongestQueue is null || QueueTime(observation) > QueueTime(LongestQueue)))
                LongestQueue = observation;
            if (observation.Finished is not null)
            {
                if (LongestAction is null || ActionTime(observation) > ActionTime(LongestAction)) LongestAction = observation;
                if (LongestReturn is null || ReturnTime(observation) > ReturnTime(LongestReturn)) LongestReturn = observation;
            }
        }
    }

    private static long Duration(CooperativeDispatchObservation item) => item.Returned - item.Submitted;
    private static long QueueTime(CooperativeDispatchObservation item) => item.Started!.Value - item.Submitted;
    private static long ActionTime(CooperativeDispatchObservation item) => item.Finished!.Value - item.Started!.Value;
    private static long ReturnTime(CooperativeDispatchObservation item) => item.Returned - item.Finished!.Value;
}
