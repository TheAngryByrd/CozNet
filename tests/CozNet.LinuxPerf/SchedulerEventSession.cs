enum SchedulerRecordKind { SwitchIn, SwitchOut, Loss }

readonly record struct SchedulerRecord(SchedulerRecordKind Kind, ushort Flags, ulong TimestampNanoseconds, ulong Lost);
sealed record SchedulerCapture(int ProcessId, int ThreadId, ulong EnableBefore, ulong EnableAfter,
    ulong DisableBefore, ulong DisableAfter, ulong? FinalLost, SchedulerRecord[] Records, string[] Failures);

interface ISchedulerEventSession : IDisposable
{
    SchedulerCapture Finish();
}

interface IServiceOwnerSchedulerBackend
{
    ISchedulerEventSession CaptureOwnerScheduling();
}
