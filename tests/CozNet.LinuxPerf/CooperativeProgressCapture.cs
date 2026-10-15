using System.Diagnostics;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
record struct CooperativeNativeProgress
{
    public ulong Generation, Ordinal, Phase;
}

sealed record CooperativeProgressCapture(long Started, long Finished, int? Status,
    CooperativeNativeProgress? Snapshot, string? Failure)
{
    internal static CooperativeProgressCapture Read(ulong generation,
        Func<(int Status, CooperativeNativeProgress Snapshot)> query)
    {
        long started = Stopwatch.GetTimestamp();
        int? status = null;
        CooperativeNativeProgress? snapshot = null;
        try
        {
            var result = query();
            long finished = Stopwatch.GetTimestamp();
            status = result.Status;
            snapshot = result.Snapshot;
            if (result.Status == 0 && (generation is 0 or > uint.MaxValue || result.Snapshot.Generation != generation ||
                result.Snapshot.Ordinal > 100000 || result.Snapshot.Phase is < 1 or > 10 ||
                result.Snapshot.Phase == 1 && result.Snapshot.Ordinal != 0 ||
                result.Snapshot.Phase is >= 4 and <= 8 && result.Snapshot.Ordinal == 0))
                return new(started, finished, status, snapshot, "Invalid native progress snapshot.");
            return new(started, finished, status, snapshot, null);
        }
        catch (Exception error)
        {
            return new(started, Stopwatch.GetTimestamp(), status, snapshot, error.ToString());
        }
    }
}
