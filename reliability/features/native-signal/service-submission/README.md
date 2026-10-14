# Native service submission diagnostic

Run: `artifacts/linux-sampled-service/20261014T172003975-1dee742a`.
Command: `dotnet run --project build/build.fsproj -- -t LinuxServiceSubmissionTiming`.

This capture uses the initialized workload with optional submission timing.
The manifest records source, binary, and container hashes.
`summary.json` reports `inputsUnchanged: true`.
The target failed. The failed treatment remains retained.

| Case | Result |
| --- | --- |
| Native contract checks | Pass |
| Managed accounting and timing checks on Linux | Pass |
| Initialization checks at zero and 50 percent | Pass |
| Zero-speedup workload | Pass |
| 50-percent workload | Fail during measurement |

The treatment failure boundary reports 2,999,990 ns of peer debt at 685.6945 ms.
The limit remains 2,000,000 ns.
The workload records 1,024 live terminal service journals and no pending service journals.
Targets joined and the session restored.
Both `protocolPassed` and `capacityPassed` are false.
The measurement did not complete its required duration.

## Submission intervals

All figures below exclude the separately retained transport initialization command.
The captured `Stopwatch` frequency is 1,000,000,000 ticks per second.

| Observation | Maximum |
| --- | ---: |
| Managed submission bracket | 61,469 ns |
| API mutex acquisition interval | 513 ns |
| Owner mutex acquisition interval | 190 ns |

Command 382 has the largest managed submission bracket.
Its native span is 60,609 ns, including 60,212 ns inside owner submission after mutex acquisition.
These elapsed intervals do not establish execution or scheduling causes.

The final live command, 1025, has an 11,617 ns managed submission bracket.
Its final child has native observation time 346291160483986 ns.
The child's first managed receipt upper bound is 346291166180603 ns.
The difference is 5,696,617 ns in the same native monotonic clock domain.
This upper bound includes snapshot copying and the subsequent receipt clock call.
It does not identify the exact first managed receipt time.

This capture does not show millisecond-scale submission intervals.
The prefix analysis below narrows the next investigation to native snapshot publication and controller scheduling.
It does not establish a scheduling cause.
The diagnostic adds clock reads, allocation, and journal retention.
It does not establish general service capacity or physical prediction accuracy.

## Snapshot publication bound

`AnalyzeServiceSnapshotLag.py` correlates retained boundary prefixes with later journals by slot, identity, command, budget, and exact native child prefix.
At capture time, five focused checks passed with `python native/ServiceSnapshotLagChecks.py`.
The analysis of this treatment is retained as `snapshot-lag-50.json` beside the workload record.
It matches 59 prefixes, finds no unmatched prefix, and identifies one delayed publication.

Boundary 91 retains command 1025 with zero children and capture time 346291166100974 ns.
The final journal contains the first child with native observation time 346291160019616 ns.
The difference is 6,081,358 ns.

The source contract establishes the ordering:

- `Owner::snapshot` copies the owner journal while holding the owner mutex.
- `Owner::run` calls `advance` while holding that same mutex.
- `advance` reads the native acknowledgment and appends it before releasing the mutex.
- `read_result` captures `observed_ns` during that acknowledgment read.
- `coz_service_snapshot` captures `captured_ns` after the owner copy and ABI output copy.

Thus, the empty owner copy precedes the first child's native observation.
At least 6,081,358 ns elapsed after the empty owner copy and before `captured_ns`.
This interval is inside the native snapshot call, after owner mutex acquisition and copying.
It excludes managed receipt parsing and waiting to acquire the owner mutex.
It can include controller descheduling or interruption during native execution.

The surrounding runtime counter snapshots report unchanged collection counts: Gen0 11, Gen1 2, and Gen2 1.
Both report 109,500 cumulative GC pause ticks.
These counters do not provide an event trace or establish a scheduling cause.
The next diagnostic should capture controller scheduling alongside native snapshot intervals.

See [the timing contract](../../../../docsSrc/Reference/native-service-accounting.md#native-submission-timing)
and [the initialized comparison](../service-initialization/README.md).
