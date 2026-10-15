using CozNet;

sealed class BoundCooperativeClient
{
    private readonly NativeCooperativeClient native;
    private readonly ClrProbeLifetime lifetime;
    private readonly CooperativeSite?[] sites;
    private readonly bool[] cleanupCancellation;
    private int cleanupStarted;
    private bool closed, completed;
    public Exception? FirstFailure => lifetime.FirstFailure;
    public CooperativeCleanupCapture? LastCleanup => native.LastCleanup;
    public CooperativeObservation? LastAccountingObservation { get; private set; }
    public ClrProbeBinding Binding => lifetime.Binding;

    public BoundCooperativeClient(ClrProbeBinding binding, uint targets = 1, uint capacity = 64)
    {
        lifetime = new(binding);
        lifetime.RequireCurrent();
        if (targets is 0 or > 64) throw new ArgumentOutOfRangeException(nameof(targets));
        sites = new CooperativeSite[targets];
        cleanupCancellation = new bool[targets];
        native = new(binding.Generation, targets, capacity);
    }
    private void RequireActive()
    {
        if (Volatile.Read(ref cleanupStarted) != 0) throw new InvalidOperationException("Cooperative cleanup has started.");
    }
    private CooperativeSite Site(uint slot) => sites[slot] ?? throw new InvalidOperationException("The cooperative target is not registered.");
    public CooperativeSite RegisterCurrentThread(uint slot, ulong worker)
    {
        RequireActive();
        return lifetime.Execute((Self: this, Slot: slot, Worker: worker), static value =>
        {
            if (value.Self.sites[value.Slot] is not null) throw new InvalidOperationException("The cooperative slot already has an owner identity.");
            var site = CooperativeSite.Parse(value.Worker, value.Self.Binding.Generation, 2);
            value.Self.sites[value.Slot] = site;
            value.Self.native.RegisterCurrentThread(value.Slot, site);
            return site;
        });
    }
    public ulong Submit(uint slot, ulong command, ulong budget)
    {
        RequireActive();
        return lifetime.Execute((Native: native, Slot: slot, Command: command, Budget: budget),
            static value => value.Native.Submit(value.Slot, value.Command, value.Budget));
    }
    public CooperativePollOutcome PollFromProbe(uint slot, ulong generation, uint site)
    {
        RequireActive();
        return lifetime.Execute((Self: this, Slot: slot, Generation: generation, Site: site), static value =>
        {
            value.Self.lifetime.RequireOrigin(value.Generation, value.Site);
            var registered = value.Self.Site(value.Slot);
            return value.Site == 1 ? CooperativePollOutcome.Idle : value.Self.native.Poll(value.Slot, registered);
        });
    }
    public (CooperativeAccountingAdapter Adapter, SignalAccounting State) ReadAndAccept(uint slot,
        CooperativeAccountingAdapter adapter, SignalAccounting state)
    {
        RequireActive();
        LastAccountingObservation = null;
        return lifetime.Execute((Self: this, Slot: slot, Adapter: adapter, State: state), static value =>
        {
            var observation = value.Self.native.Read(value.Slot);
            value.Self.LastAccountingObservation = observation;
            return value.Adapter.Accept(observation, value.State);
        });
    }
    public bool HasPending(uint slot) => native.HasPending(slot);
    public CooperativeRawCapture CaptureDiagnostic(uint slot) => native.CaptureDiagnostic(slot);
    public CooperativeNativeSnapshot CleanupHeader(uint slot) => native.Probe(slot);
    public void ReleaseAfterRetiredCopy(uint slot) => native.ReleaseAfterRetiredCopy(slot);
    public void BeginCleanup()
    {
        Interlocked.Exchange(ref cleanupStarted, 1);
        for (uint slot = 0; slot < sites.Length; ++slot)
            if (native.HasPending(slot) && native.Probe(slot).Visibility < 3)
            {
                native.Cancel(slot);
                cleanupCancellation[slot] = true;
            }
    }
    public CooperativePollOutcome PollForCleanup(uint slot)
    {
        if (Volatile.Read(ref cleanupStarted) == 0 || !cleanupCancellation[slot])
            throw new InvalidOperationException("Cleanup polling requires controller cancellation.");
        return native.Poll(slot, Site(slot));
    }
    public void RetireCurrentThread(uint slot)
    {
        if (Volatile.Read(ref cleanupStarted) == 0) throw new InvalidOperationException("Begin cleanup before target retirement.");
        native.RetireCurrentThread(slot);
    }
    public void CloseAfterThreadJoin()
    {
        if (Volatile.Read(ref cleanupStarted) == 0) throw new InvalidOperationException("Begin cleanup before close.");
        native.CloseAfterThreadJoin();
        closed = true;
    }
    public void Complete()
    {
        if (!closed || completed) throw new InvalidOperationException("Completion requires one successful cleanup and close.");
        lifetime.RequireCurrent();
        completed = true;
    }
}
