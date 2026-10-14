enum SelectedSignalCadenceMode { Continuous, QualifiedSample }

sealed class SelectedSignalCadence
{
    private readonly SelectedSignalCadenceMode mode;
    private ulong previousEpoch;
    private bool due;

    public SelectedSignalCadence(SelectedSignalCadenceMode mode) => this.mode = mode;

    public void Observe(ulong epoch)
    {
        if (epoch < previousEpoch) throw new InvalidOperationException("The selected sampling epoch decreased.");
        due = epoch == previousEpoch;
        previousEpoch = epoch;
    }

    public bool TryCheck()
    {
        bool result = mode == SelectedSignalCadenceMode.Continuous || due;
        due = false;
        return result;
    }
}
