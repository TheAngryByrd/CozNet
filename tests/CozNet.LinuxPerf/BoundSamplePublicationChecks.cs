static class BoundSamplePublicationChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Run()
    {
        ServiceFollowsDeclaredSampleBoundary();
        SampleFailureStopsBeforeServiceAndLaterSamples();
        ServiceFailurePreservesOriginalExceptionAndPrefix();
        FrozenPrefixExcludesNewSamples();
        EmptyAndInvalidInputsDoNotService();
    }

    private static void ServiceFollowsDeclaredSampleBoundary()
    {
        foreach (string? mode in new string?[] { null, "0", "1" })
        {
            var publication = BoundSamplePublication.Parse(mode);
            var events = new List<int>();
            publication.Publish(new[] { 9, 1, 2, 3 }, 1, events.Add, () => events.Add(0));
            int[] expected = mode == "1" ? [1, 2, 3, 0] : [1, 0, 2, 0, 3, 0];
            Require(events.SequenceEqual(expected) && publication.BatchServiceEnabled == (mode == "1")
                && publication.ServiceCalls == (mode == "1" ? 1 : 3), "Publication changed sample order, offset, or service cadence.");
            events.Clear();
            publication.Publish(new[] { 4 }, 0, events.Add, () => events.Add(0));
            Require(events.SequenceEqual(new[] { 4, 0 }) && publication.ServiceCalls == (mode == "1" ? 2 : 4),
                "A later singleton publication reset the service count or changed its order.");
        }
    }

    private static void SampleFailureStopsBeforeServiceAndLaterSamples()
    {
        foreach (string mode in new[] { "0", "1" })
        {
            var publication = BoundSamplePublication.Parse(mode);
            var events = new List<int>();
            var original = new InvalidDataException("intermediate debt exceeded");
            Exception? observed = null;
            try
            {
                publication.Publish(new[] { 1, 2, 3 }, 0, sample =>
                {
                    events.Add(sample);
                    if (sample == 2) throw original;
                }, () => events.Add(0));
            }
            catch (Exception error) { observed = error; }
            int[] expected = mode == "1" ? [1, 2] : [1, 0, 2];
            Require(ReferenceEquals(original, observed) && events.SequenceEqual(expected)
                && publication.ServiceCalls == (mode == "1" ? 0 : 1), "A failed sample changed its error or allowed service or later samples.");
        }
    }

    private static void ServiceFailurePreservesOriginalExceptionAndPrefix()
    {
        foreach (string mode in new[] { "0", "1" })
        {
            var publication = BoundSamplePublication.Parse(mode);
            var events = new List<int>();
            var original = new InvalidDataException("receipt rejected");
            Exception? observed = null;
            try { publication.Publish(new[] { 1, 2, 3 }, 0, events.Add, () => throw original); }
            catch (Exception error) { observed = error; }
            int[] expected = mode == "1" ? [1, 2, 3] : [1];
            Require(ReferenceEquals(original, observed) && events.SequenceEqual(expected) && publication.ServiceCalls == 1,
                "A failed service changed its error, sample prefix, or attempt count.");
        }
    }

    private static void FrozenPrefixExcludesNewSamples()
    {
        foreach (string mode in new[] { "0", "1" })
        {
            var publication = BoundSamplePublication.Parse(mode);
            var source = new List<int> { 1, 2 };
            var consumed = new List<int>();
            publication.Publish(source, 0, sample => { consumed.Add(sample); source.Add(3); }, () => { });
            Require(consumed.SequenceEqual(new[] { 1, 2 }), "Publication chased samples beyond its captured prefix.");
        }
    }

    private static void EmptyAndInvalidInputsDoNotService()
    {
        foreach (string mode in new[] { "0", "1" })
        {
            var publication = BoundSamplePublication.Parse(mode);
            int effects = 0;
            publication.Publish(Array.Empty<int>(), 0, _ => effects++, () => effects++);
            publication.Publish(new[] { 1 }, 1, _ => effects++, () => effects++);
            foreach (int offset in new[] { -1, 2 })
            {
                bool rejected = false;
                try { publication.Publish(new[] { 1 }, offset, _ => effects++, () => effects++); }
                catch (ArgumentOutOfRangeException) { rejected = true; }
                Require(rejected, "An invalid sample offset was accepted.");
            }
            Require(effects == 0 && publication.ServiceCalls == 0, "An empty or invalid prefix performed work.");
        }
        foreach (string value in new[] { "", "true", "2", " 1" })
        {
            bool rejected = false;
            try { BoundSamplePublication.Parse(value); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected, "An invalid publication mode was accepted.");
        }
    }
}
