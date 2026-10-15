using CozNet;
using static CooperativeManagedChecks;

static class ClrProbeLifetimeChecks
{
    private static ClrProbeBinding Binding() => ClrProbeBinding.Parse(1, 10, 20, 0x06000001, 2);
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new InvalidOperationException("The invalid CLR lifetime operation was accepted.");
    }
    public static void Run()
    {
        InvalidBindingAndOrigin();
        InvalidationBeforeAndDuringOperation();
        ReceiptResultCannotEscapeInvalidation();
        CallbackArgumentsAndIsolation();
    }
    private static void InvalidBindingAndOrigin()
    {
        Reject(() => ClrProbeBinding.Parse(0, 10, 20, 0x06000001, 2));
        Reject(() => ClrProbeBinding.Parse(1, 0, 20, 0x06000001, 2));
        Reject(() => ClrProbeBinding.Parse(1, 10, 0, 0x06000001, 2));
        Reject(() => ClrProbeBinding.Parse(1, 10, 20, 0x06000000, 2));
        Reject(() => ClrProbeBinding.Parse(1, 10, 20, 0x02000001, 2));
        foreach (uint sites in new uint[] { 0, 1, 3 }) Reject(() => ClrProbeBinding.Parse(1, 10, 20, 0x06000001, sites));
        foreach (var origin in new[] { (0UL, 1U), (2UL, 1U), (1UL, 0U), (1UL, 3U) })
        {
            var gate = new ClrProbeLifetime(Binding(), _ => true);
            Reject(() => gate.Execute(origin, value => { gate.RequireOrigin(value.Item1, value.Item2); return 1; }));
            var first = gate.FirstFailure;
            Require(first is not null, "A foreign origin did not fail the experiment.");
            Reject(gate.RequireCurrent);
            Require(ReferenceEquals(first, gate.FirstFailure), "A later check replaced the first origin failure.");
        }
        var admitted = new ClrProbeLifetime(Binding(), _ => true);
        admitted.Execute(0, _ => { admitted.RequireOrigin(1, 1); admitted.RequireOrigin(1, 2); return true; });
    }
    private static void InvalidationBeforeAndDuringOperation()
    {
        bool current = false, ran = false;
        var before = new ClrProbeLifetime(Binding(), _ => current);
        Reject(() => before.Execute(0, _ => { ran = true; return 1; }));
        Require(!ran && before.FirstFailure is not null, "Invalidation did not stop admission before the operation.");
        current = true;
        Reject(before.RequireCurrent);
        var during = new ClrProbeLifetime(Binding(), _ => current);
        Reject(() => during.Execute(0, _ => { ran = true; current = false; return 1; }));
        Require(ran && during.FirstFailure is not null, "Invalidation during the operation released its result.");
        var original = during.FirstFailure;
        current = true;
        Reject(during.RequireCurrent);
        Require(ReferenceEquals(original, during.FirstFailure), "Restored availability repaired a failed experiment.");
        var failure = new IOException("Controlled operation failure.");
        var throws = new ClrProbeLifetime(Binding(), _ => true);
        try { throws.Execute<int, int>(0, _ => throw failure); } catch (IOException) { }
        Require(ReferenceEquals(failure, throws.FirstFailure), "An operation failure was not retained.");
        Reject(throws.RequireCurrent);
    }
    private static void ReceiptResultCannotEscapeInvalidation()
    {
        var fixture = Reserve(100000);
        bool invalidated = false;
        var guarded = new ClrProbeLifetime(Binding(), _ => !invalidated);
        var child = new CooperativeNativeChild { Ordinal = 1, Requested = 50000, Armed = 10, Started = 20,
            Finished = 50020, Waited = 50000, Worker = 1, CodeVersion = 2, Site = 3, Valid = 1 };
        var header = new CooperativeNativeSnapshot { Generation = 1, Command = 7, Budget = 50000,
            Children = 1, Measured = 50000, Visibility = 3, Stop = 1, Captured = 50030 };
        var observation = new CooperativeReceipts(CooperativeLeaseIdentity.Parse(1, 7, 50000))
            .Observe(header, new[] { child }, new(0, 50040));
        var adapter = Adapter(fixture);
        bool settledInside = false;
        Reject(() => guarded.Execute(0, _ =>
        {
            var result = adapter.Accept(observation, fixture.State);
            settledInside = result.Adapter.IsTerminal;
            invalidated = true;
            return result;
        }));
        Require(settledInside && !adapter.IsTerminal && SignalAccountingModule.pendingCount(fixture.State) == 1,
            "Rejected post-settlement evidence changed the caller's immutable reservation.");
    }
    private static void CallbackArgumentsAndIsolation()
    {
        ulong generation = 0;
        uint site = 0;
        using (InjectedProbes.BindBound((g, s) => (generation, site) = (g, s)))
        {
            InjectedProbes.HitBound(7, 2);
            Require(generation == 7 && site == 2, "The bound callback did not receive the supplied literals.");
            bool rejected = false;
            try { using var duplicate = InjectedProbes.Bind(() => { }); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "A legacy binding replaced an active bound callback.");
            rejected = false;
            try { InjectedProbes.Hit(); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "A parameterless helper invoked the bound callback.");
        }
        int legacyCalls = 0;
        using (InjectedProbes.Bind(() => legacyCalls++))
        {
            bool rejected = false;
            try { using var duplicate = InjectedProbes.BindBound((_, _) => { }); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "A bound binding replaced an active legacy callback.");
            rejected = false;
            try { InjectedProbes.HitBound(7, 2); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected && legacyCalls == 0, "The bound helper consumed or skipped an active legacy callback without rejection.");
            InjectedProbes.Hit();
            Require(legacyCalls == 1, "Callback rejection changed the installed legacy callback.");
        }
        using (InjectedProbes.BindBound((g, s) => (generation, site) = (g, s))) InjectedProbes.HitBound(9, 2);
        Require(generation == 9 && site == 2, "Legacy disposal did not release the callback slot for a bound binding.");
    }
}
