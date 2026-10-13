static class CollectorChecks
{
    public static void Run()
    {
        CompletionPublishesFinalDrain();
        FailureReachesCaller();
    }

    private static void CompletionPublishesFinalDrain()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int calls = 0;
        using var collector = new LiveSampleCollector(() =>
        {
            int count = Interlocked.Increment(ref calls);
            if (count == 1)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5)))
                    throw new Exception("CompletionPublishesFinalDrain: the first drain was not released.");
            }
            return new SampleBatch(count, 0, 0, 0, 0, 0, 0, 0, Array.Empty<string>());
        });
        try
        {
            if (!entered.Wait(TimeSpan.FromSeconds(5)))
                throw new Exception("CompletionPublishesFinalDrain: the collector did not start.");
            collector.RequestStop();
        }
        finally { release.Set(); }
        var final = collector.Complete();
        if (final.Samples != 2 || Volatile.Read(ref calls) != 2)
            throw new Exception("CompletionPublishesFinalDrain: completion did not publish the final drain.");
        if (!ReferenceEquals(final, collector.Complete()))
            throw new Exception("CompletionPublishesFinalDrain: repeated completion changed the result.");
    }

    private static void FailureReachesCaller()
    {
        var failure = new InvalidDataException("Injected malformed sample.");
        using var collector = new LiveSampleCollector(() => throw failure);
        try
        {
            collector.Complete();
        }
        catch (InvalidDataException observed) when (ReferenceEquals(observed, failure))
        {
            return;
        }
        throw new Exception("FailureReachesCaller: completion discarded the reader failure.");
    }
}
