using System.Diagnostics;

static class ServiceSnapshotCosts
{
    internal sealed record Batch(int Index, int Iterations, long ElapsedTicks, long AllocatedBytes,
        int Gen0, int Gen1, int Gen2);

    internal static void Measure(Func<ServiceNativeObservation> read, ServiceNativeObservation expected,
        List<Batch> batches)
    {
        ulong captured = expected.Header.Captured;
        for (int i = 0; i < 256; i++) Validate(read(), expected, ref captured);
        var observations = new ServiceNativeObservation[2000];
        for (int batch = 0; batch < 5; batch++)
        {
            int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            for (int index = 0; index < observations.Length; index++) observations[index] = read();
            long elapsed = Stopwatch.GetTimestamp() - started;
            long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            batches.Add(new(batch, observations.Length, elapsed, bytes, GC.CollectionCount(0) - gen0,
                GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2));
            foreach (var observation in observations) Validate(observation, expected, ref captured);
        }
    }

    private static void Validate(ServiceNativeObservation actual, ServiceNativeObservation expected, ref ulong captured)
    {
        var header = actual.Header;
        if (header.Captured < captured)
            throw new InvalidDataException("The repeated snapshot clock regressed.");
        captured = header.Captured;
        header.Captured = expected.Header.Captured;
        if (!header.Equals(expected.Header) || actual.Children.Length != 1 || actual.Rejected.Length != 0
            || actual.Children[0] != expected.Children[0])
            throw new InvalidDataException("The completed journal or cached first receipt changed during snapshot measurement.");
    }
}
