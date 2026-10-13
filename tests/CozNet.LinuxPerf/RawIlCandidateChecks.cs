static class RawIlCandidateChecks
{
    private const string Selected = "selected";
    private static readonly RawNativeRange[] Ranges = [new(0, 0, 1), new(1, 1, 3), new(-3, 3, 4)];

    public static void Run()
    {
        ParseModes();
        HalfOpenRangesPreserveSpecialAndOtherMethods();
        RejectMalformedMaps();
        RejectChangedMethod();
    }

    private static void ParseModes()
    {
        var manual = ProbePlan.Parse(null, null);
        var injected = ProbePlan.Parse("injected", "1024");
        if (AttributionPlan.Parse(null, manual, null, null) != AttributionMode.Method ||
            AttributionPlan.Parse("method", injected, null, null) != AttributionMode.Method ||
            AttributionPlan.Parse("raw-whole-method", injected, "raw-diagnostic", "empty-stack-diagnostic") != AttributionMode.RawWholeMethod ||
            AttributionPlan.Parse("raw-exclude-candidates", injected, "raw-diagnostic", "empty-stack-diagnostic") != AttributionMode.RawExcludeCandidates)
            throw new Exception("ParseModes: a supported attribution mode changed.");
        foreach (var (value, plan, map, placement) in new (string?, ProbePlan, string?, string?)[] {
            ("", injected, "raw-diagnostic", "empty-stack-diagnostic"),
            ("other", injected, "raw-diagnostic", "empty-stack-diagnostic"),
            ("raw-whole-method", manual, "raw-diagnostic", "empty-stack-diagnostic"),
            ("raw-exclude-candidates", ProbePlan.Parse("injected", "256"), "raw-diagnostic", "empty-stack-diagnostic"),
            ("raw-exclude-candidates", injected, "registered", "empty-stack-diagnostic"),
            ("raw-exclude-candidates", injected, "raw-diagnostic", "branch") })
            Reject<ArgumentException>(() => AttributionPlan.Parse(value, plan, map, placement), "ParseModes: an incompatible mode was accepted.");
    }

    private static RawIlCandidateMap Create(bool exclude = true, RawNativeRange[]? ranges = null,
        uint[]? originals = null, byte[]? il = null, IInstructionMap? fallback = null, ulong start = 100) =>
        RawIlCandidateMap.Create(fallback ?? new FixedMap(), Selected, start, new byte[4], il ?? [0x16, 0x2a],
            originals ?? [0], ranges ?? Ranges, exclude);

    private static void HalfOpenRangesPreserveSpecialAndOtherMethods()
    {
        var excluded = Create();
        var whole = Create(false);
        foreach (var (ip, expected) in new (ulong, string)[] {
            (99, "other"), (100, Selected), (101, RawIlCandidateMap.CandidateSymbol),
            (102, RawIlCandidateMap.CandidateSymbol), (103, Selected), (104, "other") })
        {
            if (excluded.Resolve(ip) != expected) throw new Exception($"HalfOpenRangesPreserveSpecialAndOtherMethods: wrong exclusion at {ip}.");
            if (whole.Resolve(ip) != (ip is >= 100 and < 104 ? Selected : "other"))
                throw new Exception($"HalfOpenRangesPreserveSpecialAndOtherMethods: whole-method mode changed {ip}.");
        }
        if (excluded.Evidence.NativeOwnershipEstablished || !excluded.Evidence.ExcludeCandidates || whole.Evidence.ExcludeCandidates)
            throw new Exception("HalfOpenRangesPreserveSpecialAndOtherMethods: evidence overstates ownership or changes the requested mode.");
    }

    private static void RejectMalformedMaps()
    {
        foreach (var ranges in new RawNativeRange[][] {
            [], [new(0, 0, 1), new(1, 2, 4)], [new(0, 0, 2), new(1, 1, 4)],
            [new(0, 0, 1), new(1, 1, 5)], [new(0, 0, 1), new(1, 3, 2)],
            [new(0, 0, 4)], [new(1, 0, 4)], [new(0, 0, 1), new(2, 1, 4)] })
            Reject<InvalidDataException>(() => Create(ranges: ranges), "RejectMalformedMaps: an invalid native range was accepted.");
        foreach (var originals in new uint[][] { [], [0, 0], [2] })
            Reject<InvalidDataException>(() => Create(originals: originals), "RejectMalformedMaps: invalid original offsets were accepted.");
        Reject<InvalidOperationException>(() => Create(il: [0x20]), "RejectMalformedMaps: truncated IL was accepted.");
        Reject<InvalidDataException>(() => Create(start: 0), "RejectMalformedMaps: a null code pointer was accepted.");
        Reject<InvalidDataException>(() => Create(start: ulong.MaxValue - 2), "RejectMalformedMaps: address overflow was accepted.");
    }

    private static void RejectChangedMethod()
    {
        var fallback = new FixedMap();
        var snapshot = Create(fallback: fallback);
        fallback.Shift = 1;
        Reject<InvalidDataException>(() => snapshot.Resolve(100), "RejectChangedMethod: stale method ownership was accepted.");
        Reject<InvalidDataException>(() => snapshot.Resolve(104), "RejectChangedMethod: a new selected range was accepted.");
        Reject<InvalidDataException>(() => Create(fallback: fallback), "RejectChangedMethod: a mismatched method was captured.");
    }

    private sealed class FixedMap : IInstructionMap
    {
        public ulong Shift { get; set; }
        public string? Resolve(ulong ip) => ip >= 100 + Shift && ip < 104 + Shift ? Selected : "other";
    }

    private static void Reject<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception(message);
    }
}
