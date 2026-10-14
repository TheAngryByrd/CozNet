# Target CPU observations

The optional target CPU path reads the registered target's CPU clock from the controller.
It brackets each read with native monotonic time.
It leaves the signal handler source unchanged.
The capture uses the same 50-microsecond request, 5-millisecond handler, and 2-millisecond debt limits.

## Retained capture

Run `run-20261014T040401653-ffb4ad16` contains one fixed block of four cases.
The [evidence archive](data/target-cpu/run-20261014T040401653-ffb4ad16.zip) contains 71 files, verified against the retained originals.
Its SHA256 is `d26f33b5a065d345ee33d6d399aa88e694b9ace037a435be78d002a220bcdeee`.
The capture snapshot and later analysis snapshot remain separate.
The capture command selected one repetition explicitly:

```powershell
pwsh -NoProfile -File native/CollectLinuxRuntimeTraces.ps1 -TargetCpu -Repetitions 1 -EvidenceDirectory artifacts/target-cpu
```

The later `LinuxTargetCpuCollect` FAKE target defaults to three repetitions.
Each case uses the traced workload with qualified-sample cadence, warmup, and spin waiting.
The collector confirms unchanged sources and binaries during capture.
The two zero-percent cases pass protocol and capacity checks.
They send no requests, so they do not measure remote clock-read overhead.

| Treatment | Completed requests | Protocol | Capacity | Target clock consistency | Suspension correlation |
| --- | ---: | --- | --- | --- | --- |
| No forced GC | 21,962 | Pass | Fail | Pass | Pass |
| Forced GC | 6,050 | Fail | Fail | Fail: five records | Fail: incomplete markers |

All completed treatment requests contain both target CPU observations.
The no-GC treatment reaches 2.499918 ms of peer debt.
The GC treatment reaches 6.499937 ms and fails its measurement requirements.
Twenty forced collections start after measurement begins and finish before the failure boundary.
The workload lacks a completed measurement record.
That missing record triggers its misleading `No forced collection completed inside the sampled measurement window.` diagnostic.
The case is an aborted measurement, not evidence that no forced collections completed.
Do not describe it as a successful forced-GC comparison.

## Conditional execution bounds

`native/AnalyzeTargetCpu.py` checks identity, native phase ordering, CPU brackets, and counter consistency.
It retains each invalid request and fails the complete analysis if any request is invalid.
It separates warmup from measurement and analyzes completed requests only.
The backend metadata must identify the native monotonic clock and nanosecond units.

Let `C` be the target CPU delta and `G` the interval before the first handler clock reading.
Let `X` include all outer-bracket wall time outside `G`.
Under exact clock accounting, CPU inside `G` lies between `max(0, C-X)` and `min(G, C)`.
The report names these fields `modelGapCpuLowerNanoseconds` and `modelGapCpuUpperNanoseconds`.
They are conditional model bounds, not established nanosecond measurement accuracy.
Reported clock resolution does not establish accuracy.

The GC treatment contains these inconsistencies:

| Request sequence | CPU beyond the enclosing wall interval |
| ---: | ---: |
| 1888 | 7,775 ns |
| 2096 | 1,391 ns |
| 4409 | 1,727 ns |
| 4621 | 607 ns |
| 5638 | 5,000 ns |

The analyzer rejects these records without clipping them or introducing a tolerance.
Clock accounting, read overhead, and blocked-phase behavior need further investigation.
No target execution conclusion from the rejected comparison is established.

## Failure evidence

No-GC peer request 4923 lasts 5.123800 ms in the managed request interval.
It records 49,527 ns of native wait and 5.070678 ms after the last handler clock reading.
The target CPU delta is 5.126300 ms across its wider observation brackets.
The controller CPU delta is 72,100 ns across its observation brackets.
The controller analyzer bounds request CPU at 66,807–72,100 ns under its clock model.
The next request, 4924, is pending at the peak-debt observation.
The peak service interval has no runtime suspension overlap.

These records support investigation of controller time off CPU for this failure.
They do not distinguish blocking from runnable descheduling.
They do not establish the exact handler return or result-publication time.
The earlier retained failure with an active controller remains unexplained by controller starvation.

GC peer request 6050 records 2.545088 ms before the first handler clock reading.
Its measured handler interval is 10.167457 ms.
Its native wait is 10.166854 ms, despite a 50-microsecond requested delay.
The incomplete trace cannot establish whether suspension overlaps that request.
The failed clock analysis prevents treating its target CPU model bounds as established evidence.

## Verification scope

Nine Python regression checks cover the model arithmetic and rejection paths.
Native `TargetCpuChecks` passes with capture enabled and disabled.
It compares remote CPU observations with the live target's own clock.
Six existing native signal contract modes also pass.
The managed snapshot parser checks pass on Windows and Linux.
The existing handler disassembly gate passes.
Raw handler bytes change with relocation, so byte identity is not claimed.

```powershell
python native/TargetCpuChecks.py
```

General CLR safety, diagnostic overhead, and causal prediction accuracy remain unverified.
See [target clock ownership](../../../lode/profiling/target-cpu-observations.md) and [controller CPU evidence](controller-cpu.md).
