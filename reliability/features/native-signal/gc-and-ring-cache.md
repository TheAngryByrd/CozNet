# Controller allocation and empty ring drains

The sampled controller allocates gigabytes during a two-second measurement.
Reusing an unchanged ring snapshot reduces this allocation, but capacity remains unstable.
The measurements do not establish that GC caused a specific signal delay.

## Measurement contract

The workload records `GC.GetAllocatedBytesForCurrentThread`, generation collection counts, and `GC.GetTotalPauseDuration`.
Allocation measures managed bytes allocated by the controller. It does not measure live heap size, native allocation, or worker allocation.
Collection counts and pause totals apply to the process. Do not add generation counts together.
Snapshots read their fields sequentially and retain surrounding `Stopwatch` timestamps.
They are not atomic observations or a trace of individual GC pauses.

Each phase retains its starting counters, periodic observations, peak-debt counters, and ending counters.
The counter window starts before sampling enablement and ends during the measurement-boundary snapshot.
It includes diagnostic work near those boundaries. Warmup and settlement remain separate.
The cases use workstation GC, `Interactive` latency mode, and four available processors.

The API contracts match [.NET 10 allocation APIs](https://github.com/dotnet/runtime/blob/v10.0.0/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs#L418-L432),
[pause totals](https://github.com/dotnet/runtime/blob/v10.0.0/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs#L856-L865), and
[collection counts](https://learn.microsoft.com/dotnet/api/system.gc.collectioncount?view=net-10.0).
Pause totals use `TimeSpan` ticks, with 10,000 ticks per millisecond.

## Change

`RingReader.Drain` previously created a `SampleBatch` and sorted the selected symbols on every call.
It now reuses an immutable snapshot when the validated ring head has not advanced.
New records invalidate the snapshot before decoding. Sample, lost, and throttle records all update it.
The selected-symbol collection rejects mutation by the caller.
The reader still checks ring bounds on every call.

The isolated allocation check performs 10,000 warmed empty drains and observes zero managed bytes.
The six ring-reader checks pass on Windows and Linux.

| Requirement | Check |
| --- | --- |
| No allocation or duplicate delivery on empty drains | `EmptyDrainsDoNotAllocate` |
| Fresh records and unchanged earlier snapshots | `NewSampleUpdatesCachedCounters` |
| Lost and throttle counts | `LostAndThrottleRecordsUpdateCachedCounters` |
| Ring wraparound and unmapped samples | `WrappedSampleUpdatesCachedCounters` |
| Invalid head after a cached drain | `CorruptHeadIsRejectedAfterCachedDrain` |
| Caller cannot change cached symbols | `SelectedSymbolsCannotBeChangedByCaller` |

The [mutation archive](data/gc-ring-cache/ring-reader-mutation.zip) contains isolated source snapshots, commands through project paths, logs, and hashes.
Both snapshots use the same checks and .NET 10 SDK image.
The unmodified extracted reader passes all six checks.
Removing only the cached-return guard makes `EmptyDrainsDoNotAllocate` fail with 2,080,000 allocated bytes.
The mutant exits with code 134. This detects that allocation defect, not every possible reader defect.
The repository source contains no mutation.

## Sequential live comparison

Both runs use the same target:

```powershell
dotnet run --project build/build.fsproj -- -t LinuxWarmSampleCadenceVerify
```

Each run contains three fixed zero-then-50-percent blocks, with 250 ms warmup and two-second measurements.
The [uncached run](data/gc-ring-cache/run-20261014T015011760-06c3af12.zip) precedes the code change.
The [cached run](data/gc-ring-cache/run-20261014T015616527-008c7bc0.zip) follows it.
This is a sequential comparison, not a randomized comparison between implementations.
Allocation includes the reader, accounting, controller, and diagnostics.

| Case | Uncached allocation (MiB) | Cached allocation (MiB) | Uncached GC pause (ms) | Cached GC pause (ms) |
| --- | ---: | ---: | ---: | ---: |
| Block 1, zero percent | 3765.032 | 2848.097 | 43.212 | 36.431 |
| Block 2, zero percent | 3772.594 | 2880.186 | 44.545 | 34.239 |
| Block 3, zero percent | 3670.410 | 2854.477 | 48.678 | 36.710 |
| Block 1, 50 percent | 2633.633 | 1539.258 | 67.188 | 42.824 |
| Block 2, 50 percent | 2658.000 | 1549.269 | 64.630 | 42.918 |
| Block 3, 50 percent | 2624.675 | 1526.571 | 69.286 | 40.409 |

The median 50-percent allocation is about 42 percent lower in the cached run.
Generation-zero count changes fall from 175–178 to 102–103 in those cases.
All twelve protocol cases pass. All six zero-percent capacity cases pass.
All three uncached 50-percent capacity cases pass. Two of three cached cases pass.
The third cached case records 5.124952 ms of peak peer debt and fails the unchanged 2 ms limit.
Lower allocation therefore does not establish stable capacity or explain the capacity difference between these runs.

Requested wait, handler interval, and acknowledgment limits remain 50 µs, 5 ms, and 100 ms.
The two-percentage-point prediction gate remains unchanged. This comparison does not test prediction accuracy.
No forced GC is enabled in these runs.

## Evidence and next step

Both archives contain raw counter snapshots and derived `counter-analysis.json`.
Counter snapshots remain monotonic within each phase. Their capture intervals remain ordered.
Credits and waits reconcile across all twenty-four warmup and measurement phases.
The uncached run verifies twenty-one source/binary hashes. The cached run verifies twenty-two.
The uncached archive retains the measured program, runner, and workload assembly that the cache change replaces.
`source-overrides.json` identifies those retained files for `AnalyzeTiming.ps1`.
All archive members match their original files by SHA-256.

Allocation remains high, especially when no signal requests exist.
Inspect repeated accounting work during idle polling before changing signal delivery.
GC counters do not identify which thread was suspended or which pause overlaps a specific request.
Keep the earlier [native phase failures](phase-timing.md) and [capacity failures](sampled.md).
