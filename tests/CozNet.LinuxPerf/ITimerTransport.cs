interface ITimerTransport
{
    (int Status, ulong Generation)? LastSubmission { get; }
    TimerNativeCapture? LastRaw { get; }
    ulong Submit(uint slot, ulong command, ulong budget);
    TimerNativeSnapshot Probe(uint slot);
    TimerObservation Read(uint slot);
    void Cancel(uint slot);
    void ReleaseAfterRetiredCopy(uint slot);
}
