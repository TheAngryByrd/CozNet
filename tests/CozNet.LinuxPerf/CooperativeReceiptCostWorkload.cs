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
                var result = CooperativeReceiptPreparation.Exercise();
                results.Add(new(iteration, result.Started, result.Observed, result.Accepted, result.Children,
                    result.ConfirmedNanoseconds, result.Terminal, result.RemainingNanoseconds));
                result.RequireSettled();
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
