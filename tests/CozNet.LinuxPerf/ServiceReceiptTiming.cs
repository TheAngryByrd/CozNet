sealed class ServiceReceiptTiming
{
    public ulong UpperNanoseconds { get; }
    private ServiceReceiptTiming(ulong upper) => UpperNanoseconds = upper;

    public static ServiceReceiptTiming Parse(ulong started, ulong nativeObserved, ulong firstSnapshotCaptured,
        ulong firstManagedReceiptUpper, int clockStatus)
    {
        if (clockStatus != 0 || started == 0 || nativeObserved < started || firstSnapshotCaptured < nativeObserved
            || firstManagedReceiptUpper < firstSnapshotCaptured || firstManagedReceiptUpper - started > 100000000)
            throw new InvalidDataException("The first managed service receipt cannot satisfy the 100 ms bound.");
        return new(firstManagedReceiptUpper - started);
    }
}
