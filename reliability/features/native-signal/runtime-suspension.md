# Runtime suspension correlation

## Result

The first traced workload passes protocol and capacity checks.
It does not reproduce the retained forced-GC capacity failures.
The trace provides usable clock alignment and complete suspension sequences inside the run markers.
Overlap establishes timing correlation, not the cause of a thread delay.

| Observation | Result |
| --- | --- |
| Run | `run-20261014T031956228` |
| Runtime container | .NET SDK 10.0.100 |
| Treatment | 50 percent, spin, forced GC, qualified samples, warm startup |
| Reported lost events | 0 |
| Clock markers | 6 |
| Offset intersection width | 9,295 ns |
| Complete suspension episodes inside run markers | 104 |
| Pairing anomalies | 0 |
| Completed signal requests | 20,493 |
| Requests with possible envelope overlap | 59 |
| Measurement requests with possible envelope overlap | 53 |
| Peak selected debt | 59,685 ns |
| Peak peer debt | 799,837 ns |
| Peak-debt service intervals with possible envelope overlap | 0 |

All 104 paired episodes report suspension reason 1, `SuspendForGC`.
The full trace contains 108 occurrences of each suspension boundary.
Correlation includes only events between the opening and closing run markers.
The collector enables only the runtime GC and CozNet marker providers.
It uses a 64 MiB buffer, disables rundown, and runs under a 30-second watchdog.
The workload keeps the existing request, handler, acknowledgment, GC-call, and debt bounds.

## Reproduce the analysis

Install the pinned collector in the repository root:

```powershell
dotnet tool install dotnet-trace --tool-path lode/tmp/diagnostic-tools --version 10.0.750501 --allow-roll-forward
```

Decode and correlate a retained run:

```powershell
$run = (Get-Content artifacts/runtime-suspension/last-run.txt).Trim()
./native/ExportRuntimeTrace.ps1 -TracePath "$run/trace.nettrace" -OutputPath "$run/events.json"
python native/AnalyzeRuntimeSuspension.py "$run/workload.json" "$run/events.json" "$run/correlation.json"
python native/RuntimeSuspensionChecks.py
```

The analyzer uses decimal arithmetic. Decimal output values are JSON strings.
It reports lower and upper overlap bounds. These include clock-offset uncertainty.
An overlap includes managed request publication and acknowledgment observation. It does not isolate handler execution.
The decoder retains event IDs, versions, process IDs, thread IDs, payloads, and relative timestamps.
It reports the TraceEvent version and event loss.

Eight regression checks pass on the current Windows host.
They cover offset intersection, marker failures, event loss, interleaved CLR instances, incomplete sequences, unknown versions, and overlap bounds.
The full-analysis check covers requests and peak intervals with a known suspended-core overlap.
The coverage check rejects missing runtime events, unmatched forced collections, and reuse of one episode for two forced collections.
These checks do not prove general CLR safety or prediction accuracy.

## Planned comparison

`LinuxRuntimeTraceCollect` runs three randomized blocks with seed `20261019`.
Each block contains zero and 50 percent, with and without forced GC.
All cases use warm startup, qualified samples, and spin.
The retained run is `run-20261014T032810634-3652795a` under `artifacts/runtime-suspension-comparison/`.
The [comparison archive](data/runtime-suspension/run-20261014T032810634-3652795a.zip) contains 108 files, verified against the local originals.
It includes raw traces, workload records, collection commands, measured snapshots, both analysis outputs, and the revised analyzer.
The target completes every case before returning failure.
The marker checks pass on both Windows and Linux.
The recorded source and binary hashes remain unchanged across the comparison.

```powershell
dotnet run --project build/build.fsproj -- -t LinuxRuntimeTraceCollect
```

All six zero-percent cases pass protocol and capacity checks.
Four treatment cases fail capacity. One also fails a protocol bound.

| Block | Forced GC | Peak peer debt | Protocol | Capacity |
| --- | --- | --- | --- | --- |
| 1 | No | 8.699512 ms | Pass | Fail |
| 1 | Yes | 4.499863 ms | Pass | Fail |
| 2 | No | 3.499904 ms | Fail | Fail |
| 2 | Yes | 1.136412 ms | Pass | Pass |
| 3 | No | 0.500193 ms | Pass | Pass |
| 3 | Yes | 4.450037 ms | Pass | Fail |

Block 2 without forced GC records a 9.50 ms handler interval for peer request 16128.
Its measurement stops before the closing measurement marker.
The analyzer rejects full correlation for that case. Its raw trace and failure remain available.
The other eleven cases pass marker alignment and event pairing.
Reanalysis with the collection-coverage checks preserves these outcomes in `correlation-reviewed.json`.

Selected requests show different delay locations:

| Case and peer request | Request duration | Native wait | Main delay | Suspension overlap |
| --- | --- | --- | --- | --- |
| Block 1, no forced GC, 4877 | 17.41 ms | 0.05 ms | 17.35 ms after handler completion | None |
| Block 1, forced GC, 11867 | 10.80 ms | 0.05 ms | 8.55 ms after handler completion | Envelope overlap between 2.294274 and 2.312810 ms |
| Block 3, forced GC, 4538 | 7.03 ms | 0.05 ms | 5.31 ms before the first handler clock reading | None |

These are completed request intervals, not per-thread suspension intervals.
The overlap bounds use managed timestamps and the marker alignment.
The native phase durations use their separate clock contract.
No peak-debt service interval in these three completed failed treatments overlaps a suspension envelope.
The preceding request can still overlap one, as request 11867 demonstrates.
These records do not justify subtracting GC time from debt accounting.

## Remaining work

Distinguish controller execution from scheduling delay during long acknowledgment-observation intervals.
Inspect the controller sample-drain and accounting costs before changing the wait actuator.
Keep the physical-change prediction gate at two percentage points.

See [event semantics](../../../docsSrc/Research/runtime-suspension-events.md),
[the trace contract](../../../lode/profiling/runtime-suspension-trace.md),
and [the retained capacity failures](spin-reliability.md).
