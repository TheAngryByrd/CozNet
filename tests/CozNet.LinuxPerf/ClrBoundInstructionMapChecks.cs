using static CooperativeManagedChecks;

static class ClrBoundInstructionMapChecks
{
    private static ClrProbeBinding Binding() => ClrProbeBinding.Parse(1, 10, 20, 0x06000001, 2);
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new InvalidOperationException("An invalid bound native mapping was accepted.");
    }
    public static void Run()
    {
        RangeBoundaries();
        CaptureRejectsInvalidation();
        ResolutionRejectsInvalidation();
    }
    private static void RangeBoundaries()
    {
        foreach (var pair in new[] { (0UL, 1UL), (1UL, 0UL), (ulong.MaxValue, 1UL), (ulong.MaxValue - 1, 2UL) })
            Reject(() => ClrNativeRange.Parse(pair.Item1, pair.Item2));
        var range = ClrNativeRange.Parse(100, 10);
        Require(!range.Contains(99) && range.Contains(100) && range.Contains(105) && range.Contains(109)
            && !range.Contains(110) && !range.Contains(ulong.MaxValue), "The method range does not use exclusive-end boundaries.");
        var upper = ClrNativeRange.Parse(ulong.MaxValue - 1, 1);
        Require(upper.Contains(ulong.MaxValue - 1) && !upper.Contains(ulong.MaxValue), "The upper valid range wrapped its end.");
        var life = new ClrProbeLifetime(Binding(), _ => true);
        var map = ClrBoundInstructionMap.Capture(life, "selected", _ => range);
        Require(map.Resolve(100) == "selected" && map.Resolve(109) == "selected" && map.Resolve(99) is null
            && map.Resolve(110) is null, "The bound map did not preserve the selected symbol and range boundaries.");
    }
    private static void CaptureRejectsInvalidation()
    {
        var binding = Binding();
        var before = new ClrProbeLifetime(binding, _ => false);
        bool queried = false;
        Reject(() => ClrBoundInstructionMap.Capture(before, "selected", _ => { queried = true; return ClrNativeRange.Parse(100, 10); }));
        Require(!queried, "Range capture queried native code after lifetime rejection.");
        bool current = true;
        var during = new ClrProbeLifetime(binding, _ => current);
        Reject(() => ClrBoundInstructionMap.Capture(during, "selected", actual =>
        {
            Require(ReferenceEquals(actual, binding), "Range capture replaced the admitted binding.");
            current = false;
            return ClrNativeRange.Parse(100, 10);
        }));
        Require(during.FirstFailure is not null, "A failed post-capture check did not fail the experiment.");
        var failedQuery = new ClrProbeLifetime(binding, _ => true);
        Reject(() => ClrBoundInstructionMap.Capture(failedQuery, "selected", _ => throw new InvalidDataException("Controlled query failure.")));
        Require(failedQuery.FirstFailure is not null, "A failed native range query did not fail the experiment.");
    }
    private static void ResolutionRejectsInvalidation()
    {
        foreach (ulong address in new ulong[] { 99, 100, 109, 110 })
        {
            int calls = 0;
            var lifetime = new ClrProbeLifetime(Binding(), _ => ++calls < 4);
            var map = ClrBoundInstructionMap.Capture(lifetime, "selected", _ => ClrNativeRange.Parse(100, 10));
            Reject(() => map.Resolve(address));
            Require(calls == 4 && lifetime.FirstFailure is not null, "Resolution returned a result after its final lifetime check failed.");
            Reject(() => map.Resolve(address));
            Require(calls == 4, "A failed map queried the lifetime again instead of retaining the first failure.");
        }
    }
}
