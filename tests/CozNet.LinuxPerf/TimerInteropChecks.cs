using System.Diagnostics;
using System.Text.Json;
using CozNet;
using Microsoft.FSharp.Core;

static class TimerInteropChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private static T Parsed<T>(FSharpResult<T, string> value) => value.IsOk ? value.ResultValue : throw new InvalidDataException(value.ErrorValue);

    private static (LedgerThread Peer, SignalServiceLease Lease, SignalAccounting State) Reserve()
    {
        var selected = Parsed(SignalAccountingModule.register(FSharpOption<int>.Some(50), SignalAccountingModule.create()));
        var peer = Parsed(SignalAccountingModule.register(FSharpOption<int>.None, selected.Item2));
        var identity = Parsed(SignalAccountingModule.sampleIdentity(selected.Item1, peer.Item2));
        var sample = Parsed(CpuSampleObservationModule.tryCreate(identity, 1, 100000, 0, SampleThreadState.Runnable, SampleLocation.Selected));
        var observed = Parsed(SignalAccountingModule.observe(selected.Item1, sample, peer.Item2));
        var credited = Parsed(SignalAccountingModule.prepare(selected.Item1, observed.Item2));
        Require(FSharpOption<SignalDelayRequest>.get_IsNone(credited.Item1), "Synthetic selected credit must not wait.");
        var reserved = Parsed(SignalAccountingModule.prepareService(peer.Item1, credited.Item2));
        return (peer.Item1, reserved.Item1.Value, reserved.Item2);
    }

    public static int Run(string path, string mode)
    {
        if (mode is not ("receipt" or "late" or "clock-failure")) throw new ArgumentException("Select receipt, late, or clock-failure.");
        NativeTimerClient.VerifyLayout();
        NativeTimerClient? client = null;
        Thread? target = null;
        var ready = new ManualResetEventSlim();
        int stop = 0;
        Exception? targetFailure = null;
        string? failure = null;
        ulong generation = 0;
        bool joined = false, closed = false, rejected = false, firstReceiptPreserved = false;
        bool accountingSettled = false, reservationRetained = false;
        long? unpaidDebt = null;
        TimerNativeCapture? firstRaw = null;
        IReadOnlyList<TargetLeaseReceivedChild>? received = null;
        try
        {
            client = new NativeTimerClient(1, 1, capacity: 8);
            var worker = new Thread(() =>
            {
                try
                {
                    client.RegisterCurrentThread(0);
                    ready.Set();
                    while (Volatile.Read(ref stop) == 0) Thread.SpinWait(128);
                }
                catch (Exception error) { Volatile.Write(ref targetFailure, error); }
                finally
                {
                    ready.Set();
                    try { client.RetireCurrentThread(0); }
                    catch (Exception error)
                    {
                        Volatile.Write(ref targetFailure, error);
                        Thread.Sleep(Timeout.Infinite);
                    }
                }
            }) { IsBackground = true };
            worker.Start();
            target = worker;
            Require(ready.Wait(TimeSpan.FromSeconds(2)), "The timer target did not register within two seconds.");
            Require(Volatile.Read(ref targetFailure) is null, "Timer target registration failed.");
            var reservation = Reserve();
            generation = client.Submit(0, 1, 50000);
            var accounting = Parsed(SignalAccountingModule.sentService(reservation.Lease, reservation.State));
            var adapter = new TimerAccountingAdapter(TimerAccountingBinding.Parse(reservation.Lease, generation, 1, 1, 1));
            var deadline = Stopwatch.StartNew();
            TimerNativeSnapshot header;
            do
            {
                header = client.Probe(0);
                Require(deadline.Elapsed < TimeSpan.FromSeconds(2), "The timer lease did not retire within two seconds.");
                if (header.Visibility != 3) Thread.Yield();
            } while (header.Visibility != 3);
            Require(header.Children == 1 && header.Measured == 0 && header.Stop == 2 && header.Error == 0 && header.BackendError == 0,
                "The controlled ineligible region must produce one deferred child without wait or error.");
            if (mode == "late") Thread.Sleep(150);
            try
            {
                var observation = client.Read(0, mode == "clock-failure" ? WrongThreadClock : null);
                received = observation.Received;
                var applied = adapter.Accept(observation, accounting);
                adapter = applied.Adapter;
                accounting = applied.State;
                accountingSettled = adapter.IsTerminal;
            }
            catch (InvalidDataException) { rejected = true; }
            firstRaw = client.LastRaw;
            Require(firstRaw is not null && firstRaw.Children.Length == 1, "The first raw child capture was not retained.");
            Require(rejected == (mode != "receipt"), "The receipt outcome differs from the selected mode.");
            if (mode == "receipt")
            {
                Require(received is { Count: 1 } && client.Receipts(0).FirstFailure is null, "The first managed receipt was not accepted.");
                var remaining = Parsed(SignalAccountingModule.prepareService(reservation.Peer, accounting));
                unpaidDebt = SignalServiceLeaseModule.nanoseconds(remaining.Item1.Value);
                Require(accountingSettled && adapter.ConfirmedNanoseconds == 0 && SignalAccountingModule.pendingCount(accounting) == 0
                    && unpaidDebt == 50000, "The deferred live journal must release its reservation without paying synthetic debt.");
                ulong firstUpper = received![0].FirstManagedReceiptUpper;
                Thread.Sleep(150);
                var later = client.Read(0);
                firstReceiptPreserved = later.Received.Count == 1 && later.Received[0].FirstManagedReceiptUpper == firstUpper
                    && later.Raw.Clock.Nanoseconds - later.Raw.Children[0].Armed > 100000000;
                Require(firstReceiptPreserved, "A later snapshot replaced or invalidated the original managed receipt.");
            }
            else
            {
                var originalFailure = client.Receipts(0).FirstFailure;
                Require(originalFailure is not null, "The receipt tracker did not retain its first rejection.");
                Require(mode == "late"
                    ? firstRaw!.Clock.Status == 0 && firstRaw.Clock.Nanoseconds - firstRaw.Children[0].Armed > 100000000
                    : firstRaw!.Clock.Status == 1 && firstRaw.Clock.Nanoseconds == 0,
                    "The failure did not come from the required native clock boundary.");
                bool stillRejected = false;
                try { client.Read(0); }
                catch (InvalidDataException) { stillRejected = true; }
                Require(stillRejected && ReferenceEquals(originalFailure, client.Receipts(0).FirstFailure),
                    "A later valid clock repaired a failed receipt tracker.");
                reservationRetained = SignalAccountingModule.pendingCount(accounting) == 1
                    && Parsed(SignalAccountingModule.confirmedServiceNanoseconds(reservation.Lease, accounting)) == 0;
                Require(reservationRetained && !accountingSettled, "Rejected live receipt evidence released its ledger reservation.");
            }
        }
        catch (Exception error) { failure = error.ToString(); }
        finally
        {
            Volatile.Write(ref stop, 1);
            joined = target is null || target.Join(TimeSpan.FromSeconds(2));
            if (joined) ready.Dispose();
            if (!joined) failure ??= "Target cleanup remains unresolved. Native ownership was retained.";
            if (Volatile.Read(ref targetFailure) is { } targetError) failure ??= targetError.ToString();
            if (joined && client is not null)
            {
                try
                {
                    if (generation != 0 || client.LastSubmission is { Generation: > 0 }) client.ReleaseAfterRetiredCopy(0);
                    client.CloseAfterThreadJoin();
                    closed = true;
                }
                catch (Exception error) { failure ??= error.ToString(); }
            }
        }
        bool passed = failure is null && joined && closed;
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            passed, mode, failure, joined, closed, generation, rejected, firstReceiptPreserved,
            firstRaw, lastRaw = client?.LastRaw, cleanup = client?.LastCleanup, received,
            accountingSettled, reservationRetained, unpaidDebt,
            generalClrSafetyEstablished = false, sampledCapacityEstablished = false, predictionAccuracyEstablished = false
        }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        return passed ? 0 : 1;
    }

    private static TimerClockRead WrongThreadClock(TimerSessionHandle session)
    {
        TimerClockRead result = new(-1, 0);
        var thread = new Thread(() =>
        {
            int status = TimerNativeMethods.Clock(session, out ulong nanoseconds);
            result = new(status, nanoseconds);
        }) { IsBackground = true };
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(2))) throw new InvalidOperationException("The rejected native clock call did not return.");
        return result;
    }
}
