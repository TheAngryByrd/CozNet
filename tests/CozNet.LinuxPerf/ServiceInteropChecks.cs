using System.Diagnostics;
using System.Text.Json;

static class ServiceInteropChecks
{
    public static int Run(string path, bool delayedReceipt)
    {
        NativeServiceClient? client = null;
        ServiceNativeObservation? observation = null;
        Exception? workerError = null;
        string? failure = null;
        bool submitted = false, released = false, joined = false, closed = false, receiptRejected = false, workerStarted = false;
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
            var identity = Guid.NewGuid();
            client.Submit(1, identity, 1, 110000);
            submitted = true;
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
                managedReceiptBoundNanoseconds = 100000000, generalClrSafetyEstablished = false, predictionAccuracyEstablished = false
            }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        }
        return failure is null ? 0 : 1;
    }

    private static ServiceReceiptTiming ParseReceipt(ReceivedServiceChild child) => ServiceReceiptTiming.Parse(
        child.Native.Acknowledgment.Started, child.Native.Acknowledgment.Observed,
        child.FirstSnapshotCaptured, child.FirstManagedReceiptUpper, child.ClockStatus);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
