sealed class FiniteCooperativeMeasurement
{
    internal sealed record Estimate(decimal ElapsedLowerNanoseconds, decimal ElapsedUpperNanoseconds,
        long GlobalDelayNanoseconds, decimal EffectiveLowerNanoseconds, decimal EffectiveUpperNanoseconds,
        long SelectedProgress, long PeerProgress, ulong AcceptedSamples);
    internal sealed record Assessment(string Status, string[] Reasons, Estimate? DescriptiveEstimate);

    public MeasurementWindowCapture Window { get; }
    public bool AcceptanceClosed { get; private set; }
    public string? CaptureFailure { get; private set; }
    public string? Failure { get; private set; }

    internal FiniteCooperativeMeasurement(TimeProvider clock) => Window = new(clock);

    public static FiniteCooperativeMeasurement? Parse(string? value, bool ownerDrain, bool cadence, TimeProvider clock)
    {
        if (value is null or "0") return null;
        if (value != "1") throw new ArgumentException("COZNET_FINITE_MEASUREMENT must be 0 or 1.");
        if (!ownerDrain || !cadence)
            throw new ArgumentException("Finite cooperative measurement requires owner draining and an explicit cadence.");
        return new(clock);
    }

    public void Begin(Func<MeasurementWindowCapture.Readings> read)
    {
        if (AcceptanceClosed) throw new InvalidOperationException("A closed experiment cannot begin.");
        try { Window.Begin(read, () => { }); }
        catch (Exception error) { CaptureFailure ??= error.ToString(); throw; }
    }

    public void Freeze(Func<MeasurementWindowCapture.Readings> read)
    {
        if (AcceptanceClosed) return;
        AcceptanceClosed = true;
        if (Window.Status != MeasurementCaptureStatus.Active) return;
        try { Window.Finish(read); }
        catch (Exception error) { CaptureFailure ??= error.ToString(); throw; }
    }

    public void Fail(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        Failure ??= error.ToString();
    }

    public Assessment Assess(bool completed, bool sourceVerified, bool cleanupVerified, long sourceSamples)
    {
        var reasons = new List<string>();
        bool invalid = !completed || !sourceVerified || !cleanupVerified || Failure is not null
            || CaptureFailure is not null || Window.Status != MeasurementCaptureStatus.Complete;
        if (!completed) reasons.Add("workload-incomplete");
        if (!sourceVerified) reasons.Add("source-unverified");
        if (!cleanupVerified) reasons.Add("cleanup-unverified");
        if (Failure is not null) reasons.Add("workload-failure");
        if (CaptureFailure is not null || Window.Status != MeasurementCaptureStatus.Complete) reasons.Add("capture-incomplete");
        Estimate? estimate = null;
        if (Window.Start is { } start && Window.End is { } end)
        {
            var before = start.Accounting;
            var after = end.Accounting;
            decimal lower = ((decimal)end.BeforeTimestamp - start.AfterTimestamp) * 1000000000m / start.TimestampFrequency;
            decimal upper = ((decimal)end.AfterTimestamp - start.BeforeTimestamp) * 1000000000m / start.TimestampFrequency;
            long global = after.Global - before.Global;
            estimate = new(lower, upper, global, lower - global, upper - global,
                after.Selected.Progress - before.Selected.Progress, after.Peer.Progress - before.Peer.Progress,
                after.AcceptedSampleSequence - before.AcceptedSampleSequence);
            if (estimate.EffectiveLowerNanoseconds <= 0 || estimate.SelectedProgress <= 0 || estimate.PeerProgress <= 0
                || estimate.AcceptedSamples < 2)
            {
                invalid = true;
                reasons.Add("measurement-reach-or-duration");
            }
            foreach (var boundary in new[] { before, after })
            {
                if (boundary.Cooperative is null) { invalid = true; reasons.Add("cooperative-ownership-missing"); }
                else if (boundary.Cooperative.Selected is not null || boundary.Cooperative.Peer is not null)
                    reasons.Add("native-ownership-at-boundary");
                if (boundary.Selected.Ledger is not { } selected || boundary.Peer.Ledger is not { } peer)
                { invalid = true; reasons.Add("ledger-observation-missing"); }
                else if (selected.Local != boundary.Global || peer.Local != boundary.Global)
                    reasons.Add("unqualified-ledger-residual");
            }
            if (sourceSamples < 0 || (ulong)sourceSamples < after.AcceptedSampleSequence)
            { invalid = true; reasons.Add("source-prefix-missing"); }
            else if ((ulong)sourceSamples != after.AcceptedSampleSequence) reasons.Add("source-tail-outside-frozen-prefix");
        }
        reasons.Add("sample-boundary-membership-unqualified");
        reasons.Add("instrumentation-attribution-unqualified");
        return new(invalid ? "invalid" : "unqualified", reasons.Distinct().ToArray(), estimate);
    }
}
