# Native snapshot path cost

`LinuxServiceSnapshotCost` measures `NativeServiceClient.Read` against one completed native journal.
The target registers with an ineligible instruction range and returns one deferred child with zero measured wait.
The existing interop fixture validates first receipt, ledger settlement, and unchanged receipt bounds before cost measurement.
The target stays alive until the native lease is released, then retires and joins before session close.

```powershell
dotnet run --project build -t LinuxServiceSnapshotCost
```

`ServiceSnapshotCosts` uses a portable read delegate.
The native client supplies the Linux x64 transport.
After 256 warmup reads, five batches each perform 2,000 reads.
The timed region includes native snapshot copying, the clock call, receipt-cache access, managed copies, and loop stores.
It excludes journal validation and report serialization.
One preallocated buffer holds up to 2,000 returned observations for validation after each batch.
This retention differs from a controller that discards each snapshot immediately.

Every returned snapshot must preserve the completed header, child record, and cached first receipt.
Only the snapshot capture timestamp can change, and it cannot decrease.
The report retains batch elapsed ticks, thread allocation counts, collection counts, and failures.
The existing Linux runner supplies a 30-second process watchdog and retains source and binary hashes.
The cost option does not rerun the separate normal and deliberately late interop processes.

```mermaid
flowchart LR
    RegisteredTarget --> DeferredChild --> ReceiptValidation
    ReceiptValidation --> Warmup --> ReadBatches --> Release
    Release --> Retire --> Join --> Close
```

This measures the combined read path for a one-child cached journal.
The [retained Linux run](../../reliability/features/native-signal/service-snapshot-cost/README.md) passes 10,000 measured reads and native teardown.
Each batch allocates 168 managed bytes per read. All five timing batches remain in the report.
It does not isolate native allocation, first-receipt insertion, larger journals, or active service contention.
It does not establish sampled capacity or prediction accuracy.
See [service interop](service-interop.md), [adapter costs](service-accounting-cost.md), and [the service plan](../plans/native-debt-service.md).
