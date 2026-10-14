using CozNet;
using Microsoft.FSharp.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

static class ServiceAccountingCosts
{
    private sealed record Case(string Mode, int Children, ServiceAccountingAdapter Adapter,
        SignalAccounting State, ServiceNativeObservation Observation, long ExpectedWait, bool Terminal);
    private sealed record Measurement(string Mode, int Children, int Batch, int Iterations,
        long ElapsedTicks, long AllocatedBytes, int Gen0, int Gen1, int Gen2, long Checksum);
    private static T Parsed<T>(FSharpResult<T, string> result) => result.IsOk
        ? result.ResultValue : throw new InvalidOperationException(result.ErrorValue);

    private static Case[] Cases(int count)
    {
        var selected = Parsed(SignalAccountingModule.register(FSharpOption<int>.Some(50), SignalAccountingModule.create()));
        var peer = Parsed(SignalAccountingModule.register(FSharpOption<int>.None, selected.Item2));
        var identity = Parsed(SignalAccountingModule.sampleIdentity(selected.Item1, peer.Item2));
        var sample = Parsed(CpuSampleObservationModule.tryCreate(identity, 1, 220000, 0,
            SampleThreadState.Runnable, SampleLocation.Selected));
        var observed = Parsed(SignalAccountingModule.observe(selected.Item1, sample, peer.Item2)).Item2;
        var credited = Parsed(SignalAccountingModule.prepare(selected.Item1, observed)).Item2;
        var reserved = Parsed(SignalAccountingModule.prepareService(peer.Item1, credited));
        var lease = reserved.Item1.Value;
        var state = Parsed(SignalAccountingModule.sentService(lease, reserved.Item2));
        var adapter = new ServiceAccountingAdapter(lease, 1, ip => ip == 100,
            (outcome, result) => outcome is 1 or 2 && result == 0);
        var parts = NativeServiceClient.Parts(SignalServiceLeaseModule.identity(lease));
        ReceivedServiceChild Child(int index, bool terminal)
        {
            ulong start = (ulong)(index + 1) * 1000000;
            var ack = new ServiceNativeAcknowledgment { Sequence = (ulong)index + 1,
                Outcome = terminal ? 1UL : 2UL, Ip = 100, Handler = 1000, Wait = terminal ? 0UL : 1000UL,
                Started = start, Queued = start + 1, HandlerStarted = start + 2,
                HandlerFinished = start + 1002, Observed = start + 1003 };
            return new(new ServiceNativeChild { Ordinal = (ulong)index + 1,
                Requested = (ulong)Math.Min(50000, 110000 - index * 1000), Acknowledgment = ack },
                start + 1004, start + 1005, 0);
        }
        var children = Enumerable.Range(0, count).Select(i => Child(i, false)).ToArray();
        ServiceNativeObservation Observation(bool terminal) => new(new ServiceNativeSnapshot {
            IdentityFirst = parts.First, IdentitySecond = parts.Second, Command = 1, Budget = 110000,
            Measured = (ulong)count * 1000, Phase = terminal ? 2UL : 1UL,
            Children = (ulong)count + (terminal ? 1UL : 0), Captured = 100000000 },
            terminal ? [.. children, Child(count, true)] : children, []);
        var prefix = Observation(false);
        var accepted = adapter.Accept(prefix, state);
        return [new("new-prefix", count, adapter, state, prefix, count * 1000, false),
            new("repeated-prefix", count, accepted.Adapter, accepted.State, prefix, count * 1000, false),
            new("terminal-suffix", count + 1, accepted.Adapter, accepted.State, Observation(true), count * 1000, true)];
    }

    public static int Run(string path)
    {
        var measurements = new List<Measurement>();
        string? failure = null;
        const int iterations = 2000;
        try
        {
            foreach (int count in new[] { 1, 8, 32, 64 })
            {
                var cases = Cases(count);
                foreach (var item in cases)
                    for (int i = 0; i < 256; i++) Check(item, item.Adapter.Accept(item.Observation, item.State));
                for (int batch = 0; batch < 5; batch++)
                    for (int order = 0; order < cases.Length; order++)
                    {
                        var item = cases[(order + batch) % cases.Length];
                        long checksum = 0;
                        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
                        long allocated = GC.GetAllocatedBytesForCurrentThread();
                        long started = Stopwatch.GetTimestamp();
                        for (int i = 0; i < iterations; i++)
                        {
                            var result = item.Adapter.Accept(item.Observation, item.State);
                            Check(item, result);
                            checksum += result.Adapter.ConfirmedNanoseconds + result.Adapter.AcceptedCount;
                        }
                        long elapsed = Stopwatch.GetTimestamp() - started;
                        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                        measurements.Add(new(item.Mode, item.Children, batch, iterations, elapsed, bytes,
                            GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1,
                            GC.CollectionCount(2) - gen2, checksum));
                    }
            }
        }
        catch (Exception error) { failure = error.ToString(); }
        File.WriteAllText(path, JsonSerializer.Serialize(new { runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            stopwatchFrequency = Stopwatch.Frequency, warmupIterations = 256, iterations, batches = 5,
            failure, measurements, nativeSnapshotMeasured = false, sampledCapacityEstablished = false,
            predictionAccuracyEstablished = false }, new JsonSerializerOptions { WriteIndented = true }));
        return failure is null ? 0 : 1;
    }

    private static void Check(Case item, (ServiceAccountingAdapter Adapter, SignalAccounting State) result)
    {
        if (result.Adapter.AcceptedCount != item.Children || result.Adapter.ConfirmedNanoseconds != item.ExpectedWait
            || result.Adapter.IsTerminal != item.Terminal
            || SignalAccountingModule.pendingCount(result.State) != (item.Terminal ? 0 : 1))
            throw new InvalidOperationException($"The {item.Mode} journal with {item.Children} children produced an unexpected result.");
    }
}
