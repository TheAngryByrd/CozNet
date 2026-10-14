using System.Diagnostics;
using System.Text.Json;
using CozNet;
using Microsoft.FSharp.Core;

static class ServiceInteropChecks
{
    public static int Run(string path, bool delayedReceipt)
    {
        NativeServiceClient? client = null;
        ServiceNativeObservation? observation = null;
        Exception? workerError = null;
        string? failure = null;
        bool submitted = false, released = false, joined = false, closed = false, receiptRejected = false, workerStarted = false;
        SignalAccounting? accounting = null;
        SignalServiceLease? lease = null;
        bool accountingSettled = false, accountingRejected = false;
        Thread? worker = null;
        var ready = new ManualResetEventSlim();
        var stop = new ManualResetEventSlim();
        try
        {
            client = new NativeServiceClient(1, 1, SignalWaitMode.Spin, 64);
            worker = new Thread(() =>
            {
                try
                {
                    client.RegisterCurrentThread(1);
                    ready.Set();
                    stop.Wait();
                    client.RetireCurrentThread(1);
                }
                catch (Exception error) { workerError = error; ready.Set(); }
            }) { IsBackground = true };
            worker.Start();
            workerStarted = true;
            Require(ready.Wait(TimeSpan.FromSeconds(2)) && workerError is null, "The service target did not register.");
            (lease, accounting) = ReservePeer();
            var identity = SignalServiceLeaseModule.identity(lease);
            var adapter = new ServiceAccountingAdapter(lease, 1, client.IsEligibleAddress, client.IsValidOutcome);
            client.Submit(1, identity, 1, checked((ulong)SignalServiceLeaseModule.nanoseconds(lease)));
            submitted = true;
            accounting = Parsed(SignalAccountingModule.sentService(lease, accounting));
            if (delayedReceipt) Thread.Sleep(150);
            var deadline = Stopwatch.StartNew();
            do
            {
                observation = client.Read(1, identity);
                if (observation.Header.Phase != 1 && observation.Header.Outstanding == 0) break;
                Thread.Yield();
            } while (deadline.Elapsed < TimeSpan.FromSeconds(2));
            Require(observation.Header.Phase == 2 && observation.Header.Error == 0 && observation.Header.Outstanding == 0
                && observation.Children.Length == 1 && observation.Header.Measured == 0,
                "The service did not return one completed deferred child.");
            var child = observation.Children[0];
            Require(child.Native.Acknowledgment.Outcome == 1 && child.Native.Acknowledgment.Wait == 0,
                "The ineligible target received a measured wait.");
            try { ParseReceipt(child); }
            catch (InvalidDataException) { receiptRejected = true; }
            try
            {
                var applied = adapter.Accept(observation, accounting);
                accounting = applied.State;
                accountingSettled = applied.Adapter.IsTerminal;
            }
            catch (InvalidDataException) { accountingRejected = true; }
            if (!delayedReceipt && !receiptRejected)
            {
                Thread.Sleep(150);
                var repeated = client.Read(1, identity);
                Require(repeated.Header.Captured >= child.FirstManagedReceiptUpper
                    && repeated.Header.Captured - child.FirstManagedReceiptUpper > 100000000,
                    "The repeated snapshot must contain a fresh native clock after the receipt bound.");
                Require(repeated.Children[0].FirstManagedReceiptUpper == child.FirstManagedReceiptUpper
                    && repeated.Children[0].FirstSnapshotCaptured == child.FirstSnapshotCaptured,
                    "A repeated snapshot replaced the first managed receipt.");
                ParseReceipt(repeated.Children[0]);
            }
            client.Release(1, identity);
            released = true;
            Require(receiptRejected == delayedReceipt, "The managed receipt result differs from the fixture's expected result.");
            Require(accountingRejected == delayedReceipt && accountingSettled != delayedReceipt,
                "The ledger adapter did not preserve the receipt gate.");
            Require(SignalAccountingModule.pendingCount(accounting) == (delayedReceipt ? 1 : 0),
                "The ledger reservation did not match settlement or rejection.");
            if (delayedReceipt)
                Require(Parsed(SignalAccountingModule.confirmedServiceNanoseconds(lease, accounting)) == 0,
                    "A rejected receipt must not publish measured wait.");
            else
            {
                var remaining = Parsed(SignalAccountingModule.prepareService(SignalServiceLeaseModule.thread(lease), accounting));
                Require(remaining.Item1 is not null && SignalServiceLeaseModule.nanoseconds(remaining.Item1.Value) == 110000,
                    "The deferred native child must leave the original debt unpaid.");
            }
        }
        catch (Exception error) { failure = error.ToString(); }
        finally
        {
            if (!submitted || released)
            {
                stop.Set();
                joined = !workerStarted || worker!.Join(TimeSpan.FromSeconds(2));
                if (joined && client is not null)
                {
                    try { client.CloseAfterThreadJoin(); closed = true; }
                    catch (Exception error) { failure ??= error.ToString(); }
                }
            }
            if (workerError is not null) failure ??= workerError.ToString();
            if (!joined || !closed) failure ??= "Service ownership remains retained for process termination.";
            if (joined) { ready.Dispose(); stop.Dispose(); }
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                expectedReceiptRejection = delayedReceipt, receiptRejected, joined, closed, failure, observation,
                accountingRejected, accountingSettled,
                accountingPending = accounting is null ? (int?)null : SignalAccountingModule.pendingCount(accounting),
                managedReceiptBoundNanoseconds = 100000000, generalClrSafetyEstablished = false, predictionAccuracyEstablished = false
            }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        }
        return failure is null ? 0 : 1;
    }

    private static ServiceReceiptTiming ParseReceipt(ReceivedServiceChild child) => ServiceReceiptTiming.Parse(
        child.Native.Acknowledgment.Started, child.Native.Acknowledgment.Observed,
        child.FirstSnapshotCaptured, child.FirstManagedReceiptUpper, child.ClockStatus);

    private static (SignalServiceLease Lease, SignalAccounting State) ReservePeer()
    {
        var selected = Parsed(SignalAccountingModule.register(FSharpOption<int>.Some(50), SignalAccountingModule.create()));
        var peer = Parsed(SignalAccountingModule.register(FSharpOption<int>.None, selected.Item2));
        var stream = Parsed(SignalAccountingModule.sampleIdentity(selected.Item1, peer.Item2));
        var sample = Parsed(CpuSampleObservationModule.tryCreate(stream, 1, 220000, 0, SampleThreadState.Runnable, SampleLocation.Selected));
        var state = Parsed(SignalAccountingModule.observe(selected.Item1, sample, peer.Item2)).Item2;
        state = Parsed(SignalAccountingModule.prepare(selected.Item1, state)).Item2;
        var service = Parsed(SignalAccountingModule.prepareService(peer.Item1, state));
        return (service.Item1.Value, service.Item2);
    }

    private static T Parsed<T>(FSharpResult<T, string> value) => value.IsOk ? value.ResultValue : throw new InvalidDataException(value.ErrorValue);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
