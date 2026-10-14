# Cached native snapshot cost

The [Linux run](summary.json) completes the native checks, receipt parser, accounting checks, and snapshot cost fixture.
The [manifest](manifest.json) retains source, native binary, managed binary, and image hashes.
The runner confirms unchanged sources and binaries during the run.
All four process outcomes have exit code zero.

The fixture registers a real managed target with an ineligible instruction range.
The target returns one deferred acknowledgment with zero wait.
Its first managed receipt upper bound is 1,180,875 ns, below the unchanged 100,000,000 ns gate.
The accounting reservation settles once and leaves its original debt unpaid.
After a 150 ms pause, repeated snapshots still preserve the accepted first receipt.
The target remains registered during measurement.
It then retires and joins before the native session closes.

## Measurements

The measured path is `NativeServiceClient.Read` for a completed one-child journal.
It includes native snapshot copying, a native clock call, managed cache access, and managed array copies.
It excludes native service submission, first-receipt insertion, and accounting settlement.
The caller retains 2,000 observations in a preallocated buffer and validates them after each batch.
This retention differs from a controller that immediately discards each observation.

| Batch | Calls | Mean ns/call | Managed bytes/call | GC collections |
| --- | ---: | ---: | ---: | ---: |
| 0 | 2,000 | 582.5415 | 168 | 0 |
| 1 | 2,000 | 591.2075 | 168 | 0 |
| 2 | 2,000 | 586.2385 | 168 | 0 |
| 3 | 2,000 | 593.4570 | 168 | 0 |
| 4 | 2,000 | 3,471.8770 | 168 | 0 |

The [raw record](snapshot-cost.json) retains all five batches and teardown results.
The workload uses 256 warmup reads before the 10,000 measured reads.
Every returned header and cached child must match the completed journal.
Only the capture timestamp can change. It cannot decrease.
No timing threshold selects or removes a batch.

## Command and limits

```powershell
dotnet run --project build -t LinuxServiceSnapshotCost
```

The Linux container has no network, drops all capabilities, and uses a 30-second process watchdog.
The portable measurement helper receives a read delegate. Only the fixture requires the Linux x64 native backend.
The [native checks](native-checks.log) pass 6/6.
This run does not isolate native allocations or measure larger journals, first-receipt insertion, or active-service contention.
It does not establish general CLR safety, sampled capacity, or physical prediction accuracy.
See [the workload contract](../../../../lode/profiling/service-snapshot-cost.md).
