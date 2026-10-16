using static CooperativeManagedChecks;

static class BoundSampleClockCaptureChecks
{
    private static readonly SampleClockReading Start = new(80, 90, 95, 1000000000, "clock-context", 0, null);
    private static readonly SampleClockReading End = new(220, 225, 230, 1000000000, "clock-context", 0, null);
    private sealed class Clock(params long[] ticks) : TimeProvider
    {
        private int index;
        public override long TimestampFrequency => 1000000000;
        public override long GetTimestamp() => ticks[index++];
    }

    private sealed class Source(string? failAt = null, SampleClockReading? last = null) : ISampleClockEvidenceSource
    {
        public int Reads { get; private set; }
        public SampleClockDescription Description => failAt == "description" ? throw new IOException("description failed")
            : new(SampleClockDomain.Monotonic, 1000000000);
        public object Metadata => failAt == "metadata" ? throw new IOException("metadata failed") : "fixture";
        public SampleClockReading Read()
        {
            Reads++;
            if (failAt == "read" + Reads) throw new IOException(failAt + " failed");
            if (Reads > 2) throw new InvalidOperationException("Clock capture read more than twice.");
            return Reads == 1 ? Start : last ?? End;
        }
    }

    private static MeasurementWindowCapture Window()
    {
        var window = new MeasurementWindowCapture(new Clock(100, 110, 200, 210));
        MeasurementWindowCapture.Readings Read() => new(0, 0, 0, 1, new(0, 0, 0, null), new(0, 0, 0, null));
        window.Begin(Read, () => { });
        window.Finish(Read);
        return window;
    }

    private static BoundSampleClockEvidence Evidence(BoundSampleClockCapture capture)
    {
        var window = Window();
        return capture.Describe(window.Start, window.End);
    }

    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    public static void Run()
    {
        ParseRequiresFiniteMeasurement();
        CaptureRetainsEnclosingReadings();
        MissingProviderIsExplicit();
        BackendFailureIsTerminal();
        FinishIsIdempotent();
        ForeignOwnerCannotChangeCapture();
        InconsistentReadingsRemainRejected();
        NeverStartedHasNoReads();
        WrapperRejectsBeforeCreation();
    }

    private static void ParseRequiresFiniteMeasurement()
    {
        Require(BoundSampleClockCapture.Parse(null, false) is null && BoundSampleClockCapture.Parse("0", false) is null,
            "Disabled sample clock capture must need no measurement or provider.");
        Reject<ArgumentException>(() => BoundSampleClockCapture.Parse("1", false));
        foreach (var value in new[] { "", "true", " 1", "2" })
            Reject<ArgumentException>(() => BoundSampleClockCapture.Parse(value, true));
    }

    private static void CaptureRetainsEnclosingReadings()
    {
        var capture = BoundSampleClockCapture.Parse("1", true)!;
        var source = new Source();
        capture.Begin(() => source);
        var started = Evidence(capture);
        Require(started.Status == "started" && started.Start == Start && started.End is null
            && !started.ObservedMappingConsistent, "A started capture must not claim complete evidence.");
        capture.Finish();
        var result = Evidence(capture);
        Require(result.Status == "complete" && result.Start == Start && result.End == End && source.Reads == 2
            && result.Source == source.Description && Equals(result.Metadata, "fixture") && result.Failure is null
            && result.ObservedMappingConsistent && result.Rejection is null && !result.ErrorBoundEstablished,
            "Matching enclosing readings must preserve evidence without an error-bound claim.");
        Require(started.End is null && started.Status == "started", "A later finish must not mutate an earlier evidence record.");
    }

    private static void MissingProviderIsExplicit()
    {
        var capture = BoundSampleClockCapture.Parse("1", true)!;
        capture.Begin(() => null);
        capture.Finish();
        var result = Evidence(capture);
        Require(result.Status == "unavailable" && result.Source is null && result.Metadata is null
            && result.Start is null && result.End is null && result.Failure is null
            && !result.ObservedMappingConsistent && result.Rejection is not null,
            "An unavailable backend must remain distinct from successful evidence.");
        Reject<InvalidOperationException>(() => capture.Begin(() => new Source()));
    }

    private static void BackendFailureIsTerminal()
    {
        foreach (var point in new[] { "factory", "description", "metadata", "read1", "read2" })
        {
            var source = new Source(point);
            var capture = BoundSampleClockCapture.Parse("1", true)!;
            capture.Begin(() => point == "factory" ? throw new IOException("factory failed") : source);
            capture.Finish();
            var failed = Evidence(capture);
            int reads = source.Reads;
            capture.Finish();
            var retained = Evidence(capture);
            Require(failed.Status == "failed" && failed.Failure?.Contains(point + " failed", StringComparison.Ordinal) == true
                && failed.Rejection == failed.Failure && !failed.ObservedMappingConsistent && failed.End is null
                && (failed.Start is not null) == (point == "read2") && source.Reads == reads
                && retained == failed, "Backend failure must retain its first evidence without a retry: " + point);
            Reject<InvalidOperationException>(() => capture.Begin(() => source));
        }
    }

    private static void FinishIsIdempotent()
    {
        var source = new Source();
        var capture = BoundSampleClockCapture.Parse("1", true)!;
        capture.Begin(() => source);
        Reject<InvalidOperationException>(() => capture.Begin(() => throw new IOException("Repeated begin read its factory.")));
        capture.Finish();
        var expected = Evidence(capture);
        capture.Finish();
        Require(source.Reads == 2 && Evidence(capture) == expected, "Repeated cleanup must not repeat clock reads.");
    }

    private static void ForeignOwnerCannotChangeCapture()
    {
        var source = new Source();
        var capture = BoundSampleClockCapture.Parse("1", true)!;
        Task.Run(() => Reject<InvalidOperationException>(() => capture.Begin(() => source))).GetAwaiter().GetResult();
        Require(source.Reads == 0, "A foreign begin must reject before reading the source.");
        capture.Begin(() => source);
        Task.Run(() => Reject<InvalidOperationException>(capture.Finish)).GetAwaiter().GetResult();
        Task.Run(() => Reject<InvalidOperationException>(() => Evidence(capture))).GetAwaiter().GetResult();
        Require(source.Reads == 1, "A foreign completion must preserve active evidence.");
        capture.Finish();
        Require(Evidence(capture).ObservedMappingConsistent, "The owner must retain a usable capture after foreign rejection.");
    }

    private static void InconsistentReadingsRemainRejected()
    {
        foreach (var last in new[] { End with { ContextIdentity = "changed" }, End with { Error = "native read failed" },
            End with { Error = "" }, End with { Before = 209 }, End with { MonotonicOffsetNanoseconds = 1 } })
        {
            var capture = BoundSampleClockCapture.Parse("1", true)!;
            capture.Begin(() => new Source(last: last));
            capture.Finish();
            var result = Evidence(capture);
            Require(result.Status == "complete" && result.End == last && result.Failure is null
                && !result.ObservedMappingConsistent && result.Rejection is not null && !result.ErrorBoundEstablished,
                "Completed reads must not conceal inconsistent raw evidence.");
        }
    }

    private static void NeverStartedHasNoReads()
    {
        var capture = BoundSampleClockCapture.Parse("1", true)!;
        capture.Finish();
        var result = Evidence(capture);
        Require(result.Status == "not-started" && result.Source is null && result.Start is null && result.End is null
            && !result.ObservedMappingConsistent, "Cleanup before setup must not fabricate clock evidence.");
        Reject<InvalidOperationException>(() => capture.Begin(() => throw new IOException("Closed capture read a source.")));
    }

    private static void WrapperRejectsBeforeCreation()
    {
        var capture = BoundSampleClockCapture.Parse("1", true)!;
        int creates = 0;
        var factory = capture.Wrap((_, _) => { creates++; throw new IOException("Unexpected creation."); });
        Task.Run(() => Reject<InvalidOperationException>(() => factory(null!, null!))).GetAwaiter().GetResult();
        capture.Begin(() => new Source());
        Reject<InvalidOperationException>(() => factory(null!, null!));
        capture.Finish();
        Require(creates == 0, "A foreign or repeated wrapper call must reject before creating a source.");
    }
}
