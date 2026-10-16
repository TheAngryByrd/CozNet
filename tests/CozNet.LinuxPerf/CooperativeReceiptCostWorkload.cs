using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

static class CooperativeReceiptCostWorkload
{
    private sealed record Observation(int Iteration, long Started, long Observed, long Accepted,
        int Children, long ConfirmedNanoseconds, bool Terminal, long RemainingNanoseconds);

    public static int Run(string outputPath)
    {
        var results = new List<Observation>(32);
        string? failure = null;
        try
        {
            for (int iteration = 0; iteration < 32; ++iteration)
            {
                var fixture = CooperativeManagedChecks.Reserve(1000000);
                var adapter = CooperativeManagedChecks.Adapter(fixture);
                var tracker = new CooperativeReceipts(CooperativeLeaseIdentity.Parse(1, 7, 500000));
                var children = new CooperativeNativeChild[10];
                ulong finished = 1000;
                for (int index = 0; index < children.Length; ++index)
                {
                    children[index] = new()
                    {
                        Ordinal = (ulong)index + 1, Requested = 50000, Armed = finished,
                        Started = finished + 1, Finished = finished + 50001, Waited = 50000,
                        Worker = 1, CodeVersion = 2, Site = 3, Valid = 1
                    };
                    finished = children[index].Finished;
                }
                var header = new CooperativeNativeSnapshot
                {
                    Generation = 1, Command = 7, Budget = 500000, Children = 10, Measured = 500000,
                    Visibility = 3, Stop = 1, Captured = finished + 1
                };
                var clock = new CooperativeClockRead(0, finished + 2);
                long started = Stopwatch.GetTimestamp();
                var receipt = tracker.Observe(header, children, clock);
                long observed = Stopwatch.GetTimestamp();
                var accepted = adapter.Accept(receipt, fixture.State);
                long ended = Stopwatch.GetTimestamp();
                long remaining = CooperativeManagedChecks.Remaining(fixture, accepted.State);
                results.Add(new(iteration, started, observed, ended, accepted.Adapter.AcceptedCount,
                    accepted.Adapter.ConfirmedNanoseconds, accepted.Adapter.IsTerminal, remaining));
                CooperativeManagedChecks.Require(receipt.Received.Count == 10 && accepted.Adapter.AcceptedCount == 10
                    && accepted.Adapter.ConfirmedNanoseconds == 500000 && accepted.Adapter.IsTerminal && remaining == 0,
                    "The receipt cost workload did not settle its ten-child lease exactly.");
            }
        }
        catch (Exception error) { failure = error.ToString(); }
        File.WriteAllText(outputPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1, runtime = RuntimeInformation.FrameworkDescription,
            operatingSystem = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            timestampFrequency = Stopwatch.Frequency, syntheticReceipt = true, nativeTransportMeasured = false,
            runtimeEnvironment = new Dictionary<string, string?>
            {
                ["DOTNET_TieredCompilation"] = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
                ["DOTNET_ReadyToRun"] = Environment.GetEnvironmentVariable("DOTNET_ReadyToRun"),
                ["COMPlus_TieredCompilation"] = Environment.GetEnvironmentVariable("COMPlus_TieredCompilation"),
                ["COMPlus_ReadyToRun"] = Environment.GetEnvironmentVariable("COMPlus_ReadyToRun")
            },
            requestedIterations = 32, passed = failure is null && results.Count == 32, failure, observations = results
        }));
        return failure is null && results.Count == 32 ? 0 : 1;
    }
}
