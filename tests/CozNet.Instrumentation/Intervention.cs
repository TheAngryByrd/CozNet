using System.Diagnostics;
using Microsoft.FSharp.Core;

namespace CozNet.Instrumentation;

public static partial class Workload
{
    [ThreadStatic]
    private static Action? interventionProbe;

    private static T Parsed<T>(FSharpResult<T, string> result)
    {
        if (result.IsError) throw new InvalidOperationException(result.ErrorValue);
        return result.ResultValue;
    }

    private sealed record InterventionObservation(long RequestedTicks, long MeasuredWaitTicks,
        long GlobalDelayTicks, long Result, long Probes, int ReachedProbe, long StopwatchFrequency,
        ulong SyntheticCpuNanoseconds, int SpeedupPercent);

    private static InterventionObservation VerifyIntervention(long credit, ProbeSettings settings)
    {
        int trigger = settings.Interval == 1 ? 10000 : 10;
        var runtime = new CooperativeRuntime(DelayRuntimeTimingModule.real);
        var selected = Parsed(runtime.Register(null));
        int speedupPercent = credit == 0 ? 0 : 50;
        const ulong syntheticCpuNanoseconds = 40000000;
        var stream = Parsed(runtime.AttachSamples(selected, speedupPercent, 0, 0));
        using var reached = new ManualResetEventSlim();
        using var published = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var completion = new TaskCompletionSource<InterventionObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        probes = 0;
        var worker = new Thread(() =>
        {
            try
            {
                var handle = Parsed(runtime.Register(null));
                int visited = 0;
                long measured = 0;
                interventionProbe = () =>
                {
                    visited++;
                    if (visited != trigger) return;
                    reached.Set();
                    Require(published.Wait(TimeSpan.FromSeconds(5)), "The controller did not publish credit.");
                    long start = Stopwatch.GetTimestamp();
                    Parsed(runtime.Check(handle, cancellation.Token));
                    measured = Stopwatch.GetTimestamp() - start;
                    interventionProbe = null;
                };
                long result = Work(1000000);
                Require(result == 499999500000, $"Intervention changed the arithmetic: {result}.");
                Require(visited == trigger, $"Expected the loop intervention at probe {trigger}, got {visited}.");
                Require(probes == settings.ExpectedCalls(1000000), $"Intervention changed the probe count: {probes}.");
                Require(measured >= credit, $"Probe paid {measured} ticks for a debt of {credit}.");
                Parsed(runtime.Unregister(handle));
                long global = DelayTicksModule.value(runtime.GlobalDelay);
                Require(global >= credit, $"Global delay {global} lost the injected credit {credit}.");
                Require(credit != 0 || global == 0, $"A zero-credit probe created {global} ticks of global delay.");
                completion.SetResult(new InterventionObservation(credit, measured, global, result, probes,
                    visited, Stopwatch.Frequency, syntheticCpuNanoseconds, speedupPercent));
            }
            catch (Exception error) { completion.SetException(error); }
            finally { interventionProbe = null; }
        }) { IsBackground = true };
        worker.Start();
        Exception? controllerError = null;
        try
        {
            Require(reached.Wait(TimeSpan.FromSeconds(5)), "The long loop did not reach its intervention probe.");
            var sampleObservation = Parsed(CpuSampleObservationModule.tryCreate(stream, 1, syntheticCpuNanoseconds,
                0, SampleThreadState.Runnable, SampleLocation.Selected));
            var converted = Parsed(runtime.CreditObservation(selected, sampleObservation));
            Require(DelayTicksModule.value(converted) == credit, "Synthetic CPU credit converted to the wrong ledger duration.");
            Parsed(runtime.Check(selected, cancellation.Token));
        }
        catch (Exception error) { controllerError = error; }
        finally { published.Set(); }
        Require(worker.Join(TimeSpan.FromSeconds(10)), "The intervention worker exceeded its completion limit.");
        if (controllerError is not null) throw controllerError;
        var observation = completion.Task.GetAwaiter().GetResult();
        Parsed(runtime.Check(selected, cancellation.Token));
        Parsed(runtime.Unregister(selected));
        return observation;
    }
}
