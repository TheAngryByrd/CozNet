using System.Diagnostics;
using CozNet;
using Microsoft.FSharp.Core;

sealed class TimerSelectedWorker
{
    internal sealed record Payment(ulong Command, ulong Generation, long Budget, long Confirmed,
        TimerCommandInterval Interval, TimerObservation[] Snapshots);

    private readonly NativeTimerClient client;
    private readonly ulong begin, size;
    private readonly ManualResetEventSlim ready = new();
    private readonly Thread worker;
    private int stop;
    private readonly TimerWorkerLifetime lifetime = new();
    private long progress;
    private ulong command, outstanding;
    private readonly List<Payment> payments = [];
    private readonly List<TimerObservation> pendingSnapshots = [];
    public IReadOnlyList<Payment> Payments => payments;
    public IReadOnlyList<TimerObservation> PendingSnapshots => pendingSnapshots;
    public TimerCommandInterval? PendingInterval { get; private set; }
    public bool Joined => lifetime.Joined;
    public bool Retired => lifetime.Retired;
    public bool Released => lifetime.Released;
    public long Progress => Interlocked.Read(ref progress);
    public string? Failure => lifetime.Error?.ToString();

    public TimerSelectedWorker(NativeTimerClient client, ulong begin, ulong size)
    {
        (this.client, this.begin, this.size) = (client, begin, size);
        worker = new Thread(() => lifetime.Run(
            () => client.RegisterCurrentThread(1),
            ready.Set,
            () =>
            {
                while (Volatile.Read(ref stop) == 0)
                {
                    if (CpuWorkload.Run() != 49995000L)
                        throw new InvalidDataException("Selected timer payment changed the arithmetic checksum.");
                    Interlocked.Increment(ref progress);
                }
            },
            () => client.RetireCurrentThread(1),
            () => Thread.Sleep(Timeout.Infinite))) { IsBackground = true };
        worker.Start();
    }

    public void AwaitReady()
    {
        Require(ready.Wait(TimeSpan.FromSeconds(2)) && lifetime.Error is null,
            "The selected timer target did not register.");
    }

    public void Pay(LedgerThread selected, [System.Diagnostics.CodeAnalysis.NotNull] ref SignalAccounting? state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            Require(lifetime.Error is null, "The selected timer target failed.");
            var reserved = Parsed(SignalAccountingModule.prepareSelectedService(selected, state!));
            state = reserved.Item2;
            if (FSharpOption<SignalServiceLease>.get_IsNone(reserved.Item1)) return;
            Require(deadline.Elapsed < TimeSpan.FromSeconds(2) && payments.Count < 4096,
                "Selected timer debt did not settle within its bounded service window.");
            var lease = reserved.Item1.Value;
            long budget = SignalServiceLeaseModule.nanoseconds(lease);
            command = checked(command + 1);
            try { outstanding = client.Submit(1, command, checked((ulong)budget)); }
            finally
            {
                if (client.LastSubmission is { Generation: > 0 } submission) outstanding = submission.Generation;
            }
            long submitted = Stopwatch.GetTimestamp();
            PendingInterval = new(outstanding, submitted, 0);
            state = Parsed(SignalAccountingModule.sentService(lease, state));
            var adapter = new TimerAccountingAdapter(TimerAccountingBinding.Parse(lease, outstanding, command, begin, size));
            ulong children = ulong.MaxValue, visibility = ulong.MaxValue;
            while (true)
            {
                Require(deadline.Elapsed < TimeSpan.FromSeconds(2), "The selected timer lease did not retire within two seconds.");
                var header = client.Probe(1);
                if (header.Children != children || header.Visibility != visibility || header.BackendError != 0)
                {
                    var observation = client.Read(1);
                    pendingSnapshots.Add(observation);
                    if (!adapter.IsTerminal)
                    {
                        var applied = adapter.Accept(observation, state);
                        adapter = applied.Adapter;
                        state = applied.State;
                    }
                    Require(observation.Raw.Header.BackendError == 0, "The selected timer backend failed after settlement.");
                    children = observation.Raw.Header.Children;
                    visibility = observation.Raw.Header.Visibility;
                }
                if (visibility == 3) break;
                Thread.Yield();
            }
            PendingInterval = new(outstanding, submitted, Stopwatch.GetTimestamp());
            Require(adapter.IsTerminal && SignalAccountingModule.pendingCount(state) == 0,
                "The selected timer journal did not settle exactly once.");
            payments.Add(new(command, outstanding, budget, adapter.ConfirmedNanoseconds, PendingInterval.Value, pendingSnapshots.ToArray()));
            client.ReleaseAfterRetiredCopy(1);
            outstanding = 0;
            PendingInterval = null;
            pendingSnapshots.Clear();
        }
    }

    public void StopAndJoin()
    {
        lifetime.StopAndJoin(
            () => Volatile.Write(ref stop, 1),
            () => worker.Join(TimeSpan.FromSeconds(2)),
            ready.Dispose,
            () => { if (outstanding != 0) client.ReleaseAfterRetiredCopy(1); });
    }

    private static T Parsed<T>(FSharpResult<T, string> result) => result.IsOk ? result.ResultValue : throw new InvalidDataException(result.ErrorValue);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
