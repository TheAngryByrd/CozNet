sealed class BoundSelectedCadencePlan
{
    public SampledSignalPlan Sampling { get; }
    public CooperativeWorkPlan Work { get; }
    public int Batches => Work.Batches;

    private BoundSelectedCadencePlan(SampledSignalPlan sampling, CooperativeWorkPlan work) => (Sampling, Work) = (sampling, work);

    public static BoundSelectedCadencePlan Parse(string percent, string cadence, string batches)
    {
        var sampling = SampledSignalPlan.Parse(percent, cadence);
        return new(sampling, CooperativeWorkPlan.Parse(PhysicalWorkMode.Baseline, batches));
    }
}

readonly record struct BoundSelectedAdmission(int SampleCount, ulong Epoch, bool Qualified);

sealed class BoundSelectedCheckCadence(BoundSelectedCadencePlan plan, bool nominalEvents = false)
{
    private ulong epoch = 1;
    private int samples, epochSamples;
    private bool ready;
    public long Checks { get; private set; }

    public void Observe(ulong sampleEpoch)
    {
        if (sampleEpoch != epoch) throw new InvalidDataException("A cadence sample belongs to another eligibility epoch.");
        samples = checked(samples + 1);
        epochSamples = checked(epochSamples + 1);
        ready = nominalEvents || epochSamples > 1;
    }

    public bool TryCheck(out BoundSelectedAdmission admission)
    {
        admission = new(samples, epoch, ready);
        if (plan.Sampling.Cadence == SelectedSignalCadenceMode.QualifiedSample && !ready) return false;
        Checks = checked(Checks + 1);
        ready = false;
        return true;
    }

    public void Resume()
    {
        epoch = checked(epoch + 1);
        epochSamples = 0;
        ready = false;
    }
}
