using CozNet;

sealed class PeerSignalCadence
{
    private SignalAccounting? checkedState;

    public bool NeedsCheck(SignalAccounting state) => !ReferenceEquals(state, checkedState);

    public void RecordChecked(SignalAccounting resultingState) => checkedState = resultingState;
}
