using System.Diagnostics;

static class TimerTransportPreparation
{
    internal sealed record Result(string Source, uint Slot, bool Passed, bool Joined, bool Retired, bool Released,
        TimerObservation[] Observations, TimerNativeCapture? LastRaw, string[] Failures);

    public static Result Run(NativeTimerClient client, uint slot, ulong begin, ulong size)
    {
        var observations = new List<TimerObservation>();
        var failures = new List<Exception>();
        var lifetime = new TimerWorkerLifetime();
        var ready = new ManualResetEventSlim();
        var stop = new ManualResetEventSlim();
        Thread? worker = null;
        bool submitted = false;
        try
        {
            worker = new Thread(() => lifetime.Run(() => client.RegisterCurrentThread(slot), ready.Set,
                () =>
                {
                    if (!stop.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Native timer initialization did not stop.");
                }, () => client.RetireCurrentThread(slot), () => Thread.Sleep(Timeout.Infinite))) { IsBackground = true };
            worker.Start();
            Require(ready.Wait(TimeSpan.FromSeconds(2)) && lifetime.Error is null, "Native timer initialization did not register.");
            try { client.Submit(slot, 1, 50000); }
            finally { submitted = client.LastSubmission is { Generation: > 0 }; }
            var deadline = Stopwatch.StartNew();
            while (true)
            {
                Require(deadline.ElapsedMilliseconds <= 100 && observations.Count < 1024,
                    "Native timer initialization exceeded its receipt window.");
                var header = client.Probe(slot);
                if (header.Children != 0 || header.Visibility != 0 || header.BackendError != 0)
                {
                    var observation = client.Read(slot);
                    observations.Add(observation);
                    Require(header.BackendError == 0, "Native timer initialization reported a backend failure.");
                    if (observation.Raw.Header.Visibility == 3)
                    {
                        var terminal = observation.Raw.Header;
                        var child = observation.Received.Single().Raw;
                        Require(terminal.Stop == 2 && terminal.Measured == 0 && terminal.Children == 1
                            && terminal.Error == 0 && terminal.TransportError == 0 && terminal.BackendError == 0
                            && terminal.Generation == 1 && terminal.Command == 1 && terminal.Budget == 50000
                            && terminal.MayHaveDelivery == 0 && child.Valid && child.WaitStatus == 0 && child.Waited == 0
                            && child.Ordinal == 1 && child.Requested == 50000 && child.Started >= child.Armed
                            && child.Finished >= child.Started && child.Finished - child.Started <= 5000000
                            && (child.InstructionPointer < begin || child.InstructionPointer - begin >= size),
                            "Native timer initialization must defer one child without wait.");
                        break;
                    }
                }
                Thread.Yield();
            }
        }
        catch (Exception error) { failures.Add(error); }
        finally
        {
            if (worker is not null)
            {
                try
                {
                    lifetime.StopAndJoin(stop.Set, () => worker.Join(TimeSpan.FromSeconds(2)), ready.Dispose,
                        () => { if (submitted) client.ReleaseAfterRetiredCopy(slot); });
                }
                catch (Exception error) { failures.Add(error); }
                if (lifetime.Joined && lifetime.Released)
                {
                    stop.Dispose();
                }
            }
        }
        return new("synthetic-command/native-deferred/dedicated-slot", slot, failures.Count == 0 && lifetime.Released,
            lifetime.Joined, lifetime.Retired, lifetime.Released, observations.ToArray(), client.LastRaw,
            failures.Select(error => error.ToString()).ToArray());
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
