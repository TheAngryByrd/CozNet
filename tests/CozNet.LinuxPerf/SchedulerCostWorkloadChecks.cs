using System.Text.Json;

static class SchedulerCostWorkloadChecks
{
    public static void FailedStartsRetainEvidence(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (int failedAttempt in new[] { 1, 2 })
        {
            int attempts = 0;
            string path = Path.Combine(directory, $"start-{failedAttempt}.json");
            int result = SchedulerCostWorkload.Run(path, "disabled", worker =>
            {
                if (++attempts == failedAttempt) throw new ThreadStateException("Injected thread start failure.");
                worker.Start();
            });
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (result != 1 || root.GetProperty("passed").GetBoolean() || !root.GetProperty("joined").GetBoolean()
                || !root.GetProperty("stopped").GetBoolean() || root.GetProperty("startedWorkers").GetInt32() != failedAttempt - 1
                || !root.GetProperty("failures").EnumerateArray().Any(value => value.GetString()!.Contains("Injected thread start failure.", StringComparison.Ordinal)))
                throw new InvalidOperationException($"Thread start failure {failedAttempt} did not preserve evidence and join the started workers.");
        }
    }
}
