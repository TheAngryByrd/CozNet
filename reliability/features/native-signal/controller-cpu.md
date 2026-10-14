# Controller CPU intervals

The traced workload brackets controller thread CPU readings before request submission and after acknowledgment observation.
The measurement distinguishes observed CPU time from elapsed time. It does not identify why a thread stopped running.
The portable capture helper uses `IThreadCpuClock`. The controlled Linux workload supplies `CLOCK_THREAD_CPUTIME_ID`.
Ordinary sampled workloads do not take these readings.

## Bounds

Let `C` be the difference between controller CPU readings.
Let `W` be the request duration in nanoseconds.
Let `X` be the largest elapsed duration outside the request but inside the two outer clock brackets.

The request CPU interval is between `max(0, C - X)` and `min(W, C)`.
Subtract these bounds from `W` to bound time off CPU.
These are bounds on observed counters. They do not establish the accuracy of the operating system clocks.
The analyzer rejects reversed brackets, decreasing counters, missing readings, and CPU time above the enclosing elapsed interval.
It does not combine CPU intervals from overlapping requests.
It covers completed requests only. Failed or outstanding requests remain in the workload's own failure records.

## Retained comparison

Run: `artifacts/runtime-suspension-comparison/run-20261014T034355418-ac241967`.
The [evidence archive](data/controller-cpu/run-20261014T034355418-ac241967.zip) contains 91 files, verified against the local originals.
It retains all twelve workloads and traces, source snapshots, and the six treatment analyses.
The runner uses the same seed, twelve-case order, and limits as the [suspension comparison](runtime-suspension.md).
The CPU readings change the diagnostic workload. Compare these runs as diagnostics, not as paired performance trials.
All twelve protocol checks pass. All six zero-percent cases pass capacity.
Two treatments without forced GC exceed the observed debt limit.

| Block | Forced GC | Peak peer debt | Capacity |
| --- | --- | --- | --- |
| 1 | No | 2.449846 ms | Fail |
| 1 | Yes | 0.699756 ms | Pass |
| 2 | No | 0.999451 ms | Pass |
| 2 | Yes | 0.599566 ms | Pass |
| 3 | No | 5.999896 ms | Fail |
| 3 | Yes | 0.755007 ms | Pass |

All six treatment CPU analyses pass. All six treatment traces pass suspension correlation.
The marker and CPU-bracket capture checks pass on Windows and Linux.
Five Python checks pass on Windows. They exercise interval bounds, frequency conversion, invalid input, and measurement ranking.

Selected peer requests show different delay locations:

| Case and request | Elapsed | Controller CPU bounds | Native phase | Suspension overlap |
| --- | --- | --- | --- | --- |
| Block 3, no forced GC, 12926 | 13.026525 ms | 13.022354–13.026525 ms | 12.972839 ms before first handler clock reading | None |
| Block 2, no forced GC, 10243 | 4.153038 ms | 4.152559–4.153038 ms | 4.099717 ms after the last handler clock reading | Present |
| Block 1, forced GC, 19753 | 3.074223 ms | 0.073016–0.074100 ms | 3.021206 ms after the last handler clock reading | Present |
| Block 3, no forced GC, 16464 | 1.442910 ms | At most 0.016400 ms | 1.385284 ms after the last handler clock reading | None |

Request 12926 remains pending at the 5.999896 ms peer-debt peak.
Its measured native wait is 49,377 ns. The controller remains active before the first handler clock reading.
This contradicts controller starvation as the explanation for that request.
The handler checks metadata and slot state before that reading.
Target scheduling, delayed signal delivery, and handler-prefix execution or descheduling remain unresolved.
The last handler clock reading precedes result publication and return.
The interval after that reading can include remaining handler execution or descheduling before publication.

Request 10243 includes substantial controller CPU time and runtime suspension overlap.
The record does not identify whether that CPU work occurred in application code, runtime code, or the kernel.
Request 16464 includes at least 1.426510 ms off CPU without suspension overlap.
These records do not support one common remedy for all long requests.

## Reproduce

```powershell
dotnet run --project build/build.fsproj -- -t LinuxRuntimeTraceCollect
python native/ControllerCpuChecks.py
python native/AnalyzeControllerCpu.py path/to/workload.json path/to/controller-cpu.json
```

The run directory retains raw workloads and traces.
`controller-cpu.json` includes every completed request interval and the twenty longest measurement requests.
Decimal output values use JSON strings.

## Next decision

Measure target CPU activity before the first handler clock reading.
Use that evidence to select any accounting or wait-actuator change.
Keep controller execution costs and time off CPU separate in later analysis.
The public support scope and two-percentage-point prediction gate remain unchanged.
