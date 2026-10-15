static class SampleClockEvidenceChecks
{
    private sealed class Clock(params long[] ticks) : TimeProvider
    {
        private int index;
        public override long TimestampFrequency => 1000000000;
        public override long GetTimestamp() => ticks[index++];
    }

    private static MeasurementWindowCapture Window()
    {
        var window = new MeasurementWindowCapture(new Clock(100, 110, 200, 210));
        MeasurementWindowCapture.Readings Read() => new(0, 0, 0, 1,
            new(0, 0, 0, null), new(0, 0, 0, null));
        window.Begin(Read, () => { });
        window.Finish(Read);
        return window;
    }

    private static readonly SampleClockDescription Source = new(SampleClockDomain.Monotonic, 1000000000);
    private static readonly SampleClockReading Start = new(80, 90, 95, 1000000000, "context-1", 0, null);
    private static readonly SampleClockReading End = new(220, 225, 230, 1000000000, "context-1", 0, null);

    public static void Run()
    {
        MatchingReadingsPreserveEvidence();
        MissingAndIncompatibleEvidenceCannotParse();
        MappingAndContextChangesCannotParse();
        ReadingsMustEncloseMeasurement();
        NamespaceOffsetParsingIsStrict();
        CaptureRetainsNamespaceMismatch();
        CaptureRetainsOffsetAndClockFailures();
        CaptureRetainsNamespaceChangeAndRawReading();
        NativeFailuresRetainCapturedBrackets();
    }

    private static void MatchingReadingsPreserveEvidence()
    {
        var window = Window();
        var parsed = ConsistentSampleClockReadings.Parse(Source, Start, End, window.Start, window.End);
        Require(parsed.Error is null && parsed.Value?.Start == Start && parsed.Value.End == End,
            "Matching clock evidence must preserve both original readings.");
    }

    private static void MissingAndIncompatibleEvidenceCannotParse()
    {
        Reject(null, Start, End, "domain or units");
        Reject(Source with { Domain = SampleClockDomain.Unknown }, Start, End, "domain or units");
        Reject(Source with { Domain = (SampleClockDomain)42 }, Start, End, "domain or units");
        Reject(Source with { UnitsPerSecond = 1000000 }, Start, End, "domain or units");
        Reject(Source, null, End, "missing");
        Reject(Source, Start, null, "missing");
        Reject(Source, Start with { ContextIdentity = null }, End, "zero-offset evidence");
        Reject(Source, Start with { MonotonicOffsetNanoseconds = null }, End, "zero-offset evidence");
        Reject(Source, Start with { MonotonicOffsetNanoseconds = 1 }, End, "zero-offset evidence");
        Reject(Source, Start with { MonotonicOffsetNanoseconds = -1 }, End, "zero-offset evidence");
        Reject(Source, Start with { Error = "Namespace capture failed." }, End, "Namespace capture failed.");
        var missing = ConsistentSampleClockReadings.Parse(Source, Start, End, null, Window().End);
        Require(missing.Value is null && missing.Error?.Contains("missing", StringComparison.Ordinal) == true,
            "Missing measurement endpoints must reject clock evidence.");
    }

    private static void MappingAndContextChangesCannotParse()
    {
        Reject(Source, Start, End with { ContextIdentity = "context-2" }, "context changed");
        Reject(Source, Start with { TimestampFrequency = 0 }, End, "units or ordering");
        Reject(Source, Start with { Before = -1 }, End, "units or ordering");
        Reject(Source, Start with { NativeNanoseconds = -1 }, End, "units or ordering");
        Reject(Source, Start with { After = 79 }, End, "units or ordering");
        Reject(Source, Start with { NativeNanoseconds = 79 }, End, "outside its managed bracket");
        Reject(Source, Start with { NativeNanoseconds = 96 }, End, "outside its managed bracket");
        Reject(Source, Start, End with { Before = 440, After = 460, TimestampFrequency = 2000000000 }, "frequencies differ");
        Reject(Source, Start, new(70, 75, 79, 1000000000, "context-1", 0, null), "regressed");
        Reject(Source, Start with { Before = long.MaxValue - 1, NativeNanoseconds = long.MaxValue, After = long.MaxValue },
            End, "regressed");
    }

    private static void ReadingsMustEncloseMeasurement()
    {
        Reject(Source, Start with { After = 101 }, End, "do not enclose");
        Reject(Source, Start, End with { Before = 209 }, "do not enclose");
        var window = Window();
        var exact = ConsistentSampleClockReadings.Parse(Source, Start with { After = 100 },
            End with { Before = 210 }, window.Start, window.End);
        Require(exact.Value is not null && exact.Error is null, "Equal bracket endpoints must remain consistent.");
    }

    private static void NamespaceOffsetParsingIsStrict()
    {
        Require(LinuxSampleClockEvidenceSource.ParseMonotonicOffset(["monotonic 0 0", "boottime 1 0"]) == 0,
            "The monotonic parser must ignore boottime offsets.");
        Require(LinuxSampleClockEvidenceSource.ParseMonotonicOffset(["monotonic -1 999999999"]) == -1,
            "A normalized negative namespace offset must retain its sign.");
        Require(LinuxSampleClockEvidenceSource.ParseMonotonicOffset(["monotonic 1 1"]) == 1000000001,
            "The parser must preserve nonzero offsets for rejection.");
        string[][] invalid = [[], ["monotonic 0"], ["monotonic 0 0", "monotonic 0 0"],
            ["monotonic 0 -1"], ["monotonic 0 1000000000"], ["monotonic x 0"], ["monotonic 9223372036854775807 0"]];
        foreach (var lines in invalid)
        {
            bool rejected = false;
            try { LinuxSampleClockEvidenceSource.ParseMonotonicOffset(lines); }
            catch (Exception error) when (error is InvalidDataException or FormatException or OverflowException) { rejected = true; }
            Require(rejected, $"Invalid namespace offset was accepted: {string.Join(';', lines)}");
        }
    }

    private static void CaptureRetainsNamespaceMismatch()
    {
        foreach (var namespaces in new[]
        {
            new LinuxSampleClockEvidenceSource.Namespaces("current", "child", "current"),
            new LinuxSampleClockEvidenceSource.Namespaces("current", "current", "leader-child")
        })
        {
            int offsetCalls = 0, nativeCalls = 0;
            var reading = LinuxSampleClockEvidenceSource.Capture(() => namespaces,
                () => { offsetCalls++; return 0; },
                () => { nativeCalls++; return new(10, 15, 20, 1000000000); });
            Require(reading.Error == "Current and offset-source time namespaces differ." &&
                reading.ContextIdentity == "current" && reading.MonotonicOffsetNanoseconds is null &&
                offsetCalls == 0 && nativeCalls == 0,
                "A namespace mismatch must retain the current identity without reading unrelated offsets or clocks.");
        }
    }

    private static void CaptureRetainsOffsetAndClockFailures()
    {
        var context = new LinuxSampleClockEvidenceSource.Namespaces("context", "context", "context");
        int nativeCalls = 0;
        var malformed = LinuxSampleClockEvidenceSource.Capture(() => context,
            () => LinuxSampleClockEvidenceSource.ParseMonotonicOffset(["monotonic 0"]),
            () => { nativeCalls++; return new(10, 15, 20, 1000000000); });
        Require(malformed.Error == "The monotonic namespace offset is missing or ambiguous." &&
            malformed.ContextIdentity == "context" && malformed.MonotonicOffsetNanoseconds is null && nativeCalls == 0,
            "Malformed offsets must become a reading rejection before native capture.");
        Exception[] failures = [new InvalidDataException("invalid native"), new IOException("missing file"),
            new UnauthorizedAccessException("access denied"), new InvalidOperationException("native errno"),
            new PlatformNotSupportedException("unsupported"), new OverflowException("overflow"), new FormatException("format")];
        foreach (var error in failures)
        {
            var reading = LinuxSampleClockEvidenceSource.Capture(() => context, () => 0, () => throw error);
            Require(reading.Error == error.Message && reading.ContextIdentity == "context" &&
                reading.MonotonicOffsetNanoseconds == 0,
                $"Expected capture failure {error.GetType().Name} must retain known context and offset.");
        }
    }

    private static void CaptureRetainsNamespaceChangeAndRawReading()
    {
        var context = new LinuxSampleClockEvidenceSource.Namespaces("context", "context", "context");
        var clock = new LinuxSampleClockEvidenceSource.NativeReading(10, 15, 20, 1000000000);
        int reads = 0;
        var changed = LinuxSampleClockEvidenceSource.Capture(
            () => ++reads == 1 ? context : context with { OffsetSource = "changed" }, () => 0, () => clock);
        Require(reads == 2 && changed == new SampleClockReading(10, 15, 20, 1000000000, "context", 0,
            "The time namespace changed during clock capture."),
            "A namespace change after capture must retain the original context and raw clock bracket.");
        var stable = LinuxSampleClockEvidenceSource.Capture(() => context, () => 0, () => clock);
        Require(stable == changed with { Error = null }, "Stable capture must preserve the same bracket without a rejection.");
    }

    private static void NativeFailuresRetainCapturedBrackets()
    {
        var context = new LinuxSampleClockEvidenceSource.Namespaces("context", "context", "context");
        SampleClockReading Capture(Func<(long, long)> read) => LinuxSampleClockEvidenceSource.Capture(
            () => context, () => 0, () => LinuxSampleClockEvidenceSource.CaptureNative(new Clock(10, 20), read));
        var syscall = Capture(() => throw new InvalidOperationException("clock_gettime failed: 22."));
        Require(syscall == new SampleClockReading(10, 0, 0, 1000000000, "context", 0, "clock_gettime failed: 22."),
            "A failed syscall must retain the timestamp captured before the call.");
        foreach (var invalid in new[] { (-1L, 0L), (0L, -1L), (0L, 1000000000L) })
        {
            var malformed = Capture(() => invalid);
            Require(malformed == new SampleClockReading(10, 0, 20, 1000000000, "context", 0,
                "The native monotonic reading is invalid."),
                "Invalid native times must retain both captured timestamps.");
        }
        var overflow = Capture(() => (long.MaxValue, 0));
        Require(overflow.Before == 10 && overflow.After == 20 && overflow.NativeNanoseconds == 0 &&
            overflow.ContextIdentity == "context" && overflow.MonotonicOffsetNanoseconds == 0 && overflow.Error is not null,
            "Native conversion overflow must retain both timestamps and known context.");
        Require(Capture(() => (0, 15)) == new SampleClockReading(10, 15, 20, 1000000000, "context", 0, null),
            "Successful native capture must preserve its timestamp conversion and bracket.");
    }

    private static void Reject(SampleClockDescription? source, SampleClockReading? start, SampleClockReading? end, string reason)
    {
        var window = Window();
        var parsed = ConsistentSampleClockReadings.Parse(source, start, end, window.Start, window.End);
        Require(parsed.Value is null && parsed.Error?.Contains(reason, StringComparison.Ordinal) == true,
            $"Expected rejection containing '{reason}', received '{parsed.Error}'.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
